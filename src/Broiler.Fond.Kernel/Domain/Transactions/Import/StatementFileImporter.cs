using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions.Sync;
using Broiler.Fond.Kernel.Formats.Camt;
using Broiler.Fond.Kernel.Formats.Mt940;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Transactions.Import;

/// <summary>
/// Secure, bounded offline bank statement file importer for ISO 20022 CAMT and SWIFT MT940/MT942 statements.
/// Purely offline, credential-free, preserves observation provenance and entry multiplicity,
/// and commits atomically to <see cref="IProfileStore"/> via MutateAndSave.
/// </summary>
public static class StatementFileImporter
{
    public const int MaxInputFileSize = 10 * 1024 * 1024; // 10 MiB

    /// <summary>
    /// Imports a statement from a string payload into the active profile store.
    /// </summary>
    public static StatementImportResult Import(
        IProfileStore profileStore,
        string fileContent,
        StatementImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(fileContent);

        byte[] bytes = Encoding.UTF8.GetBytes(fileContent);
        return Import(profileStore, bytes, options);
    }

    /// <summary>
    /// Imports a statement from a byte span into the active profile store.
    /// </summary>
    public static StatementImportResult Import(
        IProfileStore profileStore,
        ReadOnlySpan<byte> fileBytes,
        StatementImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        if (fileBytes.IsEmpty)
        {
            return StatementImportResult.Failure("Statement file is empty.", StatementFileFormat.Unknown);
        }

        if (fileBytes.Length > MaxInputFileSize)
        {
            return StatementImportResult.Failure($"Statement file size ({fileBytes.Length} bytes) exceeds maximum limit of {MaxInputFileSize} bytes.", StatementFileFormat.Unknown);
        }

        if (profileStore.State != ProfileStoreOpenState.Ready)
        {
            return StatementImportResult.Failure($"ProfileStore must be in Ready state; current state is {profileStore.State}.", StatementFileFormat.Unknown);
        }

        if (profileStore.IsReadOnly)
        {
            return StatementImportResult.Failure("ProfileStore is opened read-only; cannot import statement files.", StatementFileFormat.Unknown);
        }

        // 1. Detect format
        StatementFileFormat detectedFormat = StatementFileDetector.Detect(fileBytes);
        if (detectedFormat == StatementFileFormat.Unknown)
        {
            return StatementImportResult.Failure(
                "Unrecognized statement file format. Only ISO 20022 CAMT (camt.052, camt.053) and SWIFT MT940/MT942 formats are supported.",
                StatementFileFormat.Unknown);
        }

        // 2. Compute file checksum (SHA-256)
        string fileDigest = Convert.ToHexString(SHA256.HashData(fileBytes));

        // 3. Decode text for parsing
        string fileText;
        try
        {
            // Skip UTF-8 BOM if present
            ReadOnlySpan<byte> textSpan = fileBytes;
            if (textSpan.Length >= 3 && textSpan[0] == 0xEF && textSpan[1] == 0xBB && textSpan[2] == 0xBF)
            {
                textSpan = textSpan[3..];
            }

            fileText = Encoding.UTF8.GetString(textSpan);
        }
        catch
        {
            fileText = Encoding.Latin1.GetString(fileBytes);
        }

        // 4. Parse statements
        IReadOnlyList<Mt940Statement>? mtStatements = null;
        IReadOnlyList<CamtStatement>? camtStatements = null;

        try
        {
            switch (detectedFormat)
            {
                case StatementFileFormat.Mt940:
                case StatementFileFormat.Mt942:
                    mtStatements = Mt940Parser.Parse(fileText);
                    break;

                case StatementFileFormat.Camt052:
                case StatementFileFormat.Camt053:
                    camtStatements = CamtXmlParser.Parse(fileText);
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException or System.Xml.XmlException)
        {
            return StatementImportResult.Failure($"Failed to parse {detectedFormat} statement: {ex.Message}", detectedFormat);
        }

        int statementCount = (mtStatements?.Count ?? 0) + (camtStatements?.Count ?? 0);
        if (statementCount == 0)
        {
            return StatementImportResult.Failure("Statement file contains no valid statements.", detectedFormat);
        }

        // 5. Account matching against the profile store
        StoreSnapshot currentSnapshot = profileStore.CurrentSnapshot
            ?? throw new InvalidOperationException("Active profile store snapshot is null.");

        string firstDeclaredAccount = mtStatements != null
            ? mtStatements[0].AccountIdentification
            : camtStatements![0].AccountIban;

        StoreBindingEntry? matchedBinding = ResolveTargetBinding(currentSnapshot, firstDeclaredAccount, options);
        if (matchedBinding is null)
        {
            return StatementImportResult.Failure(
                $"Unable to match statement account '{firstDeclaredAccount}' to a unique active account binding in profile store. Specify TargetAccountId or TargetAccountBindingId in options.",
                detectedFormat);
        }

        AccountId accountId = new(matchedBinding.AccountId);
        AccountIncarnationId incarnationId = new(matchedBinding.IncarnationId);
        AccountBindingId bindingId = new(matchedBinding.Id);

        DateTimeOffset syncTimestamp = options?.ImportTimestamp ?? DateTimeOffset.UtcNow;
        string formatTag = detectedFormat switch
        {
            StatementFileFormat.Mt940 => "FileImport:MT940",
            StatementFileFormat.Mt942 => "FileImport:MT942",
            StatementFileFormat.Camt053 => "FileImport:CAMT.053",
            StatementFileFormat.Camt052 => "FileImport:CAMT.052",
            _ => "FileImport:UNKNOWN",
        };

        // 6. Gather existing transactions, links, and checkpoint for correlation
        var existingTxDoc = currentSnapshot.TransactionsOrDefault;
        List<TransactionRevision> existingRevisions = ExtractRevisionsForBinding(existingTxDoc, bindingId);
        List<PendingToBookedLink> existingLinks = ExtractLinks(existingTxDoc);
        StatementSyncCheckpoint? existingCheckpoint = ExtractCheckpoint(existingTxDoc, accountId, incarnationId, bindingId);

        // 7. Perform correlation with multiplicity and deduplication
        ulong nextAllocId = currentSnapshot.Profile.NextId;
        List<TransactionCandidate> allNewTransactions = [];
        List<PendingToBookedLink> allNewLinks = [];
        int totalMatchedCount = 0;
        StatementSyncCheckpoint? latestCheckpoint = existingCheckpoint;
        List<TransactionRevision> accumulatedRevisions = new(existingRevisions);

        if (mtStatements is not null)
        {
            foreach (Mt940Statement stmt in mtStatements)
            {
                var corr = StatementOverlapSyncPlanner.CorrelateMt940(
                    stmt,
                    accountId,
                    incarnationId,
                    bindingId,
                    accumulatedRevisions,
                    existingLinks,
                    latestCheckpoint,
                    transactionIdFactory: () => new TransactionId(nextAllocId++),
                    revisionIdFactory: () => new TransactionRevisionId(nextAllocId++),
                    observationIdFactory: () => new ObservationId(nextAllocId++),
                    syncTimestamp: syncTimestamp,
                    continuationToken: null,
                    isComplete: true,
                    statementFormat: formatTag,
                    sourceChecksum: fileDigest);

                allNewTransactions.AddRange(corr.NewTransactions);
                allNewLinks.AddRange(corr.NewLinks);
                totalMatchedCount += corr.ExistingTransactionsMatchedCount;
                latestCheckpoint = corr.UpdatedCheckpoint;
                accumulatedRevisions.AddRange(corr.NewTransactions.Select(t => t.Revision));
            }
        }
        else if (camtStatements is not null)
        {
            foreach (CamtStatement stmt in camtStatements)
            {
                var corr = StatementOverlapSyncPlanner.CorrelateCamt(
                    stmt,
                    accountId,
                    incarnationId,
                    bindingId,
                    accumulatedRevisions,
                    existingLinks,
                    latestCheckpoint,
                    transactionIdFactory: () => new TransactionId(nextAllocId++),
                    revisionIdFactory: () => new TransactionRevisionId(nextAllocId++),
                    observationIdFactory: () => new ObservationId(nextAllocId++),
                    syncTimestamp: syncTimestamp,
                    continuationToken: null,
                    isComplete: true,
                    statementFormat: formatTag,
                    sourceChecksum: fileDigest);

                allNewTransactions.AddRange(corr.NewTransactions);
                allNewLinks.AddRange(corr.NewLinks);
                totalMatchedCount += corr.ExistingTransactionsMatchedCount;
                latestCheckpoint = corr.UpdatedCheckpoint;
                accumulatedRevisions.AddRange(corr.NewTransactions.Select(t => t.Revision));
            }
        }

        // 8. Handle PreviewOnly
        if (options?.PreviewOnly == true)
        {
            return StatementImportResult.Success(
                detectedFormat,
                accountId,
                bindingId,
                transactionsImportedCount: allNewTransactions.Count,
                transactionsMatchedCount: totalMatchedCount,
                linksCreatedCount: allNewLinks.Count,
                issues: null,
                resultingSnapshot: null);
        }

        // 9. Atomic commit to profile store via MutateAndSave
        profileStore.MutateAndSave(snapshot =>
        {
            var txDoc = snapshot.TransactionsOrDefault;
            ulong currentNextId = snapshot.Profile.NextId;

            List<StoreTransactionObservationEntry> obsList = [.. txDoc.Observations];
            List<StoreTransactionEntry> txList = [.. txDoc.Transactions];
            List<StoreTransactionRevisionEntry> revList = [.. txDoc.Revisions];
            List<StorePendingToBookedLinkEntry> linkList = [.. txDoc.Links];
            List<StoreLedgerRecord> ledgerList = [.. snapshot.Ledger.Records];

            foreach (TransactionCandidate candidate in allNewTransactions)
            {
                TransactionObservation obs = candidate.Observation;
                TransactionRevision rev = candidate.Revision;

                obsList.Add(new StoreTransactionObservationEntry(
                    Id: obs.Id.Value,
                    AccountBindingId: obs.AccountBindingId.Value,
                    RetrievedAt: obs.RetrievedAt,
                    StatementFormat: obs.StatementFormat,
                    StatementNumber: obs.StatementNumber,
                    PageToken: obs.PageToken));

                txList.Add(new StoreTransactionEntry(
                    Id: rev.TransactionId.Value,
                    AccountId: rev.AccountId.Value,
                    AccountIncarnationId: rev.AccountIncarnationId.Value,
                    Status: rev.Status.ToString(),
                    Kind: rev.Kind.ToString()));

                revList.Add(new StoreTransactionRevisionEntry(
                    Id: rev.Id.Value,
                    TransactionId: rev.TransactionId.Value,
                    ObservationId: rev.ObservationId.Value,
                    AccountId: rev.AccountId.Value,
                    AccountIncarnationId: rev.AccountIncarnationId.Value,
                    AccountBindingId: rev.AccountBindingId.Value,
                    Status: rev.Status.ToString(),
                    Kind: rev.Kind.ToString(),
                    Amount: rev.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    Currency: rev.Amount.Currency,
                    IsCredit: rev.IsCredit,
                    BookingDate: rev.BookingDate,
                    ValueDate: rev.ValueDate,
                    BankTimestamp: rev.BankTimestamp,
                    RetrievedAt: rev.RetrievedAt,
                    CounterpartyName: rev.Counterparty?.Name,
                    CounterpartyIban: rev.Counterparty?.Iban,
                    CounterpartyBic: rev.Counterparty?.Bic,
                    RemittanceText: rev.Remittance?.UnstructuredText,
                    EndToEndId: rev.Remittance?.EndToEndId,
                    MandateId: rev.Remittance?.MandateId,
                    CreditorId: rev.Remittance?.CreditorId,
                    CustomerReference: rev.Remittance?.CustomerReference,
                    BankReference: rev.Remittance?.BankReference,
                    PurposeCode: rev.Remittance?.PurposeCode,
                    ReturnReasonCode: rev.ReturnReasonCode,
                    RawBankCode: rev.RawBankCode));

                // Add ledger records for all newly allocated identities
                ledgerList.Add(new StoreLedgerRecord(
                    Id: obs.Id.Value,
                    Kind: LocalEntityKind.Observation.ToString(),
                    SupersedesId: null,
                    References: [(obs.AccountBindingId.Value, LocalEntityKind.AccountBinding.ToString())]));

                ledgerList.Add(new StoreLedgerRecord(
                    Id: rev.TransactionId.Value,
                    Kind: LocalEntityKind.Transaction.ToString(),
                    SupersedesId: null,
                    References:
                    [
                        (rev.AccountId.Value, LocalEntityKind.Account.ToString()),
                        (rev.AccountIncarnationId.Value, LocalEntityKind.AccountIncarnation.ToString()),
                    ]));

                ledgerList.Add(new StoreLedgerRecord(
                    Id: rev.Id.Value,
                    Kind: LocalEntityKind.TransactionRevision.ToString(),
                    SupersedesId: null,
                    References:
                    [
                        (rev.TransactionId.Value, LocalEntityKind.Transaction.ToString()),
                        (rev.ObservationId.Value, LocalEntityKind.Observation.ToString()),
                        (rev.AccountId.Value, LocalEntityKind.Account.ToString()),
                        (rev.AccountIncarnationId.Value, LocalEntityKind.AccountIncarnation.ToString()),
                        (rev.AccountBindingId.Value, LocalEntityKind.AccountBinding.ToString()),
                    ]));
            }

            foreach (PendingToBookedLink link in allNewLinks)
            {
                linkList.Add(new StorePendingToBookedLinkEntry(
                    PendingTransactionId: link.PendingTransactionId.Value,
                    BookedTransactionId: link.BookedTransactionId.Value,
                    LinkedAt: link.LinkedAt,
                    MatchCriterion: link.MatchCriterion));
            }

            // Update checkpoint
            List<StoreStatementSyncCheckpointEntry> cpList = [];
            foreach (var cp in txDoc.Checkpoints)
            {
                if (cp.AccountId != accountId.Value || cp.AccountBindingId != bindingId.Value)
                {
                    cpList.Add(cp);
                }
            }

            if (latestCheckpoint is not null)
            {
                cpList.Add(new StoreStatementSyncCheckpointEntry(
                    AccountId: latestCheckpoint.AccountId.Value,
                    AccountIncarnationId: latestCheckpoint.AccountIncarnationId.Value,
                    AccountBindingId: latestCheckpoint.AccountBindingId.Value,
                    WindowStart: latestCheckpoint.WindowStart,
                    WindowEnd: latestCheckpoint.WindowEnd,
                    LastSyncTimestamp: latestCheckpoint.LastSyncTimestamp,
                    LastContinuationToken: latestCheckpoint.LastContinuationToken,
                    IsComplete: latestCheckpoint.IsComplete,
                    ObservedTransactionCount: latestCheckpoint.ObservedTransactionCount));
            }

            var updatedTransactions = new StoreTransactionsDocument(
                ProfileId: snapshot.Profile.ProfileId,
                BranchId: snapshot.Profile.BranchId,
                Generation: snapshot.Profile.Generation,
                Observations: obsList,
                Transactions: txList,
                Revisions: revList,
                Annotations: txDoc.Annotations,
                Links: linkList,
                Checkpoints: cpList);

            var updatedLedger = snapshot.Ledger with
            {
                Records = ledgerList,
            };

            var updatedProfile = snapshot.Profile with
            {
                NextId = Math.Max(currentNextId, nextAllocId),
            };

            return snapshot with
            {
                Profile = updatedProfile,
                Ledger = updatedLedger,
                Transactions = updatedTransactions,
            };
        }, syncTimestamp);

        return StatementImportResult.Success(
            detectedFormat,
            accountId,
            bindingId,
            transactionsImportedCount: allNewTransactions.Count,
            transactionsMatchedCount: totalMatchedCount,
            linksCreatedCount: allNewLinks.Count,
            issues: null,
            resultingSnapshot: profileStore.CurrentSnapshot);
    }

    /// <summary>
    /// Imports a statement from a stream into the active profile store.
    /// </summary>
    public static StatementImportResult Import(
        IProfileStore profileStore,
        Stream fileStream,
        StatementImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(fileStream);

        using MemoryStream ms = new();
        fileStream.CopyTo(ms);
        return Import(profileStore, ms.ToArray(), options);
    }

    private static StoreBindingEntry? ResolveTargetBinding(
        StoreSnapshot snapshot,
        string declaredAccount,
        StatementImportOptions? options)
    {
        // 1. Explicit target binding
        if (options?.TargetAccountBindingId is { } targetBindingId)
        {
            return snapshot.Accounts.Bindings.FirstOrDefault(b => b.Id == targetBindingId.Value);
        }

        // 2. Explicit target account
        if (options?.TargetAccountId is { } targetAccountId)
        {
            var matching = snapshot.Accounts.Bindings
                .Where(b => b.AccountId == targetAccountId.Value && string.Equals(b.State, "Active", StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matching.Count == 1 ? matching[0] : null;
        }

        // 3. Auto-matching by IBAN or Domestic Account Number
        string cleanDeclared = CleanAccountIdentifier(declaredAccount);
        if (string.IsNullOrEmpty(cleanDeclared))
        {
            return null;
        }

        var activeBindings = snapshot.Accounts.Bindings
            .Where(b => string.Equals(b.State, "Active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Exact IBAN match
        var ibanMatches = activeBindings
            .Where(b => !string.IsNullOrEmpty(b.Iban) && string.Equals(CleanAccountIdentifier(b.Iban), cleanDeclared, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (ibanMatches.Count == 1)
        {
            return ibanMatches[0];
        }

        // Domestic Account Number match
        var domesticMatches = activeBindings
            .Where(b => !string.IsNullOrEmpty(b.DomesticAccountNumber) && string.Equals(CleanAccountIdentifier(b.DomesticAccountNumber), cleanDeclared, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (domesticMatches.Count == 1)
        {
            return domesticMatches[0];
        }

        // SWIFT slash format: BLZ/AccountNumber
        if (declaredAccount.Contains('/'))
        {
            string[] parts = declaredAccount.Split('/');
            if (parts.Length == 2)
            {
                string cleanBankCode = CleanAccountIdentifier(parts[0]);
                string cleanAccNum = CleanAccountIdentifier(parts[1]);

                var blzAccMatches = activeBindings
                    .Where(b => !string.IsNullOrEmpty(b.DomesticBankCode) &&
                                !string.IsNullOrEmpty(b.DomesticAccountNumber) &&
                                string.Equals(CleanAccountIdentifier(b.DomesticBankCode), cleanBankCode, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(CleanAccountIdentifier(b.DomesticAccountNumber), cleanAccNum, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (blzAccMatches.Count == 1)
                {
                    return blzAccMatches[0];
                }
            }
        }

        return null;
    }

    private static string CleanAccountIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }

        StringBuilder sb = new();
        foreach (char c in identifier)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static List<TransactionRevision> ExtractRevisionsForBinding(
        StoreTransactionsDocument txDoc,
        AccountBindingId bindingId)
    {
        List<TransactionRevision> revisions = [];
        foreach (StoreTransactionRevisionEntry rev in txDoc.Revisions)
        {
            if (rev.AccountBindingId != bindingId.Value)
            {
                continue;
            }

            Counterparty? counterparty = (rev.CounterpartyName != null || rev.CounterpartyIban != null)
                ? new Counterparty(rev.CounterpartyName, rev.CounterpartyIban, rev.CounterpartyBic)
                : null;

            RemittanceInformation? remittance = (rev.RemittanceText != null || rev.EndToEndId != null ||
                                                 rev.CustomerReference != null || rev.BankReference != null ||
                                                 rev.MandateId != null || rev.CreditorId != null)
                ? new RemittanceInformation(
                    unstructuredText: rev.RemittanceText ?? string.Empty,
                    endToEndId: rev.EndToEndId,
                    mandateId: rev.MandateId,
                    creditorId: rev.CreditorId,
                    customerReference: rev.CustomerReference,
                    bankReference: rev.BankReference,
                    purposeCode: rev.PurposeCode)
                : null;

            TransactionStatus status = Enum.TryParse<TransactionStatus>(rev.Status, out var st) ? st : TransactionStatus.Booked;
            TransactionKind kind = Enum.TryParse<TransactionKind>(rev.Kind, out var kd) ? kd : TransactionKind.Transfer;
            decimal amountVal = decimal.TryParse(rev.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amt) ? amt : 0m;
            Money money = new(amountVal, rev.Currency);

            revisions.Add(new TransactionRevision(
                id: new TransactionRevisionId(rev.Id),
                transactionId: new TransactionId(rev.TransactionId),
                observationId: new ObservationId(rev.ObservationId),
                accountId: new AccountId(rev.AccountId),
                accountIncarnationId: new AccountIncarnationId(rev.AccountIncarnationId),
                accountBindingId: new AccountBindingId(rev.AccountBindingId),
                status: status,
                kind: kind,
                amount: money,
                isCredit: rev.IsCredit,
                bookingDate: rev.BookingDate,
                valueDate: rev.ValueDate ?? rev.BookingDate,
                bankTimestamp: rev.BankTimestamp ?? rev.RetrievedAt,
                retrievedAt: rev.RetrievedAt,
                counterparty: counterparty,
                remittance: remittance,
                returnReasonCode: rev.ReturnReasonCode,
                rawBankCode: rev.RawBankCode));
        }

        return revisions;
    }

    private static List<PendingToBookedLink> ExtractLinks(StoreTransactionsDocument txDoc)
    {
        List<PendingToBookedLink> links = [];
        foreach (StorePendingToBookedLinkEntry link in txDoc.Links)
        {
            links.Add(new PendingToBookedLink(
                pendingTransactionId: new TransactionId(link.PendingTransactionId),
                bookedTransactionId: new TransactionId(link.BookedTransactionId),
                linkedAt: link.LinkedAt,
                matchCriterion: link.MatchCriterion));
        }

        return links;
    }

    private static StatementSyncCheckpoint? ExtractCheckpoint(
        StoreTransactionsDocument txDoc,
        AccountId accountId,
        AccountIncarnationId incarnationId,
        AccountBindingId bindingId)
    {
        var match = txDoc.Checkpoints.FirstOrDefault(cp =>
            cp.AccountId == accountId.Value &&
            cp.AccountIncarnationId == incarnationId.Value &&
            cp.AccountBindingId == bindingId.Value);

        if (match is null)
        {
            return null;
        }

        return new StatementSyncCheckpoint(
            accountId: accountId,
            accountIncarnationId: incarnationId,
            accountBindingId: bindingId,
            windowStart: match.WindowStart,
            windowEnd: match.WindowEnd,
            lastSyncTimestamp: match.LastSyncTimestamp,
            lastContinuationToken: match.LastContinuationToken,
            isComplete: match.IsComplete,
            observedTransactionCount: match.ObservedTransactionCount);
    }
}
