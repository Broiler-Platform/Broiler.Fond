using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Status of a live FinTS read data ingestion and store binding application.</summary>
public enum FinTsReadIngestionStatus
{
    /// <summary>Ingestion applied successfully; new accounts bound, balances ingested, and ledger committed.</summary>
    Applied = 1,

    /// <summary>The bank connection is disabled; ingestion rejected.</summary>
    RejectedDisabledConnection = 2,

    /// <summary>The bank connection is not synchronized or connected.</summary>
    RejectedUnsynchronizedConnection = 3,

    /// <summary>Institution identity in session evidence does not match connection configuration.</summary>
    RejectedInstitutionMismatch = 4,

    /// <summary>No qualifying account discovery, balance report, or parameter evidence was found in the session result.</summary>
    RejectedMissingEvidence = 5,

    /// <summary>Session terminated with an abort or unresolvable review state preventing ingestion.</summary>
    RejectedSessionOutcome = 6,

    /// <summary>Ledger batch conflict or stale writer rejected the atomic identity commit.</summary>
    RejectedLedgerConflict = 7,

    /// <summary>Identity snapshot validation or entity limit failed during allocation or commit.</summary>
    RejectedIdentityValidation = 8,
}

/// <summary>Result of applying FinTS read data ingestion, account rediscovery, and atomic store binding.</summary>
public sealed class FinTsReadIngestionResult
{
    public FinTsReadIngestionStatus Status { get; }
    public ReadOnlyCollection<KnownAccountBinding> BoundAccounts { get; }
    public ReadOnlyCollection<BalanceSnapshot> IngestedSnapshots { get; }
    public ReadOnlyCollection<AccountRediscoveryDecision> RediscoveryDecisions { get; }
    public BankConnection UpdatedConnection { get; }
    public IdentitySnapshot? CommittedSnapshot { get; }
    public ReadOnlyCollection<string> Issues { get; }
    public bool IsSuccess => Status == FinTsReadIngestionStatus.Applied;

    internal FinTsReadIngestionResult(
        FinTsReadIngestionStatus status,
        IList<KnownAccountBinding> boundAccounts,
        IList<BalanceSnapshot> ingestedSnapshots,
        IList<AccountRediscoveryDecision> rediscoveryDecisions,
        BankConnection updatedConnection,
        IdentitySnapshot? committedSnapshot,
        IList<string> issues)
    {
        Status = status;
        BoundAccounts = new ReadOnlyCollection<KnownAccountBinding>(boundAccounts);
        IngestedSnapshots = new ReadOnlyCollection<BalanceSnapshot>(ingestedSnapshots);
        RediscoveryDecisions = new ReadOnlyCollection<AccountRediscoveryDecision>(rediscoveryDecisions);
        UpdatedConnection = updatedConnection;
        CommittedSnapshot = committedSnapshot;
        Issues = new ReadOnlyCollection<string>(issues);
    }

    public static FinTsReadIngestionResult Success(
        IList<KnownAccountBinding> boundAccounts,
        IList<BalanceSnapshot> ingestedSnapshots,
        IList<AccountRediscoveryDecision> rediscoveryDecisions,
        BankConnection updatedConnection,
        IdentitySnapshot committedSnapshot,
        IList<string>? issues = null) =>
        new(FinTsReadIngestionStatus.Applied, boundAccounts, ingestedSnapshots, rediscoveryDecisions, updatedConnection, committedSnapshot, issues ?? Array.Empty<string>());

    public static FinTsReadIngestionResult Rejected(
        FinTsReadIngestionStatus status,
        string issue,
        BankConnection connection,
        IList<string>? additionalIssues = null)
    {
        List<string> issues = [issue];
        if (additionalIssues is not null)
        {
            issues.AddRange(additionalIssues);
        }

        return new(
            status,
            Array.Empty<KnownAccountBinding>(),
            Array.Empty<BalanceSnapshot>(),
            Array.Empty<AccountRediscoveryDecision>(),
            connection,
            committedSnapshot: null,
            issues);
    }
}

/// <summary>
/// Coordinates pure, atomic ingestion of FinTS 3.0 read session results into the local store,
/// executing exact account rediscovery, entity allocation, balance snapshot creation, and ledger commit.
/// </summary>
public static class FinTsReadIngestionApplication
{
    public static FinTsReadIngestionResult Apply(
        BankConnection connection,
        FinTsPinTanReadSessionResult sessionResult,
        IReadOnlyList<KnownAccountBinding> knownBindings,
        LocalIdentityLedger identityLedger,
        DateTimeOffset retrievedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(knownBindings);
        ArgumentNullException.ThrowIfNull(identityLedger);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Operational connection state validation
        if (connection.State == BankConnectionState.Disabled)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedDisabledConnection,
                "Bank connection is disabled; read ingestion rejected.",
                connection.WithAttemptedContact(retrievedAt));
        }

        if (!connection.IsSynchronized && connection.State != BankConnectionState.Connected)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedUnsynchronizedConnection,
                "Bank connection must be synchronized or connected before ingesting read data.",
                connection.WithAttemptedContact(retrievedAt));
        }

        // 2. Terminal session outcome validation
        if (sessionResult.Transition == FinTsPinTanReadSessionTransition.AbortReported)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedSessionOutcome,
                "Session reported a terminal bank abort; read ingestion rejected.",
                connection.WithAttemptedContact(retrievedAt));
        }

        if (sessionResult.Stage == FinTsPinTanReadSessionStage.Terminal &&
            sessionResult.Transition is FinTsPinTanReadSessionTransition.RejectedForReview
                or FinTsPinTanReadSessionTransition.ContextMismatch
                or FinTsPinTanReadSessionTransition.ReplayRejected)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedSessionOutcome,
                $"Session terminated with rejection {sessionResult.Transition}; read ingestion rejected.",
                connection.WithAttemptedContact(retrievedAt));
        }

        // 3. Evidence presence validation
        bool hasBalances = sessionResult.AggregatedBalances.Count > 0;
        bool hasDiscovery = sessionResult.AllAccountEvidence is not null &&
                            sessionResult.AllAccountEvidence.ReadData.Discovery.Count > 0;
        bool hasParameters = sessionResult.InitializationEvidence?.Parameters is not null;

        if (!hasBalances && !hasDiscovery && !hasParameters && !sessionResult.HasMatchingEvidence)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedMissingEvidence,
                "Read session result contains no account, balance, or parameter evidence.",
                connection.WithAttemptedContact(retrievedAt));
        }

        // 4. Institution bank code comparison
        if (!string.IsNullOrWhiteSpace(connection.InstitutionConfiguration.BankCode) &&
            sessionResult.InitializationEvidence?.Parameters.Bank is { } bank)
        {
            var bankFields = bank.Source.Fields;
            if (bankFields.Count > 1 && bankFields[1].Elements.Count > 0)
            {
                var elements = bankFields[1].Elements;
                string reportedBankCode = elements.Count > 1
                    ? elements[1].HeaderText().Trim()
                    : elements[0].HeaderText().Trim();

                if (!string.Equals(reportedBankCode, connection.InstitutionConfiguration.BankCode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return FinTsReadIngestionResult.Rejected(
                        FinTsReadIngestionStatus.RejectedInstitutionMismatch,
                        $"Reported bank code '{reportedBankCode}' does not match connection bank code '{connection.InstitutionConfiguration.BankCode}'.",
                        connection.WithAttemptedContact(retrievedAt));
                }
            }
        }

        List<string> issues = [];

        // Map UPD currency by account identifier to enrich discovery records where possible
        Dictionary<string, string> updCurrencies = new(StringComparer.Ordinal);
        if (sessionResult.InitializationEvidence?.Parameters.Accounts is { } updAccounts)
        {
            foreach (var upd in updAccounts)
            {
                if (upd.Currency is not null && !upd.Currency.IsEmpty)
                {
                    string curr = upd.Currency.HeaderText();
                    if (!upd.Iban.IsEmpty)
                    {
                        updCurrencies[upd.Iban.HeaderText()] = curr;
                    }
                    if (upd.HasAccountConnection && upd.AccountConnection.Elements.Count > 0 && !upd.AccountConnection.Elements[0].IsEmpty)
                    {
                        updCurrencies[upd.AccountConnection.Elements[0].HeaderText()] = curr;
                    }
                }
            }
        }

        // 5. Discover accounts across reports and parameters
        Dictionary<AccountSourceLocator, FinTsReadAccount> locatorToReadAccount = [];
        HashSet<AccountSourceLocator> discoveredLocators = [];

        // 5a. From all-account discovery evidence (HISPA)
        if (sessionResult.AllAccountEvidence is not null)
        {
            foreach (var report in sessionResult.AllAccountEvidence.ReadData.Discovery)
            {
                foreach (var account in report.Accounts)
                {
                    string? currency = null;
                    if (!string.IsNullOrEmpty(account.Iban) && updCurrencies.TryGetValue(account.Iban, out string? c1))
                    {
                        currency = c1;
                    }
                    else if (!string.IsNullOrEmpty(account.Number) && updCurrencies.TryGetValue(account.Number, out string? c2))
                    {
                        currency = c2;
                    }

                    var locator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, account, currency);
                    discoveredLocators.Add(locator);
                    locatorToReadAccount.TryAdd(locator, account);
                }
            }
        }

        // 5b. From aggregated balance reports (HISAL)
        foreach (var report in sessionResult.AggregatedBalances)
        {
            var locator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, report.Account, report.AccountCurrency);
            discoveredLocators.Add(locator);
            locatorToReadAccount.TryAdd(locator, report.Account);
        }

        // 5c. From UPD accounts (HIUPD)
        if (sessionResult.InitializationEvidence?.Parameters.Accounts is { } parameterAccounts)
        {
            foreach (var accountParam in parameterAccounts)
            {
                if (accountParam.HasAccountConnection || !accountParam.Iban.IsEmpty)
                {
                    var locator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, accountParam);
                    discoveredLocators.Add(locator);
                }
            }
        }

        // 6. Begin atomic identity allocation batch
        using IdentityAllocationBatch batch = identityLedger.BeginBatch();

        // 7. Account rediscovery planning
        List<AccountDiscoveryOccurrence> occurrences = [];
        foreach (var locator in discoveredLocators)
        {
            ObservationId obsId = new(batch.Allocate(LocalEntityKind.Observation, [new(new(connection.Id.Value), LocalEntityKind.Connection)]).Value);
            occurrences.Add(new AccountDiscoveryOccurrence(obsId, locator, AccountLifetimeEvidence.Unspecified));
        }

        var decisions = AccountRediscoveryPlanner.Plan(knownBindings, occurrences, cancellationToken);

        List<KnownAccountBinding> activeBindings = new(knownBindings);
        Dictionary<AccountSourceLocator, KnownAccountBinding> locatorToBinding = [];

        foreach (var known in knownBindings)
        {
            locatorToBinding[known.Locator] = known;
        }

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (decision.Action == AccountRediscoveryAction.ReuseBinding)
            {
                var binding = decision.ReusedBinding!;
                locatorToBinding[decision.Occurrence.Locator] = binding;
            }
            else if (decision.Action == AccountRediscoveryAction.NewAccountCandidate)
            {
                AccountId accountId = new(batch.Allocate(LocalEntityKind.Account).Value);
                AccountIncarnationId incarnationId = new(batch.Allocate(
                    LocalEntityKind.AccountIncarnation,
                    [new(new(accountId.Value), LocalEntityKind.Account)]).Value);
                AccountBindingId bindingId = new(batch.Allocate(
                    LocalEntityKind.AccountBinding,
                    [
                        new(new(incarnationId.Value), LocalEntityKind.AccountIncarnation),
                        new(new(connection.Id.Value), LocalEntityKind.Connection),
                    ]).Value);

                var newBinding = new KnownAccountBinding(
                    accountId,
                    incarnationId,
                    bindingId,
                    decision.Occurrence.Locator,
                    AccountBindingState.Active);

                activeBindings.Add(newBinding);
                locatorToBinding[decision.Occurrence.Locator] = newBinding;
            }
            else if (decision.Action == AccountRediscoveryAction.QuarantinedCandidate)
            {
                AccountId accountId = new(batch.Allocate(LocalEntityKind.Account).Value);
                AccountIncarnationId incarnationId = new(batch.Allocate(
                    LocalEntityKind.AccountIncarnation,
                    [new(new(accountId.Value), LocalEntityKind.Account)]).Value);
                AccountBindingId bindingId = new(batch.Allocate(
                    LocalEntityKind.AccountBinding,
                    [
                        new(new(incarnationId.Value), LocalEntityKind.AccountIncarnation),
                        new(new(connection.Id.Value), LocalEntityKind.Connection),
                    ]).Value);

                var quarantinedBinding = new KnownAccountBinding(
                    accountId,
                    incarnationId,
                    bindingId,
                    decision.Occurrence.Locator,
                    AccountBindingState.Quarantined);

                activeBindings.Add(quarantinedBinding);
                locatorToBinding[decision.Occurrence.Locator] = quarantinedBinding;
                issues.Add($"Account with locator {decision.Occurrence.Locator} quarantined: {decision.Reason}.");
            }
        }

        // 8. Ingest balance snapshots for active bindings
        List<BalanceSnapshot> ingestedSnapshots = [];
        foreach (var report in sessionResult.AggregatedBalances)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var reportLocator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, report.Account, report.AccountCurrency);
            if (!locatorToBinding.TryGetValue(reportLocator, out KnownAccountBinding? binding) ||
                binding.State != AccountBindingState.Active)
            {
                issues.Add($"Balance report skipped for inactive or unmapped locator {reportLocator}.");
                continue;
            }

            DateTimeOffset? bankTimestamp = null;
            if (report.Booked.Timestamp is { } ts)
            {
                bankTimestamp = new DateTimeOffset(
                    ts.Date.Year, ts.Date.Month, ts.Date.Day,
                    ts.Time?.Hour ?? 0, ts.Time?.Minute ?? 0, ts.Time?.Second ?? 0,
                    TimeSpan.Zero);
            }

            // Booked Balance Snapshot
            ObservationId bookedObsId = new(batch.Allocate(
                LocalEntityKind.Observation,
                [new(new(binding.BindingId.Value), LocalEntityKind.AccountBinding)]).Value);
            BalanceSnapshotId bookedSnapshotId = new(batch.Allocate(
                LocalEntityKind.BalanceSnapshot,
                [new(new(bookedObsId.Value), LocalEntityKind.Observation)]).Value);

            Money? bookedMoney = null;
            if (CurrencyCatalog.IsRecognized(report.Booked.Amount.Currency))
            {
                bookedMoney = new Money(report.Booked.SignedValue, report.Booked.Amount.Currency);
            }
            else
            {
                issues.Add($"Reported booked currency '{report.Booked.Amount.Currency}' is not recognized.");
            }

            var bookedSnapshot = new BalanceSnapshot(
                bookedSnapshotId,
                bookedObsId,
                binding.AccountId,
                binding.IncarnationId,
                binding.BindingId,
                BalanceKind.Booked,
                bookedMoney,
                bankTimestamp,
                retrievedAt);

            ingestedSnapshots.Add(bookedSnapshot);

            // Available Balance Snapshot
            if (report.Available is not null)
            {
                ObservationId availObsId = new(batch.Allocate(
                    LocalEntityKind.Observation,
                    [new(new(binding.BindingId.Value), LocalEntityKind.AccountBinding)]).Value);
                BalanceSnapshotId availSnapshotId = new(batch.Allocate(
                    LocalEntityKind.BalanceSnapshot,
                    [new(new(availObsId.Value), LocalEntityKind.Observation)]).Value);

                Money? availMoney = null;
                if (CurrencyCatalog.IsRecognized(report.Available.Currency))
                {
                    availMoney = new Money(report.Available.Value, report.Available.Currency);
                }
                else
                {
                    issues.Add($"Reported available currency '{report.Available.Currency}' is not recognized.");
                }

                var availSnapshot = new BalanceSnapshot(
                    availSnapshotId,
                    availObsId,
                    binding.AccountId,
                    binding.IncarnationId,
                    binding.BindingId,
                    BalanceKind.Available,
                    availMoney,
                    bankTimestamp,
                    retrievedAt);

                ingestedSnapshots.Add(availSnapshot);
            }

            // CreditLine Balance Snapshot
            if (report.CreditLine is not null)
            {
                ObservationId creditObsId = new(batch.Allocate(
                    LocalEntityKind.Observation,
                    [new(new(binding.BindingId.Value), LocalEntityKind.AccountBinding)]).Value);
                BalanceSnapshotId creditSnapshotId = new(batch.Allocate(
                    LocalEntityKind.BalanceSnapshot,
                    [new(new(creditObsId.Value), LocalEntityKind.Observation)]).Value);

                Money? creditMoney = null;
                if (CurrencyCatalog.IsRecognized(report.CreditLine.Currency))
                {
                    creditMoney = new Money(report.CreditLine.Value, report.CreditLine.Currency);
                }
                else
                {
                    issues.Add($"Reported credit line currency '{report.CreditLine.Currency}' is not recognized.");
                }

                var creditSnapshot = new BalanceSnapshot(
                    creditSnapshotId,
                    creditObsId,
                    binding.AccountId,
                    binding.IncarnationId,
                    binding.BindingId,
                    BalanceKind.CreditLine,
                    creditMoney,
                    bankTimestamp,
                    retrievedAt);

                ingestedSnapshots.Add(creditSnapshot);
            }

            if (report.HasCurrencyConflict)
            {
                issues.Add($"Balance report for account '{report.Account.Iban}' has internal currency conflict.");
            }
        }

        // 9. Atomic commit of identity ledger batch
        IdentitySnapshot committed;
        try
        {
            committed = batch.Commit(cancellationToken);
        }
        catch (StaleIdentityBatchException)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedLedgerConflict,
                "Identity ledger snapshot changed concurrently; commit rejected.",
                connection.WithAttemptedContact(retrievedAt),
                issues);
        }
        catch (InvalidIdentitySnapshotException ex)
        {
            return FinTsReadIngestionResult.Rejected(
                FinTsReadIngestionStatus.RejectedIdentityValidation,
                $"Identity validation failed: {ex.Validation.Issues.FirstOrDefault()?.Code}",
                connection.WithAttemptedContact(retrievedAt),
                issues);
        }

        // 10. Update connection contact and return success
        var updatedConnection = connection.WithSuccessfulContact(retrievedAt);

        return FinTsReadIngestionResult.Success(
            activeBindings,
            ingestedSnapshots,
            decisions,
            updatedConnection,
            committed,
            issues);
    }
}
