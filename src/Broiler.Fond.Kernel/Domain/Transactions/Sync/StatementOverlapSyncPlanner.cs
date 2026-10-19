using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Formats.Camt;
using Broiler.Fond.Kernel.Formats.Mt940;

namespace Broiler.Fond.Kernel.Domain.Transactions.Sync;

/// <summary>
/// Represents a proposed new transaction and its raw wire observation.
/// </summary>
public sealed class TransactionCandidate
{
    public TransactionCandidate(
        TransactionRevision revision,
        TransactionObservation observation)
    {
        Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        Observation = observation ?? throw new ArgumentNullException(nameof(observation));
    }

    public TransactionRevision Revision { get; }
    public TransactionObservation Observation { get; }
}

/// <summary>
/// Result of correlating incoming statement entries against cached transactions and checkpoints.
/// </summary>
public sealed class StatementCorrelationResult
{
    public StatementCorrelationResult(
        IReadOnlyList<TransactionCandidate> newTransactions,
        IReadOnlyList<PendingToBookedLink> newLinks,
        int existingTransactionsMatchedCount,
        StatementSyncCheckpoint updatedCheckpoint)
    {
        NewTransactions = new ReadOnlyCollection<TransactionCandidate>(newTransactions != null ? [.. newTransactions] : []);
        NewLinks = new ReadOnlyCollection<PendingToBookedLink>(newLinks != null ? [.. newLinks] : []);
        ExistingTransactionsMatchedCount = existingTransactionsMatchedCount;
        UpdatedCheckpoint = updatedCheckpoint ?? throw new ArgumentNullException(nameof(updatedCheckpoint));
    }

    public ReadOnlyCollection<TransactionCandidate> NewTransactions { get; }
    public ReadOnlyCollection<PendingToBookedLink> NewLinks { get; }
    public int ExistingTransactionsMatchedCount { get; }
    public StatementSyncCheckpoint UpdatedCheckpoint { get; }
}

/// <summary>
/// Deterministic overlap synchronization and correlation planner.
/// Preserves entry multiplicity, prevents duplicate transactions during overlapping range queries,
/// and reconciles settled bookings with pending authorizations without destructive mutations.
/// </summary>
public static class StatementOverlapSyncPlanner
{
    public const int DefaultOverlapDays = 30;
    public const int MaxPendingMatchWindowDays = 7;

    /// <summary>
    /// Computes the next query window for an account based on previous checkpoint history.
    /// </summary>
    public static (DateOnly FromDate, DateOnly ToDate) CalculateQueryWindow(
        DateOnly currentDate,
        StatementSyncCheckpoint? previousCheckpoint,
        int overlapDays = DefaultOverlapDays,
        int initialWindowDays = 90)
    {
        if (overlapDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapDays), "Overlap days cannot be negative.");
        }

        if (initialWindowDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialWindowDays), "Initial window days must be positive.");
        }

        if (previousCheckpoint == null)
        {
            // Initial sync: query initialWindowDays back up to currentDate
            DateOnly from = currentDate.AddDays(-initialWindowDays);
            return (from, currentDate);
        }

        if (!previousCheckpoint.IsComplete && previousCheckpoint.LastContinuationToken != null)
        {
            // Resuming an interrupted sync window
            return (previousCheckpoint.WindowStart, previousCheckpoint.WindowEnd);
        }

        // Incremental sync with overlap: overlap with the end of previous window
        DateOnly overlapStart = previousCheckpoint.WindowEnd.AddDays(-overlapDays);
        if (overlapStart > currentDate)
        {
            overlapStart = currentDate;
        }

        return (overlapStart, currentDate);
    }

    /// <summary>
    /// Correlates MT940 statement entries against existing account transactions with multiplicity preservation.
    /// </summary>
    public static StatementCorrelationResult CorrelateMt940(
        Mt940Statement statement,
        AccountId accountId,
        AccountIncarnationId accountIncarnationId,
        AccountBindingId accountBindingId,
        IReadOnlyList<TransactionRevision> existingRevisions,
        IReadOnlyList<PendingToBookedLink> existingLinks,
        StatementSyncCheckpoint? previousCheckpoint,
        Func<TransactionId> transactionIdFactory,
        Func<TransactionRevisionId> revisionIdFactory,
        Func<ObservationId> observationIdFactory,
        DateTimeOffset syncTimestamp,
        string? continuationToken = null,
        bool isComplete = true,
        string? statementFormat = null,
        string? sourceChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(existingRevisions);
        ArgumentNullException.ThrowIfNull(transactionIdFactory);
        ArgumentNullException.ThrowIfNull(revisionIdFactory);
        ArgumentNullException.ThrowIfNull(observationIdFactory);

        // Build canonical existing occurrences for one-to-one multiplicity matching
        var existingCounts = BuildExistingCounts(existingRevisions);
        var pendingCandidates = existingRevisions
            .Where(r => r.Status == TransactionStatus.Pending)
            .ToList();

        var linkedPendingIds = new HashSet<TransactionId>(
            (existingLinks ?? []).Select(l => l.PendingTransactionId));

        List<TransactionCandidate> newTransactions = [];
        List<PendingToBookedLink> newLinks = [];
        int matchedExistingCount = 0;

        DateOnly minDate = DateOnly.MaxValue;
        DateOnly maxDate = DateOnly.MinValue;

        foreach (var entry in statement.Entries)
        {
            if (entry.BookingDate < minDate) { minDate = entry.BookingDate; }
            if (entry.BookingDate > maxDate) { maxDate = entry.BookingDate; }

            var key = new CanonicalKey(
                entry.BookingDate,
                entry.ValueDate,
                entry.Amount.Amount,
                entry.Amount.Currency,
                entry.IsCredit,
                NormalizeString(entry.CounterpartyIban),
                NormalizeString(entry.CounterpartyName),
                NormalizeString(entry.EndToEndId),
                NormalizeString(entry.RemittanceText));

            if (existingCounts.TryGetValue(key, out int count) && count > 0)
            {
                // Multiplicity match: this occurrence was already recorded
                existingCounts[key] = count - 1;
                matchedExistingCount++;
                continue;
            }

            // New transaction entry
            var txId = transactionIdFactory();
            var revId = revisionIdFactory();
            var obsId = observationIdFactory();

            var counterparty = (entry.CounterpartyName != null || entry.CounterpartyIban != null)
                ? new Counterparty(entry.CounterpartyName, entry.CounterpartyIban, entry.CounterpartyBic)
                : null;

            var remittance = !string.IsNullOrWhiteSpace(entry.RemittanceText) || entry.EndToEndId != null || entry.CustomerReference != null
                ? new RemittanceInformation(
                    unstructuredText: entry.RemittanceText,
                    endToEndId: entry.EndToEndId,
                    mandateId: entry.MandateId,
                    creditorId: entry.CreditorId,
                    customerReference: entry.CustomerReference,
                    bankReference: entry.BankReference)
                : null;

            var revision = new TransactionRevision(
                id: revId,
                transactionId: txId,
                observationId: obsId,
                accountId: accountId,
                accountIncarnationId: accountIncarnationId,
                accountBindingId: accountBindingId,
                status: TransactionStatus.Booked,
                kind: DetermineKind(entry.TransactionCode, entry.IsReversal),
                amount: entry.Amount,
                isCredit: entry.IsCredit,
                bookingDate: entry.BookingDate,
                valueDate: entry.ValueDate,
                bankTimestamp: syncTimestamp,
                retrievedAt: syncTimestamp,
                counterparty: counterparty,
                remittance: remittance,
                rawBankCode: entry.TransactionCode);

            var observation = new TransactionObservation(
                id: obsId,
                accountBindingId: accountBindingId,
                retrievedAt: syncTimestamp,
                statementFormat: statementFormat ?? "MT940",
                statementNumber: statement.StatementNumber,
                pageToken: continuationToken,
                sourceChecksum: sourceChecksum);

            newTransactions.Add(new TransactionCandidate(revision, observation));

            // Check if this booked transaction reconciles an existing pending authorization
            var matchedPending = FindMatchingPending(revision, pendingCandidates, linkedPendingIds);
            if (matchedPending != null)
            {
                linkedPendingIds.Add(matchedPending.TransactionId);
                newLinks.Add(new PendingToBookedLink(
                    matchedPending.TransactionId,
                    revision.TransactionId,
                    syncTimestamp,
                    matchedPending.Remittance?.EndToEndId != null && matchedPending.Remittance.EndToEndId == revision.Remittance?.EndToEndId
                        ? "EndToEndId"
                        : "ExactAmountAndCounterparty"));
            }
        }

        DateOnly windowStart = minDate != DateOnly.MaxValue ? minDate : (previousCheckpoint?.WindowStart ?? DateOnly.FromDateTime(syncTimestamp.Date));
        DateOnly windowEnd = maxDate != DateOnly.MinValue ? maxDate : (previousCheckpoint?.WindowEnd ?? DateOnly.FromDateTime(syncTimestamp.Date));

        var updatedCheckpoint = new StatementSyncCheckpoint(
            accountId,
            accountIncarnationId,
            accountBindingId,
            windowStart,
            windowEnd,
            syncTimestamp,
            lastContinuationToken: continuationToken,
            isComplete: isComplete,
            observedTransactionCount: (previousCheckpoint?.ObservedTransactionCount ?? 0) + newTransactions.Count);

        return new StatementCorrelationResult(
            newTransactions,
            newLinks,
            matchedExistingCount,
            updatedCheckpoint);
    }

    /// <summary>
    /// Correlates ISO 20022 CAMT statement entries against existing account transactions with multiplicity preservation.
    /// </summary>
    public static StatementCorrelationResult CorrelateCamt(
        CamtStatement statement,
        AccountId accountId,
        AccountIncarnationId accountIncarnationId,
        AccountBindingId accountBindingId,
        IReadOnlyList<TransactionRevision> existingRevisions,
        IReadOnlyList<PendingToBookedLink> existingLinks,
        StatementSyncCheckpoint? previousCheckpoint,
        Func<TransactionId> transactionIdFactory,
        Func<TransactionRevisionId> revisionIdFactory,
        Func<ObservationId> observationIdFactory,
        DateTimeOffset syncTimestamp,
        string? continuationToken = null,
        bool isComplete = true,
        string? statementFormat = null,
        string? sourceChecksum = null)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(existingRevisions);
        ArgumentNullException.ThrowIfNull(transactionIdFactory);
        ArgumentNullException.ThrowIfNull(revisionIdFactory);
        ArgumentNullException.ThrowIfNull(observationIdFactory);

        var existingCounts = BuildExistingCounts(existingRevisions);
        var pendingCandidates = existingRevisions
            .Where(r => r.Status == TransactionStatus.Pending)
            .ToList();

        var linkedPendingIds = new HashSet<TransactionId>(
            (existingLinks ?? []).Select(l => l.PendingTransactionId));

        List<TransactionCandidate> newTransactions = [];
        List<PendingToBookedLink> newLinks = [];
        int matchedExistingCount = 0;

        DateOnly minDate = DateOnly.MaxValue;
        DateOnly maxDate = DateOnly.MinValue;

        foreach (var entry in statement.Entries)
        {
            if (entry.BookingDate < minDate) { minDate = entry.BookingDate; }
            if (entry.BookingDate > maxDate) { maxDate = entry.BookingDate; }

            var key = new CanonicalKey(
                entry.BookingDate,
                entry.ValueDate,
                entry.Amount.Amount,
                entry.Amount.Currency,
                entry.IsCredit,
                NormalizeString(entry.CounterpartyIban),
                NormalizeString(entry.CounterpartyName),
                NormalizeString(entry.EndToEndId),
                NormalizeString(entry.RemittanceText));

            if (existingCounts.TryGetValue(key, out int count) && count > 0)
            {
                existingCounts[key] = count - 1;
                matchedExistingCount++;
                continue;
            }

            var txId = transactionIdFactory();
            var revId = revisionIdFactory();
            var obsId = observationIdFactory();

            var counterparty = (entry.CounterpartyName != null || entry.CounterpartyIban != null)
                ? new Counterparty(entry.CounterpartyName, entry.CounterpartyIban, entry.CounterpartyBic)
                : null;

            var remittance = !string.IsNullOrWhiteSpace(entry.RemittanceText) || entry.EndToEndId != null || entry.MandateId != null
                ? new RemittanceInformation(
                    unstructuredText: entry.RemittanceText,
                    endToEndId: entry.EndToEndId,
                    mandateId: entry.MandateId,
                    creditorId: entry.CreditorId,
                    bankReference: entry.EntryReference)
                : null;

            var revision = new TransactionRevision(
                id: revId,
                transactionId: txId,
                observationId: obsId,
                accountId: accountId,
                accountIncarnationId: accountIncarnationId,
                accountBindingId: accountBindingId,
                status: entry.Status,
                kind: DetermineKind(entry.BankTransactionCode, isReversal: entry.ReturnReasonCode != null),
                amount: entry.Amount,
                isCredit: entry.IsCredit,
                bookingDate: entry.BookingDate,
                valueDate: entry.ValueDate,
                bankTimestamp: syncTimestamp,
                retrievedAt: syncTimestamp,
                counterparty: counterparty,
                remittance: remittance,
                returnReasonCode: entry.ReturnReasonCode,
                rawBankCode: entry.BankTransactionCode);

            var observation = new TransactionObservation(
                id: obsId,
                accountBindingId: accountBindingId,
                retrievedAt: syncTimestamp,
                statementFormat: statementFormat ?? "CAMT",
                statementNumber: statement.StatementId,
                pageToken: continuationToken,
                sourceChecksum: sourceChecksum);

            newTransactions.Add(new TransactionCandidate(revision, observation));

            if (revision.Status == TransactionStatus.Booked)
            {
                var matchedPending = FindMatchingPending(revision, pendingCandidates, linkedPendingIds);
                if (matchedPending != null)
                {
                    linkedPendingIds.Add(matchedPending.TransactionId);
                    newLinks.Add(new PendingToBookedLink(
                        matchedPending.TransactionId,
                        revision.TransactionId,
                        syncTimestamp,
                        matchedPending.Remittance?.EndToEndId != null && matchedPending.Remittance.EndToEndId == revision.Remittance?.EndToEndId
                            ? "EndToEndId"
                            : "ExactAmountAndCounterparty"));
                }
            }
        }

        DateOnly windowStart = minDate != DateOnly.MaxValue ? minDate : (previousCheckpoint?.WindowStart ?? DateOnly.FromDateTime(syncTimestamp.Date));
        DateOnly windowEnd = maxDate != DateOnly.MinValue ? maxDate : (previousCheckpoint?.WindowEnd ?? DateOnly.FromDateTime(syncTimestamp.Date));

        var updatedCheckpoint = new StatementSyncCheckpoint(
            accountId,
            accountIncarnationId,
            accountBindingId,
            windowStart,
            windowEnd,
            syncTimestamp,
            lastContinuationToken: continuationToken,
            isComplete: isComplete,
            observedTransactionCount: (previousCheckpoint?.ObservedTransactionCount ?? 0) + newTransactions.Count);

        return new StatementCorrelationResult(
            newTransactions,
            newLinks,
            matchedExistingCount,
            updatedCheckpoint);
    }

    private static TransactionRevision? FindMatchingPending(
        TransactionRevision booked,
        List<TransactionRevision> pendingCandidates,
        HashSet<TransactionId> linkedPendingIds)
    {
        foreach (var pending in pendingCandidates)
        {
            if (linkedPendingIds.Contains(pending.TransactionId))
            {
                continue;
            }

            if (pending.Amount != booked.Amount || pending.IsCredit != booked.IsCredit)
            {
                continue;
            }

            // Direct EndToEndId match
            if (!string.IsNullOrEmpty(booked.Remittance?.EndToEndId) &&
                string.Equals(booked.Remittance.EndToEndId, pending.Remittance?.EndToEndId, StringComparison.OrdinalIgnoreCase))
            {
                return pending;
            }

            // Heuristic match within ±7 days and matching counterparty
            int daysDiff = Math.Abs(booked.BookingDate.DayNumber - pending.BookingDate.DayNumber);
            if (daysDiff <= MaxPendingMatchWindowDays)
            {
                bool sameIban = !string.IsNullOrEmpty(booked.Counterparty?.Iban) &&
                                string.Equals(booked.Counterparty.Iban, pending.Counterparty?.Iban, StringComparison.OrdinalIgnoreCase);

                bool sameName = !string.IsNullOrEmpty(booked.Counterparty?.Name) &&
                                string.Equals(booked.Counterparty.Name, pending.Counterparty?.Name, StringComparison.OrdinalIgnoreCase);

                if (sameIban || sameName)
                {
                    return pending;
                }
            }
        }

        return null;
    }

    private static Dictionary<CanonicalKey, int> BuildExistingCounts(IReadOnlyList<TransactionRevision> existing)
    {
        Dictionary<CanonicalKey, int> counts = [];
        foreach (var rev in existing)
        {
            var key = new CanonicalKey(
                rev.BookingDate,
                rev.ValueDate,
                rev.Amount.Amount,
                rev.Amount.Currency,
                rev.IsCredit,
                NormalizeString(rev.Counterparty?.Iban),
                NormalizeString(rev.Counterparty?.Name),
                NormalizeString(rev.Remittance?.EndToEndId),
                NormalizeString(rev.Remittance?.UnstructuredText));

            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        return counts;
    }

    private static TransactionKind DetermineKind(string? code, bool isReversal)
    {
        if (isReversal)
        {
            return TransactionKind.ReturnDebit;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return TransactionKind.Other;
        }

        string upper = code.ToUpperInvariant();
        if (upper.Contains("TRF") || upper.Contains("UEBERWEISUNG"))
        {
            return TransactionKind.Transfer;
        }
        if (upper.Contains("LASTSCHRIFT") || upper.Contains("DEBIT") || upper.Contains("DD"))
        {
            return TransactionKind.Debit;
        }
        if (upper.Contains("GUTSCHRIFT") || upper.Contains("CREDIT"))
        {
            return TransactionKind.Credit;
        }
        if (upper.Contains("CHG") || upper.Contains("FEE") || upper.Contains("ENTGELT"))
        {
            return TransactionKind.Fee;
        }
        if (upper.Contains("INT") || upper.Contains("ZINS"))
        {
            return TransactionKind.Interest;
        }
        if (upper.Contains("CARD") || upper.Contains("KARTE") || upper.Contains("POS"))
        {
            return TransactionKind.CardPayment;
        }

        return TransactionKind.Other;
    }

    private static string? NormalizeString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private readonly record struct CanonicalKey(
        DateOnly BookingDate,
        DateOnly? ValueDate,
        decimal Amount,
        string Currency,
        bool IsCredit,
        string? CounterpartyIban,
        string? CounterpartyName,
        string? EndToEndId,
        string? RemittanceText);
}
