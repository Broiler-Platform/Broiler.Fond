using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests;

internal static class AccountRediscoveryTests
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyLocators(Verify);
        VerifyDecisions(Verify);
        VerifyInvalidInputs(Verify);
        VerifyLedgerIntegration(Verify);
        Console.WriteLine($"Lossless account locators and rediscovery: {checks} checks completed.");
    }

    private static void VerifyLocators(Action<bool, string> verify)
    {
        BankAccountIdentifier first = new("issuer", "scope", "account", "00123");
        BankAccountIdentifier second = new("issuer", "scope", "other", "Grüße 東京");
        List<BankAccountIdentifier> input = [first, second, first];
        AccountSourceLocator original = Source(identifiers: input);
        input.Clear();
        AccountSourceLocator reordered = Source(identifiers: [first, first, second]);
        verify(original.Equals(reordered) && original.GetHashCode() == reordered.GetHashCode(),
            "Bank identifier order must not change exact identity.");
        verify(original.BankIdentifiers.SequenceEqual(new[] { first, second, first }), "Original bank identifier order and duplicate occurrences must remain intact.");
        verify(!original.Equals(Source(identifiers: [first, second])), "Identifier multiplicity must affect exact equality.");
        verify(!original.HasConflictingIdentifiers, "Repeated equal identifier values are not contradictory.");
        verify(Source(identifiers: [first, new("issuer", "scope", "account", "00456")]).HasConflictingIdentifiers,
            "Different values of the same scoped bank field must be flagged.");
        verify(!Source(identifiers: [first, new("issuer", "different-scope", "account", "00456")]).HasConflictingIdentifiers,
            "Separate identifier scopes must not be conflated.");
        verify(!Source(iban: null).Equals(Source(iban: "")), "Missing and empty source fields must remain distinct.");
        verify(Source(iban: " de 001 ", domestic: "0000123").Iban == " de 001 " && Source(domestic: "0000123").DomesticAccountNumber == "0000123",
            "Raw whitespace, case and leading zeros must be preserved.");
        verify(!Source(iban: "de001").Equals(Source(iban: "DE001")), "Case folding cannot establish exact identity.");
        verify(!Source(iban: "DE 001").Equals(Source(iban: "DE001")), "Whitespace normalization cannot establish exact identity.");
        verify(!Source(iban: "é").Equals(Source(iban: "e\u0301")), "Source identifiers must not inherit passphrase NFC normalization.");
        foreach (BankAccountIdentifier changed in new BankAccountIdentifier[]
        {
            new("another-issuer", "scope", "account", "00123"), new("issuer", "another-scope", "account", "00123"),
            new("issuer", "scope", "another-kind", "00123"), new("issuer", "scope", "account", "123"),
        })
        {
            verify(!Source(identifiers: [first]).Equals(Source(identifiers: [changed])), "Every scoped identifier component must participate in exact equality.");
        }

        ManualInstitutionConfiguration reviewed = ManualInstitutionConfiguration.Create("Different friendly name", "https://bank.example/fints");
        reviewed = reviewed.ConfirmEndpoint(reviewed.FinTsEndpoint.AbsoluteUri);
        AccountSourceLocator reviewedSource = new(new(1), new(2), "fints", "1", reviewed, "DE001", "00001", "12345678", null, "EUR");
        verify(Source().Equals(reviewedSource), "Friendly labels and endpoint-review state are separate from source identity.");

        AccountSourceLocator[] different =
        [
            Source(institution: 7, connection: 8), Source(connection: 9), Source(connector: "other"), Source(version: "2"),
            Source(endpoint: "https://bank.example/changed"), Source(iban: "DE002"), Source(domestic: "00002"),
            Source(bankCode: "87654321"), Source(subaccount: "01"), Source(currency: "USD"),
            Source(identifiers: [first]),
        ];
        foreach (AccountSourceLocator changed in different)
        {
            verify(!Source().Equals(changed), "Every source context and locator component must participate in equality.");
        }

        Dictionary<AccountSourceLocator, string> collisionIndex = new(new ConstantHashComparer())
        {
            [Source(iban: "DE001")] = "first",
            [Source(iban: "DE002")] = "second",
        };
        verify(collisionIndex.Count == 2 && collisionIndex[Source(iban: "DE002")] == "second",
            "Hash collisions must still compare complete source locators.");
        verify(!Source(iban: null, domestic: null, bankCode: null).HasAccountIdentifier,
            "Context and currency alone cannot identify an account.");
        verify(Source(iban: null, domestic: null, bankCode: null, identifiers: [first]).HasAccountIdentifier,
            "A scoped nonempty bank identifier can supply account evidence.");
        Throws<NotSupportedException>(() => ((IList<BankAccountIdentifier>)original.BankIdentifiers).Clear(), verify, "Bank identifiers must be immutable.");
        Throws<ArgumentException>(() => Source(iban: new string('x', 1025)), verify, "Raw fields must be bounded.");
        Throws<ArgumentException>(() => Source(identifiers: Enumerable.Repeat(first, 33)), verify, "Bank field count must be bounded.");
        verify(Source(iban: new string('x', 1024), identifiers: Enumerable.Repeat(first, 32)).BankIdentifiers.Count == 32,
            "Documented field and identifier limits must be inclusive.");
        const string sentinel = "SENTINEL-BANK-IDENTIFIER";
        verify(!Source(iban: sentinel).ToString().Contains(sentinel, StringComparison.Ordinal) &&
            !new BankAccountIdentifier("issuer", "scope", "kind", sentinel).ToString().Contains(sentinel, StringComparison.Ordinal),
            "Default diagnostics must not print source identifier values.");
    }

    private static void VerifyDecisions(Action<bool, string> verify)
    {
        KnownAccountBinding known = Binding(Source());
        AccountDiscoveryOccurrence occurrence = new(new(100), Source());
        AccountRediscoveryDecision exact = AccountRediscoveryPlanner.Plan([known], [occurrence])[0];
        verify(exact.Action == AccountRediscoveryAction.ReuseBinding && ReferenceEquals(exact.ReusedBinding, known),
            "A unique exact active binding must preserve all existing local identities.");
        verify(ReferenceEquals(exact.Occurrence, occurrence) && ReferenceEquals(known.Locator, exact.ReusedBinding!.Locator),
            "Rediscovery must retain occurrence and binding provenance unchanged.");
        AccountRediscoveryDecision later = AccountRediscoveryPlanner.Plan([known], [new(new(101), Source())])[0];
        verify(later.ReusedBinding?.BindingId == known.BindingId && later.Occurrence.ObservationId != occurrence.ObservationId,
            "Repeated discovery preserves the binding but not the identity of a distinct received occurrence.");

        AccountSourceLocator[] changes =
        [
            Source(connection: 9), Source(institution: 7, connection: 8), Source(connector: "other"), Source(version: "2"),
            Source(endpoint: "https://other.example/fints"), Source(endpoint: "https://bank.example/Fints"),
            Source(endpoint: "https://bank.example:8443/fints"), Source(endpoint: "https://bank.example/fints?mode=2"),
            Source(iban: "DE002"), Source(iban: "de001"), Source(iban: "DE 001"), Source(domestic: "00002"),
            Source(bankCode: "87654321"), Source(subaccount: "01"), Source(currency: "USD"), Source(currency: null),
        ];
        foreach (AccountSourceLocator changed in changes)
        {
            AccountRediscoveryDecision decision = AccountRediscoveryPlanner.Plan([known], [new(new(100), changed)])[0];
            verify(decision.Action == AccountRediscoveryAction.QuarantinedCandidate && decision.ReusedBinding is null && decision.Candidates.Contains(known),
                "Changed connection, connector, endpoint or account evidence must not replace the existing binding.");
        }

        foreach (AccountBindingState state in new[] { AccountBindingState.Closed, AccountBindingState.Quarantined })
        {
            AccountRediscoveryDecision inactive = AccountRediscoveryPlanner.Plan([Binding(Source(), state)], [occurrence])[0];
            verify(inactive.Reason == AccountRediscoveryReason.InactiveBinding && inactive.ReusedBinding is null,
                "An exact locator cannot revive a closed or quarantined binding.");
        }

        foreach (AccountLifetimeEvidence evidence in new[] { AccountLifetimeEvidence.NewLifetimeReported, AccountLifetimeEvidence.ClosedReported })
        {
            AccountRediscoveryDecision lifecycle = AccountRediscoveryPlanner.Plan([known], [new(new(100), Source(), evidence)])[0];
            verify(lifecycle.Action == AccountRediscoveryAction.QuarantinedCandidate && lifecycle.ReusedBinding is null,
                "Explicit bank lifetime evidence must override exact locator equality.");
        }

        KnownAccountBinding duplicate = Binding(Source(), account: 20, incarnation: 21, binding: 22);
        AccountRediscoveryDecision ambiguous = AccountRediscoveryPlanner.Plan([duplicate, known], [occurrence])[0];
        verify(ambiguous.Reason == AccountRediscoveryReason.AmbiguousBindings && ambiguous.Candidates.Count == 2 && ambiguous.ReusedBinding is null,
            "Multiple exact local bindings must remain ambiguous.");
        verify(ambiguous.Candidates[0].BindingId == known.BindingId, "Review candidate order must be stable by binding ID.");
        foreach (KnownAccountBinding[] inventory in new[] { new[] { known }, Array.Empty<KnownAccountBinding>() })
        {
            var repeated = AccountRediscoveryPlanner.Plan(inventory, [occurrence, new(new(101), Source())]);
            verify(repeated.Count == 2 && repeated.All(static decision => decision.Reason == AccountRediscoveryReason.DuplicateOccurrences && decision.ReusedBinding is null),
                "Identical rows in one batch must retain multiplicity instead of consuming one binding twice.");
            verify(repeated[0].Occurrence.ObservationId != repeated[1].Occurrence.ObservationId, "Duplicate-looking rows retain separate occurrence IDs.");
        }

        KnownAccountBinding other = Binding(Source(iban: "DE002", subaccount: "01"), account: 20, incarnation: 21, binding: 22);
        var twoMatches = AccountRediscoveryPlanner.Plan([known, other], [new(new(100), Source()), new(new(101), other.Locator)]);
        verify(twoMatches.All(static decision => decision.Action == AccountRediscoveryAction.ReuseBinding) &&
            twoMatches.Select(static decision => decision.ReusedBinding!.BindingId).Distinct().Count() == 2,
            "Distinct exact tuples must preserve one-to-one matching despite shared domestic evidence.");
        AccountRediscoveryDecision newAccount = AccountRediscoveryPlanner.Plan([], [occurrence])[0];
        verify(newAccount.Action == AccountRediscoveryAction.NewAccountCandidate && newAccount.ReusedBinding is null,
            "First discovery proposes a new account without allocating or claiming a binding.");
        AccountSourceLocator insufficient = Source(iban: null, domestic: null, bankCode: null);
        verify(AccountRediscoveryPlanner.Plan([Binding(insufficient)], [new(new(100), insufficient)])[0].Reason == AccountRediscoveryReason.InsufficientIdentity,
            "Even a unique full tuple with no account identifier must not be reused.");
        AccountSourceLocator conflicting = Source(identifiers: [new("i", "s", "k", "one"), new("i", "s", "k", "two")]);
        verify(AccountRediscoveryPlanner.Plan([Binding(conflicting)], [new(new(100), conflicting)])[0].Reason == AccountRediscoveryReason.ConflictingBankIdentifiers,
            "A contradictory identifier list cannot establish exact rediscovery.");
        verify(AccountRediscoveryPlanner.Plan([known], []).Count == 0 && known.State == AccountBindingState.Active,
            "Absence from a response must not mark a known account closed.");
        BankAccountIdentifier scoped = new("issuer", "scope", "account", "opaque-value");
        AccountSourceLocator bankOnly = Source(iban: null, domestic: null, bankCode: null, identifiers: [scoped]);
        verify(AccountRediscoveryPlanner.Plan([Binding(bankOnly)], [new(new(100), bankOnly)])[0].Action == AccountRediscoveryAction.ReuseBinding,
            "Exact bank-specific identity can rediscover an account without an IBAN.");
        AccountSourceLocator changedBankOnly = Source(connection: 9, iban: null, domestic: null, bankCode: null, identifiers: [scoped]);
        verify(AccountRediscoveryPlanner.Plan([Binding(bankOnly)], [new(new(100), changedBankOnly)])[0].Action == AccountRediscoveryAction.QuarantinedCandidate,
            "Shared scoped bank evidence can only suggest review across connections.");
        AccountSourceLocator unrelated = Source(institution: 7, connection: 8, iban: "DE999", domestic: "99999");
        verify(AccountRediscoveryPlanner.Plan([known], [new(new(100), unrelated)])[0].Action == AccountRediscoveryAction.NewAccountCandidate,
            "An unrelated institution and source identity can propose a separate new account.");
    }

    private static void VerifyInvalidInputs(Action<bool, string> verify)
    {
        KnownAccountBinding known = Binding(Source());
        AccountDiscoveryOccurrence observation = new(new(100), Source());
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([known, known], [observation]), verify, "Duplicate binding IDs must fail closed.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([known], [observation, observation]), verify, "Duplicate observation IDs require durable replay handling elsewhere.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([known], [new(new(10), Source())]), verify, "Occurrence IDs cannot collide with account IDs.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([known, Binding(Source(), account: 20, incarnation: 11, binding: 22)], [observation]), verify,
            "An incarnation cannot belong to multiple accounts.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([known], [new(new(100), Source(institution: 7))]), verify,
            "A connection cannot belong to multiple institutions.");
        Throws<ArgumentException>(() => Source(institution: 0), verify, "Invalid context IDs must be rejected.");
        Throws<ArgumentException>(() => Binding(Source(), account: 1), verify, "Binding identities must use separate entity IDs.");
        Throws<ArgumentException>(() => new AccountDiscoveryOccurrence(new(0), Source()), verify, "Occurrence IDs must be nonzero.");
        Throws<ArgumentException>(() => new AccountDiscoveryOccurrence(new(100), Source(), (AccountLifetimeEvidence)999), verify, "Unknown lifetime evidence must fail closed.");
        Throws<OperationCanceledException>(() => AccountRediscoveryPlanner.Plan([known], [observation], new CancellationToken(true)), verify, "Planning must honor cancellation.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan(Enumerable.Repeat(known, AccountRediscoveryPlanner.MaximumBindings + 1), []), verify, "Known inventory must be bounded before indexing.");
        Throws<ArgumentException>(() => AccountRediscoveryPlanner.Plan([], Enumerable.Repeat(observation, AccountRediscoveryPlanner.MaximumOccurrences + 1)), verify, "Incoming occurrence inventory must be bounded.");
        KnownAccountBinding[] candidates = Enumerable.Range(0, 400).Select(i => Binding(Source(), account: (ulong)(1000 + 3 * i),
            incarnation: (ulong)(1001 + 3 * i), binding: (ulong)(1002 + 3 * i))).ToArray();
        AccountDiscoveryOccurrence[] occurrences = Enumerable.Range(0, 400).Select(i => new AccountDiscoveryOccurrence(new((ulong)(10_000 + i)), Source())).ToArray();
        Throws<InvalidOperationException>(() => AccountRediscoveryPlanner.Plan(candidates, occurrences), verify,
            "Ambiguous candidate cross-products must fail at the total link bound without returning a partial plan.");
        var plan = AccountRediscoveryPlanner.Plan([known], [observation]);
        Throws<NotSupportedException>(() => ((IList<AccountRediscoveryDecision>)plan).Clear(), verify, "Returned plans must be immutable.");
        Throws<NotSupportedException>(() => ((IList<KnownAccountBinding>)plan[0].Candidates).Clear(), verify, "Candidate lists must be immutable.");

        KnownAccountBinding[] largeInventory = Enumerable.Range(0, AccountRediscoveryPlanner.MaximumBindings)
            .Select(i => Binding(Source(iban: "SYNTHETIC-" + i), account: (ulong)(1000 + 3 * i),
                incarnation: (ulong)(1001 + 3 * i), binding: (ulong)(1002 + 3 * i))).ToArray();
        AccountDiscoveryOccurrence[] largeBatch = largeInventory.Select((item, index) =>
            new AccountDiscoveryOccurrence(new((ulong)(100_000 + index)), item.Locator)).ToArray();
        var largePlan = AccountRediscoveryPlanner.Plan(largeInventory, largeBatch);
        verify(largePlan.Count == AccountRediscoveryPlanner.MaximumOccurrences &&
            largePlan.All(static decision => decision.Action == AccountRediscoveryAction.ReuseBinding),
            "The inclusive 10,000-binding/occurrence limit must support a complete exact-match batch.");
    }

    private static void VerifyLedgerIntegration(Action<bool, string> verify)
    {
        _ = LocalIdentityLedger.TryOpen(IdentitySnapshot.Empty(), out LocalIdentityLedger? ledger);
        using IdentityAllocationBatch batch = ledger!.BeginBatch();
        InstitutionId institution = new(batch.Allocate(LocalEntityKind.Institution).Value);
        ConnectionId connection = new(batch.Allocate(LocalEntityKind.Connection, [new(new(institution.Value), LocalEntityKind.Institution)]).Value);
        AccountId account = new(batch.Allocate(LocalEntityKind.Account).Value);
        AccountIncarnationId incarnation = new(batch.Allocate(LocalEntityKind.AccountIncarnation, [new(new(account.Value), LocalEntityKind.Account)]).Value);
        AccountBindingId binding = new(batch.Allocate(LocalEntityKind.AccountBinding,
            [new(new(incarnation.Value), LocalEntityKind.AccountIncarnation), new(new(connection.Value), LocalEntityKind.Connection)]).Value);
        ObservationId observation = new(batch.Allocate(LocalEntityKind.Observation).Value);
        IdentitySnapshot committed = batch.Commit();
        AccountSourceLocator locator = Source(institution: institution.Value, connection: connection.Value);
        KnownAccountBinding known = new(account, incarnation, binding, locator, AccountBindingState.Active);
        AccountRediscoveryDecision result = AccountRediscoveryPlanner.Plan([known], [new(observation, locator)])[0];
        verify(result.ReusedBinding?.AccountId == account && result.ReusedBinding.IncarnationId == incarnation && result.ReusedBinding.BindingId == binding,
            "Rediscovery must preserve the three distinct ledger-allocated account identities.");
        verify(ReferenceEquals(committed, ledger.Snapshot), "Planning must not advance the allocator or mutate an identity snapshot.");
    }

    private static AccountSourceLocator Source(ulong institution = 1, ulong connection = 2, string connector = "fints",
        string version = "1", string endpoint = "https://bank.example/fints", string? iban = "DE001", string? domestic = "00001",
        string? bankCode = "12345678", string? subaccount = null, string? currency = "EUR", IEnumerable<BankAccountIdentifier>? identifiers = null) =>
        new(new(institution), new(connection), connector, version, ManualInstitutionConfiguration.Create("Synthetic bank", endpoint),
            iban, domestic, bankCode, subaccount, currency, identifiers);

    private static KnownAccountBinding Binding(AccountSourceLocator locator, AccountBindingState state = AccountBindingState.Active,
        ulong account = 10, ulong incarnation = 11, ulong binding = 12) => new(new(account), new(incarnation), new(binding), locator, state);

    private sealed class ConstantHashComparer : IEqualityComparer<AccountSourceLocator>
    {
        public bool Equals(AccountSourceLocator? x, AccountSourceLocator? y) => x?.Equals(y) ?? y is null;
        public int GetHashCode(AccountSourceLocator obj) => 0;
    }

    private static void Throws<T>(Action action, Action<bool, string> verify, string message) where T : Exception
    {
        try
        {
            action();
            verify(false, message);
        }
        catch (T)
        {
            verify(true, message);
        }
    }
}
