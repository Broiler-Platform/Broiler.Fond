using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Ingests only a verified existing binding. Never discovers, relinks, or allocates account identities.</summary>
public static class FinTsTargetBalanceIngestion
{
    public static bool MatchesParameter(BankConnection connection, KnownAccountBinding target, FinTsAccountParameters parameter, string bic)
    {
        var source = FinTsAccountSourceLocatorFactory.CreateLocator(connection, parameter);
        var enriched = new AccountSourceLocator(source.InstitutionId, source.ConnectionId, source.Connector, source.ConnectorVersion,
            connection.InstitutionConfiguration, source.Iban, source.DomesticAccountNumber, source.DomesticBankCode, source.Subaccount, source.Currency,
            source.BankIdentifiers.Concat(new[] { new BankAccountIdentifier("fints", "account", "bic", bic) }));
        return target.State == AccountBindingState.Active && target.Locator.Equals(enriched);
    }

    public static FinTsReadIngestionResult Apply(BankConnection connection, KnownAccountBinding target,
        FinTsPinTanReadSessionResult session, IReadOnlyList<KnownAccountBinding> knownBindings, LocalIdentityLedger ledger,
        DateTimeOffset retrievedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(knownBindings); ArgumentNullException.ThrowIfNull(ledger);
        cancellationToken.ThrowIfCancellationRequested();
        FinTsReadIngestionResult Reject(string check) => FinTsReadIngestionResult.Rejected(FinTsReadIngestionStatus.RejectedMissingEvidence,
            $"The balance exchange does not qualify the verified target binding ({check}).", connection);
        if (!connection.IsSynchronized || connection.State != BankConnectionState.Connected || target.State != AccountBindingState.Active ||
            target.Locator.Connector != "fints" || target.Locator.ConnectionId != connection.Id || target.Locator.InstitutionId != connection.InstitutionId ||
            !knownBindings.Any(b => b.BindingId == target.BindingId && b.AccountId == target.AccountId && b.IncarnationId == target.IncarnationId &&
                b.State == AccountBindingState.Active && b.Locator.Equals(target.Locator)) ||
            session.Stage != FinTsPinTanReadSessionStage.Terminal || session.Transition != FinTsPinTanReadSessionTransition.ExecutionReported ||
            session.RequirementsEvidence?.HasMatchingEvidence != true || session.BalanceFirstPageEvidence?.HasMatchingEvidence != true ||
            session.ClosingEvidence?.HasMatchingEvidence != true || !session.ClosingEvidence.CloseReported || session.ClosingEvidence.AbortReported ||
            session.AggregatedBalances.Count == 0)
            return Reject("session");
        var first = session.BalanceFirstPageEvidence.Request.Context;
        if (!ReferenceEquals(first.Signature.Initialization, session.RequirementsEvidence) ||
            !ReferenceEquals(session.ClosingEvidence.Request.Context.Initialization, session.InitializationEvidence) ||
            !ReferenceEquals(session.RequirementsEvidence.Procedures.Initialization, session.InitializationEvidence)) return Reject("provenance");
        var reports = session.BalanceFirstPageEvidence.ReadData.Balances.ToList();
        var continuation = session.BalanceContinuationEvidence;
        var pages = new List<FinTsPinTanReadContinuationEvidence>();
        while (continuation is not null)
        {
            if (pages.Count >= 15 || !continuation.HasMatchingEvidence) return Reject("continuation-budget");
            pages.Add(continuation);
            var context = continuation.Request.Context.Continuation;
            if (!ReferenceEquals(context.Origin, first)) return Reject("continuation-origin");
            if (context.Previous is not null && !ReferenceEquals(context.Previous, session.BalanceFirstPageEvidence)) return Reject("continuation-chain");
            continuation = context.PreviousContinuation;
        }
        foreach (var page in pages.AsEnumerable().Reverse()) reports.AddRange(page.ReadData.Balances);
        var terminalOutcome = session.BalanceContinuationEvidence?.Outcome ?? session.BalanceFirstPageEvidence.Outcome;
        int finalClient = session.BalanceContinuationEvidence?.Request.MessageNumber ?? session.BalanceFirstPageEvidence.Request.MessageNumber;
        int finalBank = session.BalanceContinuationEvidence?.Request.ExpectedBankMessageNumber ?? session.BalanceFirstPageEvidence.Request.ExpectedBankMessageNumber;
        if (session.ClosingEvidence.Request.MessageNumber != finalClient + 1 || session.ClosingEvidence.Request.ExpectedBankMessageNumber != finalBank + 1 ||
            session.ClosingEvidence.Request.DialogueId != session.BalanceFirstPageEvidence.Request.DialogueId) return Reject("closing-correlation");
        if (terminalOutcome != FinTsReadOutcomeObservation.ExecutionReported || reports.Count != session.AggregatedBalances.Count ||
            !reports.Zip(session.AggregatedBalances).All(pair => ReferenceEquals(pair.First, pair.Second))) return Reject("reports");
        var requested = first.Signature.Request.Request.Accounts.Single();
        if (!MatchesParameter(connection, target, first.Account, requested.Bic) || requested.Iban != target.Locator.Iban ||
            string.IsNullOrEmpty(target.Locator.Currency) || !CurrencyCatalog.IsRecognized(target.Locator.Currency)) return Reject("target");
        var report = session.AggregatedBalances[0];
        foreach (var page in session.AggregatedBalances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (page.Account.Iban != requested.Iban || page.Account.Bic != requested.Bic || page.AccountCurrency != target.Locator.Currency ||
                page.HasCurrencyConflict || page.DueDate is not null ||
                page.Account.Number != requested.Number || page.Account.Subaccount != requested.Subaccount ||
                page.Account.Country != requested.Country || page.Account.Institution != requested.Institution ||
                page.Booked.SignedValue != report.Booked.SignedValue || !SameTimestamp(page.Booked.Timestamp, report.Booked.Timestamp) ||
                !SameAmount(page.Available, report.Available) || !SameAmount(page.CreditLine, report.CreditLine)) return Reject("values");
        }
        // Identical repeated reports across pages describe one observation, not multiple conflicting current balances.
        using var batch = ledger.BeginBatch();
        var snapshots = new List<BalanceSnapshot>();
        void Add(BalanceKind kind, Money? money, DateTimeOffset? bankTimestamp)
        {
            var observation = batch.Allocate(LocalEntityKind.Observation, [new(new(target.BindingId.Value), LocalEntityKind.AccountBinding)]);
            var id = batch.Allocate(LocalEntityKind.BalanceSnapshot, [new(observation, LocalEntityKind.Observation)]);
            snapshots.Add(new(new(id.Value), new(observation.Value), target.AccountId, target.IncarnationId, target.BindingId,
                kind, money, bankTimestamp, retrievedAt));
        }
        Add(BalanceKind.Booked, new Money(report.Booked.SignedValue, report.AccountCurrency), Timestamp(report.Booked.Timestamp));
        // Explicit unavailable observations prevent an older available/credit value from appearing current.
        Add(BalanceKind.Available, report.Available is null ? null : new Money(report.Available.Value, report.Available.Currency), null);
        Add(BalanceKind.CreditLine, report.CreditLine is null ? null : new Money(report.CreditLine.Value, report.CreditLine.Currency), null);
        var committed = batch.Commit(cancellationToken);
        return FinTsReadIngestionResult.Success(new[] { target }, snapshots, Array.Empty<AccountRediscoveryDecision>(),
            connection.WithSuccessfulContact(retrievedAt), committed);
    }

    private static bool SameAmount(FinTsReadAmount? a, FinTsReadAmount? b) => a is null ? b is null : b is not null && a.Value == b.Value && a.Currency == b.Currency;
    private static bool SameTimestamp(FinTsReadTimestamp? a, FinTsReadTimestamp? b) => a is null ? b is null : b is not null && a.Date == b.Date && a.Time == b.Time;
    // The wire supplies calendar fields, not an offset. UTC is the existing storage convention, not a bank timezone assertion.
    private static DateTimeOffset? Timestamp(FinTsReadTimestamp? value) => value is null ? null :
        new DateTimeOffset(value.Date.ToDateTime(value.Time ?? TimeOnly.MinValue), TimeSpan.Zero);
}
