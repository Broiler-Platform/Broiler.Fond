using System.Globalization;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests;

internal static class AccountValueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(1);

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyMoney(Verify);
        VerifyValues(Verify);
        VerifyFreshness(Verify);
        VerifyTotals(Verify);
        VerifyInvalidInputs(Verify);
        Console.WriteLine($"Exact money and account values: {checks} checks completed.");
    }

    private static void VerifyMoney(Action<bool, string> verify)
    {
        verify(new Money(0.1m, "EUR").Add(new(0.2m, "EUR")) == new Money(0.3m, "EUR"), "Money addition must preserve exact decimal fractions.");
        verify(new Money(1.001m, "EUR").Amount == 1.001m, "Source precision must not be rounded to currency minor units.");
        verify(Money.ParseExact("0.0000000000000000000000000001", "KWD").Amount == 0.0000000000000000000000000001m, "The smallest decimal unit must survive parsing.");
        verify(Money.ParseExact("79228162514264337593543950335.0", "EUR").Amount == decimal.MaxValue, "Redundant fractional zeros may be removed without precision loss.");
        verify(Money.ParseExact("-79228162514264337593543950335", "EUR").Amount == decimal.MinValue, "Negative decimal boundary must parse exactly.");
        verify(new Money(decimal.MaxValue, "EUR").Add(new(decimal.MinValue, "EUR")).Amount == 0, "Large opposite amounts must cancel exactly.");
        foreach (string currency in new[] { "EUR", "USD", "JPY", "KWD", "GBP", "CHF", "XCG", "ZWG" })
        {
            verify(new Money(1m, currency).Currency == currency, "Recognized currency codes must be preserved.");
        }

        foreach (string invalid in new[] { "", "eur", " EUR", "EUR ", "EU", "EURO", "ZZZ", "XXX", "XTS", "BGN", "€" })
        {
            Throws<ArgumentException>(() => new Money(1, invalid), verify, "Unrecognized/placeholder/noncanonical currency must be rejected.");
        }

        Throws<ArgumentException>(() => new Money(1, null!), verify, "Missing currency must not become an implicit default.");
        Throws<ArgumentException>(() => new Money(1, "EUR").Add(new(1, "USD")), verify, "Cross-currency addition is forbidden.");
        Throws<OverflowException>(() => new Money(decimal.MaxValue, "EUR").Add(new(1, "EUR")), verify, "Money overflow must fail explicitly.");
        Throws<OverflowException>(() => new Money(decimal.MaxValue, "EUR").Add(new(0.1m, "EUR")), verify, "A small amount cannot silently disappear near the decimal boundary.");
        foreach (string invalid in new[] { "", "-", "+1", "01", "-01", ".1", "1.", " 1", "1 ", "1,2", "1e2", "NaN", "١", "1\n", "0.00000000000000000000000000001", new string('1', 65) })
        {
            Throws<FormatException>(() => Money.ParseExact(invalid, "EUR"), verify, "Noncanonical or over-precise amount text must fail.");
        }

        Throws<OverflowException>(() => Money.ParseExact("79228162514264337593543950335.1", "EUR"), verify, "Parsing must not round excess coefficient precision.");
        Throws<OverflowException>(() => Money.ParseExact("79228162514264337593543950336", "EUR"), verify, "Amount text overflow must fail.");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                verify(Money.ParseExact("-1234.567", "EUR").Amount == -1234.567m, "Parsing must be culture-independent.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        verify(new Money(1234.567m, "EUR").ToString() == nameof(Money), "Default money diagnostics must not print financial amounts.");
    }

    private static void VerifyValues(Action<bool, string> verify)
    {
        KnownAccountBinding binding = Binding(10);
        BalanceSnapshot booked = Snapshot(binding, 100, 10m);
        BalanceSnapshot available = Snapshot(binding, 102, 20m, kind: BalanceKind.Available);
        BalanceSnapshot credit = Snapshot(binding, 104, 500m, kind: BalanceKind.CreditLine);
        AccountValueInput input = new(binding, AccountBalanceSupport.SupportedCurrentAccount, [booked, available, credit]);
        var projection = Project([input]);
        verify(projection.Rows[0].Value?.Amount == 10 && ReferenceEquals(projection.Rows[0].Source, booked), "Booked values retain exact source provenance.");
        verify(Project([input], BalanceKind.Available).Rows[0].Value?.Amount == 20, "Available values must remain distinct from booked balances.");
        verify(projection.Totals[0].PositiveTotal?.Amount == 10, "Credit lines and available balances cannot inflate booked assets.");
        verify(Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [available, credit])]).Rows[0].Status == AccountValueStatus.Unavailable,
            "Missing booked balance must not fall back to another balance kind.");
        verify(Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [Snapshot(binding, 100, null)])]).Rows[0].Value is null,
            "Unavailable must remain null, not zero.");
        verify(Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [Snapshot(binding, 100, 0)])]).Rows[0].Value?.Amount == 0,
            "A supplied zero must remain an available value.");
        var unsupported = Project([new(binding, AccountBalanceSupport.IdentityOnly, [booked])]);
        verify(unsupported.Rows[0].Status == AccountValueStatus.Unsupported && unsupported.Rows[0].Value is null && unsupported.Rows[0].Source == booked,
            "Unsupported account shells retain evidence but do not expose supported balance semantics.");
        var ambiguous = Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [booked, Snapshot(binding, 102, 10)])]);
        verify(ambiguous.Rows[0].Status == AccountValueStatus.Ambiguous && ambiguous.Rows[0].Candidates.Count == 2 && ambiguous.Rows[0].Value is null,
            "Identical value bytes from two occurrences do not establish replay or select a winner.");
        BalanceSnapshot newest = Snapshot(binding, 102, 99, retrievedAt: Now.AddMinutes(1));
        verify(Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [booked, newest])]).Rows[0].Status == AccountValueStatus.Ambiguous,
            "A timestamp cannot silently choose among unresolved current candidates.");
        KnownAccountBinding relinked = new(binding.AccountId, binding.IncarnationId, new(90), binding.Locator, AccountBindingState.Active);
        var oldBinding = Project([new(relinked, AccountBalanceSupport.SupportedCurrentAccount, [booked])]);
        verify(oldBinding.Rows[0].Status == AccountValueStatus.ProvenanceMismatch && booked.AccountBindingId == binding.BindingId,
            "Relinking cannot rewrite old balance binding provenance.");
        KnownAccountBinding reopened = new(binding.AccountId, new(91), binding.BindingId, binding.Locator, AccountBindingState.Active);
        verify(Project([new(reopened, AccountBalanceSupport.SupportedCurrentAccount, [booked])]).Rows[0].Status == AccountValueStatus.ProvenanceMismatch,
            "A different account incarnation cannot reuse an old value.");
        BalanceSnapshot foreign = Snapshot(binding, 100, 10, currency: "USD");
        verify(Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [foreign])]).Rows[0].Status == AccountValueStatus.CurrencyMismatch,
            "Source money must match the selected account's exact currency.");
        KnownAccountBinding unknownCurrency = Binding(10, "ZZZ");
        verify(Project([new(unknownCurrency, AccountBalanceSupport.SupportedCurrentAccount, [])]).Rows[0].Status == AccountValueStatus.UnrecognizedCurrency,
            "An unknown source currency remains a visible excluded account.");
        foreach (AccountBindingState state in new[] { AccountBindingState.Closed, AccountBindingState.Quarantined })
        {
            KnownAccountBinding inactive = new(binding.AccountId, binding.IncarnationId, binding.BindingId, binding.Locator, state);
            verify(Project([new(inactive, AccountBalanceSupport.SupportedCurrentAccount, [booked])]).Rows[0].Status == AccountValueStatus.InactiveBinding,
                "Inactive bindings cannot contribute visible current balances.");
        }

        BalanceSnapshot labelled = new(new(100), new(101), binding.AccountId, binding.IncarnationId, binding.BindingId,
            BalanceKind.Other, new Money(1, "EUR"), Now, Now, sourceLabel: "bank-type-42");
        verify(labelled.SourceLabel == "bank-type-42" && Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [labelled])]).Rows[0].Value is null,
            "Unmapped source labels survive without becoming booked balances.");
    }

    private static void VerifyFreshness(Action<bool, string> verify)
    {
        KnownAccountBinding binding = Binding(10);
        BalanceSnapshot current = Snapshot(binding, 100, 1);
        verify(Project([Input(binding, current)]).Rows[0].IsCurrent, "Recent bank and retrieval timestamps may satisfy the explicit freshness policy.");
        AccountValueProjection evaluated = Project([Input(binding, current)]);
        verify(evaluated.EvaluatedAt == Now && evaluated.MaximumAge == MaximumAge && evaluated.Kind == BalanceKind.Booked,
            "A projection must expose the clock and policy used for its freshness judgments.");
        (BalanceSnapshot Snapshot, BalanceFreshness Flag)[] cases =
        [
            (Snapshot(binding, 100, 1, retrievedAt: Now - MaximumAge), BalanceFreshness.RetrievalExpired),
            (Snapshot(binding, 100, 1, bankTime: Now - MaximumAge), BalanceFreshness.BankTimeExpired),
            (Snapshot(binding, 100, 1, bankTime: Now.AddTicks(1)), BalanceFreshness.FutureTimestamp),
            (Snapshot(binding, 100, 1, retrievedAt: Now.AddTicks(1)), BalanceFreshness.FutureTimestamp),
            (Snapshot(binding, 100, 1, stale: true), BalanceFreshness.SourceMarkedStale),
            (Snapshot(binding, 100, 1, bankTime: Now.AddMinutes(-5), retrievedAt: Now.AddMinutes(-10)), BalanceFreshness.InconsistentTimestamps),
        ];
        foreach ((BalanceSnapshot snapshot, BalanceFreshness flag) in cases)
        {
            AccountValueRow row = Project([Input(binding, snapshot)]).Rows[0];
            verify(row.Freshness.HasFlag(flag) && !row.IsCurrent && row.Value?.Amount == 1, "Freshness warnings must not erase cached values.");
        }

        BalanceSnapshot missingBankTime = new(new(100), new(101), binding.AccountId, binding.IncarnationId, binding.BindingId,
            BalanceKind.Booked, new Money(1, "EUR"), null, Now);
        verify(Project([Input(binding, missingBankTime)]).Rows[0].Freshness.HasFlag(BalanceFreshness.BankTimeMissing), "Retrieval time cannot substitute for missing bank time.");
        foreach (AccountRefreshOutcome outcome in new[] { AccountRefreshOutcome.Failed, AccountRefreshOutcome.Partial })
        {
            var projection = Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [current], outcome)]);
            verify(!projection.Rows[0].IsCurrent && projection.Rows[0].Value == current.Value && !projection.Totals[0].IsCurrentAndComplete,
                "Failed or partial refresh must preserve cached value with an explicit freshness warning.");
        }

        var offline = Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [current], isOffline: true)]);
        verify(offline.Rows[0].Freshness.HasFlag(BalanceFreshness.Offline) && offline.Rows[0].Value == current.Value, "Offline cached view must be visibly offline.");
        DateTimeOffset offsetTime = Now.ToOffset(TimeSpan.FromHours(2));
        BalanceSnapshot offsetSnapshot = Snapshot(binding, 100, 1, bankTime: offsetTime);
        verify(Project([Input(binding, offsetSnapshot)]).Rows[0].IsCurrent && offsetSnapshot.BankTimestamp?.Offset == TimeSpan.FromHours(2),
            "Timestamp comparison uses instants while retaining the bank's offset.");
        verify(Project([Input(binding, Snapshot(binding, 100, 1, bankTime: Now - MaximumAge + TimeSpan.FromTicks(1)))]).Rows[0].IsCurrent,
            "Freshness expiry boundary must be exact.");
        verify(!AccountValueProjection.Create([Input(binding, current)], Now + MaximumAge, MaximumAge).Rows[0].IsCurrent && current.BankTimestamp == Now,
            "Reprojection must age a value without changing its immutable source timestamps.");
    }

    private static void VerifyTotals(Action<bool, string> verify)
    {
        var projection = Project([Value(10, 100, 100), Value(20, 102, -25), Value(30, 104, 50, "USD"), Value(40, 106, null)]);
        verify(projection.Totals.Count == 2 && projection.Totals[0].Currency == "EUR" && projection.Totals[1].Currency == "USD", "Currencies must have separate deterministic total groups.");
        CurrencyBalanceTotal eur = projection.Totals[0];
        verify(eur.PositiveTotal?.Amount == 100 && eur.NegativeMagnitude?.Amount == 25 && eur.NetTotal?.Amount == 75,
            "Positive booked balances and negative magnitudes must be separately inspectable.");
        verify(eur.IncludedCount == 2 && eur.ExcludedCount == 1 && eur.IsPartial && !eur.IsCurrentAndComplete,
            "Excluded accounts must make a currency subtotal visibly partial.");
        verify(projection.HasExcludedAccounts && projection.Totals[1].NetTotal?.Amount == 50, "Exclusions cannot silently change another currency's arithmetic.");
        var none = Project([Value(10, 100, null)]).Totals[0];
        verify(none.Arithmetic == BalanceTotalArithmetic.Unavailable && none.NetTotal is null && none.PositiveTotal is null,
            "An all-missing total must not become zero.");
        verify(Project([Value(10, 100, 0)]).Totals[0].NetTotal?.Amount == 0, "A real zero balance can yield an exact zero total.");
        verify(Project([]).Totals.Count == 0, "An empty selection must not invent a currency or zero total.");
        var overflow = Project([Value(10, 100, decimal.MaxValue), Value(20, 102, 1)]).Totals[0];
        verify(overflow.Arithmetic == BalanceTotalArithmetic.Unrepresentable && overflow.NetTotal is null && overflow.PositiveTotal is null,
            "Overflow must not produce partial or saturated arithmetic fields.");
        var lostPrecision = Project([Value(10, 100, decimal.MaxValue), Value(20, 102, 0.1m)]).Totals[0];
        verify(lostPrecision.Arithmetic == BalanceTotalArithmetic.Unrepresentable, "Totals must reject implicit decimal precision loss.");
        var exactFractions = Project([Value(10, 100, 0.1m), Value(20, 102, 0.2m)]).Totals[0];
        verify(exactFractions.NetTotal?.Amount == 0.3m, "Fractional totals must remain exact.");
        var reverse = Project([Value(20, 102, -25), Value(10, 100, 100)]).Totals[0];
        verify(reverse.NetTotal == eur.NetTotal, "Arithmetic must not depend on account enumeration order.");
        var signOverflow = Project([Value(10, 100, decimal.MaxValue), Value(20, 102, decimal.MaxValue), Value(30, 104, decimal.MinValue)]).Totals[0];
        verify(signOverflow.Arithmetic == BalanceTotalArithmetic.Unrepresentable, "A representable net must not conceal an unrepresentable asset subtotal.");
    }

    private static void VerifyInvalidInputs(Action<bool, string> verify)
    {
        KnownAccountBinding binding = Binding(10);
        BalanceSnapshot snapshot = Snapshot(binding, 100, 1);
        AccountValueInput input = Input(binding, snapshot);
        Throws<ArgumentException>(() => Project([input, input]), verify, "Duplicate selected accounts cannot be counted twice.");
        Throws<ArgumentException>(() => Project([new(binding, AccountBalanceSupport.SupportedCurrentAccount, [snapshot, snapshot])]), verify, "Duplicate snapshot IDs require replay resolution.");
        KnownAccountBinding sameBinding = new(new(20), new(21), binding.BindingId, Binding(20).Locator, AccountBindingState.Active);
        Throws<ArgumentException>(() => Project([input, new(sameBinding, AccountBalanceSupport.SupportedCurrentAccount, [])]), verify, "A binding cannot back two selected accounts.");
        KnownAccountBinding sameLocator = new(new(20), new(21), new(22), binding.Locator, AccountBindingState.Active);
        Throws<ArgumentException>(() => Project([input, new(sameLocator, AccountBalanceSupport.SupportedCurrentAccount, [])]), verify, "Indistinguishable active sources cannot be totaled as two accounts.");
        Throws<ArgumentException>(() => Project([input], BalanceKind.CreditLine), verify, "Credit limits must not be requested as account asset totals.");
        Throws<ArgumentException>(() => AccountValueProjection.Create([input], Now, TimeSpan.Zero), verify, "Freshness policy must be explicit and positive.");
        Throws<OperationCanceledException>(() => AccountValueProjection.Create([input], Now, MaximumAge, cancellationToken: new CancellationToken(true)), verify, "Projection honors cancellation.");
        Throws<ArgumentException>(() => new AccountValueInput(binding, AccountBalanceSupport.SupportedCurrentAccount, Enumerable.Repeat(snapshot, 65)), verify, "Per-account snapshots must be bounded.");
        Throws<ArgumentException>(() => new BalanceSnapshot(new(10), new(101), binding.AccountId, binding.IncarnationId, binding.BindingId,
            BalanceKind.Booked, null, Now, Now), verify, "Snapshot IDs cannot collide with account identities.");
        Throws<ArgumentException>(() => new BalanceSnapshot(new(100), new(101), binding.AccountId, binding.IncarnationId, binding.BindingId,
            BalanceKind.Unknown, null, Now, Now), verify, "Unknown balance types require source labels.");
        List<BalanceSnapshot> source = [snapshot];
        AccountValueInput copy = new(binding, AccountBalanceSupport.SupportedCurrentAccount, source);
        source.Clear();
        verify(copy.Snapshots.Count == 1, "Account input must detach from caller-owned collections.");
        var result = Project([copy]);
        Throws<NotSupportedException>(() => ((IList<AccountValueRow>)result.Rows).Clear(), verify, "Projection rows must be immutable.");
        Throws<NotSupportedException>(() => ((IList<BalanceSnapshot>)result.Rows[0].Candidates).Clear(), verify, "Candidate evidence must be immutable.");
        var large = Enumerable.Range(0, AccountValueProjection.MaximumAccounts).Select(i => Value((ulong)(10 + i * 3), (ulong)(100_000 + i * 2), 1)).ToArray();
        verify(Project(large).Totals[0].NetTotal?.Amount == AccountValueProjection.MaximumAccounts, "A complete 10,000-account projection must remain exact.");
        Throws<ArgumentException>(() => Project(large.Append(Value(40_000, 130_000, 1))), verify, "Selected account count must be bounded.");
    }

    private static KnownAccountBinding Binding(ulong account, string currency = "EUR")
    {
        AccountSourceLocator locator = new(new(1), new(2), "fints", "1", ManualInstitutionConfiguration.Create("Synthetic", "https://bank.example/fints"),
            "SYNTHETIC-" + account, null, null, null, currency);
        return new(new(account), new(account + 1), new(account + 2), locator, AccountBindingState.Active);
    }

    private static BalanceSnapshot Snapshot(KnownAccountBinding binding, ulong id, decimal? amount, string? currency = null,
        BalanceKind kind = BalanceKind.Booked, DateTimeOffset? bankTime = null, DateTimeOffset? retrievedAt = null, bool stale = false) =>
        new(new(id), new(id + 1), binding.AccountId, binding.IncarnationId, binding.BindingId, kind,
            amount is decimal value ? new Money(value, currency ?? binding.Locator.Currency!) : null, bankTime ?? Now, retrievedAt ?? Now, stale);

    private static AccountValueInput Input(KnownAccountBinding binding, BalanceSnapshot snapshot) => new(binding, AccountBalanceSupport.SupportedCurrentAccount, [snapshot]);
    private static AccountValueInput Value(ulong account, ulong id, decimal? amount, string currency = "EUR")
    {
        KnownAccountBinding binding = Binding(account, currency);
        return Input(binding, Snapshot(binding, id, amount));
    }

    private static AccountValueProjection Project(IEnumerable<AccountValueInput> accounts, BalanceKind kind = BalanceKind.Booked) =>
        AccountValueProjection.Create(accounts, Now, MaximumAge, kind);

    private static void Throws<T>(Action action, Action<bool, string> verify, string message) where T : Exception
    {
        try { action(); verify(false, message); }
        catch (T) { verify(true, message); }
    }
}
