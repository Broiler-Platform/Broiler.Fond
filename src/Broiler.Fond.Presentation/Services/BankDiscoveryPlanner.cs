using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

internal static class BankDiscoveryPlanner
{
    internal static AccountSourceLocator WithDiscoveryBic(BankConnection connection, AccountSourceLocator source, FinTsReadAccount report) =>
        new(source.InstitutionId, source.ConnectionId, source.Connector, source.ConnectorVersion, connection.InstitutionConfiguration,
            source.Iban, source.DomesticAccountNumber, source.DomesticBankCode, source.Subaccount, source.Currency,
            source.BankIdentifiers.Concat(string.IsNullOrEmpty(report.Bic) ? [] : new[] { new BankAccountIdentifier("fints", "account", "bic", report.Bic) }));

    internal static LocalIdentityLedger DetachedLedger(StoreSnapshot snapshot)
    {
        var validation = LocalIdentityLedger.TryOpen(StoreArchiveCodec.ToIdentitySnapshot(snapshot), out var ledger);
        if (!validation.IsValid) throw new InvalidOperationException("The profile identity ledger requires review.");
        return ledger!;
    }

    internal static IReadOnlyList<KnownAccountBinding> KnownBindings(StoreSnapshot snapshot, ulong connectionId) =>
        snapshot.Accounts.Bindings.Where(b => b.ConnectionId == connectionId).Select(b => new KnownAccountBinding(
            new(b.AccountId), new(b.IncarnationId), new(b.Id), Locator(snapshot, b),
            Enum.TryParse<AccountBindingState>(b.State, out var state) && Enum.IsDefined(state) ? state : AccountBindingState.Quarantined)).ToArray();

    private static AccountSourceLocator Locator(StoreSnapshot snapshot, StoreBindingEntry b)
    {
        var institution = snapshot.Institutions.Institutions.Single(i => i.Id == b.InstitutionId);
        return new(new(b.InstitutionId), new(b.ConnectionId), b.Connector, b.ConnectorVersion,
            ManualInstitutionConfiguration.Create(institution.DisplayName, b.Endpoint, institution.BankCode, institution.Bic),
            b.Iban, b.DomesticAccountNumber, b.DomesticBankCode, b.Subaccount, b.Currency,
            b.BankIdentifiers.Select(i => new BankAccountIdentifier(i.Issuer, i.Scope, i.Kind, i.Value)));
    }

    internal static BankDiscoveryReview Create(HostSession owner, ConnectionSetupContext context, StoreSnapshot snapshot,
        FinTsReadExecutionResult result, IReadOnlyDictionary<AccountSourceLocator, int> versions)
    {
        var session = result.SessionResult;
        if (!result.IsSuccess || session?.AllAccountEvidence is null || session.InitializationEvidence is null ||
            session.ClosingEvidence is null) throw new InvalidOperationException("A closed, qualified discovery is required.");
        var connection = result.UpdatedConnection;
        var parameters = session.InitializationEvidence.Parameters.Accounts;
        var reported = session.AllAccountEvidence.ReadData.Discovery.SelectMany(d => d.Accounts).ToArray();
        var locators = parameters.Where(p => p.HasAccountConnection || !p.Iban.IsEmpty)
            .Select(p => FinTsAccountSourceLocatorFactory.CreateLocator(connection, p)).Select(l =>
            {
                var matches = reported.Where(a => Matches(l, a)).ToArray();
                return matches.Length == 1 ? WithDiscoveryBic(connection, l, matches[0]) : l;
            }).ToList();
        // Preserve HISPA-only accounts as unsupported observations, without inventing their currency.
        foreach (var account in reported)
            if (!locators.Any(l => Matches(l, account))) locators.Add(FinTsAccountSourceLocatorFactory.CreateLocator(connection, account));
        var known = KnownBindings(snapshot, context.Connection.Id);
        using var allocation = DetachedLedger(snapshot).BeginBatch();
        var occurrences = locators.Select(l => new AccountDiscoveryOccurrence(new(allocation.Allocate(LocalEntityKind.Observation,
            [new(new(context.Connection.Id), LocalEntityKind.Connection)]).Value), l)).ToArray();
        var decisions = AccountRediscoveryPlanner.Plan(known, occurrences);
        var rows = new List<BankDiscoveredAccount>();
        foreach (var decision in decisions)
        {
            var locator = decision.Occurrence.Locator;
            int? version = versions.TryGetValue(locator, out int v) ? v : null;
            var matches = reported.Where(a => Matches(locator, a)).ToArray();
            var manual = snapshot.Accounts.Bindings.Where(b => b.ConnectionId == context.Connection.Id && b.InstitutionId == context.Institution.Id &&
                b.Endpoint == locator.Endpoint && b.Connector == "Manual" && b.State == "Quarantined" &&
                !string.IsNullOrEmpty(locator.Iban) && b.Iban == locator.Iban).ToArray();
            BankDiscoveryMatch match = BankDiscoveryMatch.NeedsReview;
            ulong? target = null, binding = null;
            string? display = null;
            string explanation = "Identity is ambiguous, inactive, changed, or incomplete. Keep this account unverified and review its source and lifetime.";
            bool unique = locators.Count(l => l.Equals(locator)) == 1 &&
                (string.IsNullOrEmpty(locator.Iban) || locators.Count(l => l.Iban == locator.Iban) == 1);
            bool qualified = unique && matches.Length == 1 && SepaIbanValidator.TryNormalize(locator.Iban, out string iban) && iban == locator.Iban &&
                !string.IsNullOrEmpty(locator.Currency) && CurrencyCatalog.IsRecognized(locator.Currency);
            if (decision.ReusedBinding is { } reused && snapshot.Accounts.Incarnations.Single(i => i.Id == reused.IncarnationId.Value).LifetimeEvidence == "ClosedReported")
                qualified = false;
            if (qualified && decision.Action == AccountRediscoveryAction.ReuseBinding && decision.ReusedBinding!.Locator.Connector == "fints")
            {
                match = BankDiscoveryMatch.KnownBinding; target = decision.ReusedBinding.AccountId.Value;
                binding = decision.ReusedBinding.BindingId.Value;
                explanation = "The exact active bank source binding was rediscovered.";
            }
            else if (qualified && manual.Length == 1)
            {
                var m = manual[0];
                var account = snapshot.Accounts.Accounts.Single(a => a.Id == m.AccountId);
                var incarnations = snapshot.Accounts.Incarnations.Where(i => i.AccountId == m.AccountId).ToArray();
                bool safeTarget = account.Currency == locator.Currency && m.Currency == locator.Currency &&
                    incarnations.Length == 1 && incarnations[0].Id == m.IncarnationId && incarnations[0].LifetimeEvidence == "Unspecified" &&
                    !snapshot.Accounts.Bindings.Any(b => b.AccountId == m.AccountId && b.Connector != "Manual") &&
                    decision.Candidates.All(b => b.BindingId.Value == m.Id);
                if (safeTarget)
                {
                    match = BankDiscoveryMatch.ManualAccount; target = m.AccountId;
                    explanation = "Confirm this bank-reported account belongs to the manual account. Its local name and history will be preserved.";
                }
                else explanation = "Currency, source bindings, or account lifetime conflicts with the manual account. Association is blocked.";
            }
            else if (qualified && (decision.Action == AccountRediscoveryAction.NewAccountCandidate ||
                decision.Reason == AccountRediscoveryReason.ChangedOrConflictingLocator && decision.Candidates.All(b =>
                    !string.IsNullOrEmpty(b.Locator.Iban) && !string.IsNullOrEmpty(locator.Iban) && b.Locator.Iban != locator.Iban &&
                    (string.IsNullOrEmpty(b.Locator.DomesticAccountNumber) || b.Locator.DomesticAccountNumber != locator.DomesticAccountNumber))))
            {
                match = BankDiscoveryMatch.NewAccount;
                explanation = "Confirm adding this bank-reported account as a separate local account.";
            }
            if (!qualified) explanation = "Missing or conflicting UPD/HISPA identity, duplicate account, or unsupported currency. No association can be confirmed.";
            if (target.HasValue) display = snapshot.Accounts.Accounts.Single(a => a.Id == target).DisplayName;
            if (version is null) explanation += " The bank did not qualify a supported balance request for this account.";
            rows.Add(new(rows.Count, locator, match, target, binding, display, version, explanation));
        }
        return new(owner, context, connection, rows);
    }

    private static bool Matches(AccountSourceLocator locator, FinTsReadAccount account) =>
        !string.IsNullOrEmpty(locator.Iban) && locator.Iban == account.Iban &&
        (string.IsNullOrEmpty(locator.DomesticAccountNumber) || locator.DomesticAccountNumber == account.Number) &&
        (string.IsNullOrEmpty(locator.DomesticBankCode) || locator.DomesticBankCode == account.Institution) &&
        locator.BankIdentifiers.Where(i => i.Kind == "country").All(i => i.Value == account.Country) &&
        (locator.Subaccount ?? "") == account.Subaccount;

    internal static StoreSnapshot Apply(StoreSnapshot snapshot, BankDiscoveryReview review, IReadOnlyCollection<int> confirmed)
    {
        if (confirmed.Distinct().Count() != confirmed.Count || confirmed.Any(i => !review.Accounts.Any(a => a.Index == i && a.CanConfirm)))
            throw new ArgumentException("Only explicitly reviewable account rows can be confirmed.");
        var accounts = snapshot.Accounts.Accounts.ToList();
        var incarnations = snapshot.Accounts.Incarnations.ToList();
        var bindings = snapshot.Accounts.Bindings.ToList();
        var capabilities = snapshot.Accounts.Capabilities.Where(c => c.ConnectionId != review.Context.Connection.Id || c.OperationCode != "HKSAL").ToList();
        var observed = new HashSet<ulong>();
        using var batch = DetachedLedger(snapshot).BeginBatch();
        foreach (var row in review.Accounts.Where(r => r.Match == BankDiscoveryMatch.KnownBinding || confirmed.Contains(r.Index)))
        {
            ulong accountId, incarnationId, bindingId;
            if (row.Match == BankDiscoveryMatch.KnownBinding)
            {
                var existing = bindings.Single(b => b.Id == row.BindingId);
                accountId = existing.AccountId; incarnationId = existing.IncarnationId; bindingId = existing.Id;
            }
            else
            {
                if (row.Match == BankDiscoveryMatch.ManualAccount)
                {
                    accountId = row.AccountId!.Value;
                    incarnationId = incarnations.Single(i => i.AccountId == accountId).Id;
                }
                else
                {
                    accountId = batch.Allocate(LocalEntityKind.Account).Value;
                    incarnationId = batch.Allocate(LocalEntityKind.AccountIncarnation, [new(new(accountId), LocalEntityKind.Account)]).Value;
                    accounts.Add(new(accountId, $"Account {row.Iban ?? row.AccountNumber}", row.Currency!));
                    incarnations.Add(new(incarnationId, accountId, "LiveDialogue"));
                }
                bindingId = batch.Allocate(LocalEntityKind.AccountBinding,
                    [new(new(incarnationId), LocalEntityKind.AccountIncarnation), new(new(review.Context.Connection.Id), LocalEntityKind.Connection)]).Value;
                var l = row.Locator;
                bindings.Add(new(bindingId, accountId, incarnationId, l.InstitutionId.Value, l.ConnectionId.Value, "Active", l.Connector,
                    l.ConnectorVersion, l.Endpoint, l.Iban, l.DomesticAccountNumber, l.DomesticBankCode, l.Subaccount, l.Currency,
                    l.BankIdentifiers.Select(i => (i.Issuer, i.Scope, i.Kind, i.Value)).ToArray()));
            }
            observed.Add(bindingId);
            batch.Allocate(LocalEntityKind.Observation, [new(new(bindingId), LocalEntityKind.AccountBinding)]);
            if (row.BalanceVersion is int version)
                capabilities.Add(new(review.Context.Connection.Id, accountId, "HKSAL", version, "Active", null));
        }
        // An absent/changed source never retains operational permission based on an older discovery.
        bindings = bindings.Select(b => b.ConnectionId == review.Context.Connection.Id && b.Connector == "fints" && b.State == "Active" && !observed.Contains(b.Id)
            ? b with { State = "Quarantined" } : b).ToList();
        var ledger = batch.Commit();
        var updated = snapshot with
        {
            Profile = snapshot.Profile with { NextId = ledger.NextId },
            Connections = snapshot.Connections with
            {
                Connections = snapshot.Connections.Connections.Select(c => c.Id == review.Context.Connection.Id
                ? Broiler.Fond.Kernel.Domain.Connections.Orchestration.MultibankSnapshotMutator.ToStoreEntry(review.Connection) : c).ToArray()
            },
            Accounts = snapshot.Accounts with { Accounts = accounts, Incarnations = incarnations, Bindings = bindings, Capabilities = capabilities },
            Ledger = snapshot.Ledger with
            {
                Records = ledger.Records.Select(r => new StoreLedgerRecord(r.Id.Value, r.Kind.ToString(), r.SupersedesId?.Value,
                r.References.Select(x => (x.TargetId.Value, x.ExpectedKind.ToString())).ToArray())).ToArray()
            }
        };
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(updated);
        return updated;
    }
}
