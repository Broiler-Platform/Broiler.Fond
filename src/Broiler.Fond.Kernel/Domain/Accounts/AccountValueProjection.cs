using System.Collections.ObjectModel;
using System.Numerics;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

public enum AccountBalanceSupport { SupportedCurrentAccount, IdentityOnly }
public enum AccountRefreshOutcome { NotAttempted, Succeeded, Failed, Partial }
public enum AccountValueStatus
{
    Available, Unavailable, Unsupported, InactiveBinding, UnrecognizedCurrency,
    Ambiguous, ProvenanceMismatch, CurrencyMismatch,
}

[Flags]
public enum BalanceFreshness
{
    None = 0,
    SourceMarkedStale = 1,
    RetrievalExpired = 2,
    BankTimeExpired = 4,
    BankTimeMissing = 8,
    FutureTimestamp = 16,
    RefreshFailed = 32,
    RefreshPartial = 64,
    Offline = 128,
    InconsistentTimestamps = 256,
}

public enum BalanceTotalArithmetic { Exact, Unavailable, Unrepresentable }

/// <summary>
/// One selected account and its current value candidates, not its entire history.
/// Replay/history resolution and capability qualification are caller duties.
/// </summary>
public sealed class AccountValueInput
{
    public const int MaximumSnapshots = 64;

    public AccountValueInput(KnownAccountBinding binding, AccountBalanceSupport support,
        IEnumerable<BalanceSnapshot> snapshots, AccountRefreshOutcome refreshOutcome = AccountRefreshOutcome.NotAttempted,
        bool isOffline = false)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (!Enum.IsDefined(support) || !Enum.IsDefined(refreshOutcome))
        {
            throw new ArgumentException("Unknown account support or refresh outcome.");
        }

        List<BalanceSnapshot> copy = [];
        foreach (BalanceSnapshot snapshot in snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (copy.Count == MaximumSnapshots)
            {
                throw new ArgumentException("Account value candidate limit exceeded.");
            }

            copy.Add(snapshot);
        }

        Binding = binding;
        Support = support;
        Snapshots = copy.AsReadOnly();
        RefreshOutcome = refreshOutcome;
        IsOffline = isOffline;
    }

    public KnownAccountBinding Binding { get; }
    public AccountBalanceSupport Support { get; }
    public ReadOnlyCollection<BalanceSnapshot> Snapshots { get; }
    public AccountRefreshOutcome RefreshOutcome { get; }
    public bool IsOffline { get; }
}

public sealed class AccountValueRow
{
    internal AccountValueRow(AccountValueInput input, BalanceKind kind, AccountValueStatus status,
        BalanceSnapshot[] candidates, BalanceFreshness freshness)
    {
        Input = input;
        Kind = kind;
        Status = status;
        Candidates = Array.AsReadOnly(candidates);
        Freshness = freshness;
        Source = candidates.Length == 1 ? candidates[0] : null;
        Value = status == AccountValueStatus.Available ? Source!.Value : null;
    }

    public AccountValueInput Input { get; }
    public BalanceKind Kind { get; }
    public AccountValueStatus Status { get; }
    public ReadOnlyCollection<BalanceSnapshot> Candidates { get; }
    public BalanceSnapshot? Source { get; }
    public Money? Value { get; }
    public BalanceFreshness Freshness { get; }
    public bool IsCurrent => Status == AccountValueStatus.Available && Freshness == BalanceFreshness.None;
}

/// <summary>
/// Totals for one currency and one balance kind only. Positive booked balances
/// and negative booked magnitudes provide separate asset/liability views for the
/// supported current-account scope; available balances are not net-worth totals.
/// </summary>
public sealed class CurrencyBalanceTotal
{
    internal CurrencyBalanceTotal(string currency, BalanceKind kind, IReadOnlyList<AccountValueRow> rows)
    {
        Currency = currency;
        Kind = kind;
        BigInteger positive = 0;
        BigInteger negative = 0;
        foreach (AccountValueRow row in rows)
        {
            if (row.Value is Money money)
            {
                IncludedCount++;
                Freshness |= row.Freshness;
                BigInteger units = ExactDecimal.ToUnits(money.Amount);
                if (units.Sign >= 0)
                {
                    positive += units;
                }
                else
                {
                    negative -= units;
                }
            }
            else
            {
                ExcludedCount++;
            }
        }

        if (IncludedCount == 0)
        {
            Arithmetic = BalanceTotalArithmetic.Unavailable;
            return;
        }

        try
        {
            // Compute all fields before assigning any: precision failure must
            // not leave a plausible-looking partial arithmetic result.
            Money assets = new(ExactDecimal.FromUnits(positive), currency);
            Money liabilities = new(ExactDecimal.FromUnits(negative), currency);
            Money net = new(ExactDecimal.FromUnits(positive - negative), currency);
            PositiveTotal = assets;
            NegativeMagnitude = liabilities;
            NetTotal = net;
            Arithmetic = BalanceTotalArithmetic.Exact;
        }
        catch (OverflowException)
        {
            Arithmetic = BalanceTotalArithmetic.Unrepresentable;
        }
    }

    public string Currency { get; }
    public BalanceKind Kind { get; }
    public BalanceTotalArithmetic Arithmetic { get; }
    public Money? PositiveTotal { get; }
    public Money? NegativeMagnitude { get; }
    public Money? NetTotal { get; }
    public int IncludedCount { get; }
    public int ExcludedCount { get; }
    public bool IsPartial => ExcludedCount != 0;
    public BalanceFreshness Freshness { get; }
    public bool IsCurrentAndComplete => Arithmetic == BalanceTotalArithmetic.Exact && !IsPartial && Freshness == BalanceFreshness.None;
}

public sealed class AccountValueProjection
{
    public const int MaximumAccounts = 10_000;

    private AccountValueProjection(List<AccountValueRow> rows, List<CurrencyBalanceTotal> totals,
        DateTimeOffset evaluatedAt, TimeSpan maximumAge, BalanceKind kind)
    {
        Rows = rows.AsReadOnly();
        Totals = totals.AsReadOnly();
        EvaluatedAt = evaluatedAt;
        MaximumAge = maximumAge;
        Kind = kind;
    }

    public ReadOnlyCollection<AccountValueRow> Rows { get; }
    public ReadOnlyCollection<CurrencyBalanceTotal> Totals { get; }
    public DateTimeOffset EvaluatedAt { get; }
    public TimeSpan MaximumAge { get; }
    public BalanceKind Kind { get; }
    public bool HasExcludedAccounts => Rows.Any(static row => row.Value is null);

    /// <summary>
    /// Pure projection with an explicit clock and freshness policy. It selects no
    /// latest-by-timestamp winner, mutates no history and performs no conversion.
    /// </summary>
    public static AccountValueProjection Create(IEnumerable<AccountValueInput> accounts, DateTimeOffset now,
        TimeSpan maximumAge, BalanceKind kind = BalanceKind.Booked, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (maximumAge <= TimeSpan.Zero || kind is not (BalanceKind.Booked or BalanceKind.Available))
        {
            throw new ArgumentException("A positive freshness window and booked or available balance kind are required.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        List<AccountValueRow> rows = [];
        HashSet<AccountId> accountIds = [];
        HashSet<AccountIncarnationId> incarnationIds = [];
        HashSet<AccountBindingId> bindingIds = [];
        HashSet<AccountSourceLocator> sourceLocators = [];
        HashSet<BalanceSnapshotId> snapshotIds = [];
        Dictionary<ObservationId, (AccountId, AccountIncarnationId, AccountBindingId)> observationOrigins = [];
        Dictionary<ulong, LocalEntityKind> identities = [];
        Dictionary<ConnectionId, InstitutionId> connections = [];
        Dictionary<string, List<AccountValueRow>> groups = new(StringComparer.Ordinal);
        void Register(ulong id, LocalEntityKind identityKind)
        {
            if (identities.TryGetValue(id, out LocalEntityKind previous) && previous != identityKind)
            {
                throw new ArgumentException("Conflicting identity kinds in account value input.");
            }

            identities[id] = identityKind;
        }

        foreach (AccountValueInput account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(account);
            if (rows.Count == MaximumAccounts || !accountIds.Add(account.Binding.AccountId))
            {
                throw new ArgumentException("Account projection limit exceeded or duplicate selected account.");
            }

            KnownAccountBinding binding = account.Binding;
            if (!incarnationIds.Add(binding.IncarnationId) || !bindingIds.Add(binding.BindingId) || !sourceLocators.Add(binding.Locator))
            {
                throw new ArgumentException("Conflicting or indistinguishable selected account identities; resolve before totaling.");
            }
            Register(binding.AccountId.Value, LocalEntityKind.Account);
            Register(binding.IncarnationId.Value, LocalEntityKind.AccountIncarnation);
            Register(binding.BindingId.Value, LocalEntityKind.AccountBinding);
            Register(binding.Locator.InstitutionId.Value, LocalEntityKind.Institution);
            Register(binding.Locator.ConnectionId.Value, LocalEntityKind.Connection);
            if (connections.TryGetValue(binding.Locator.ConnectionId, out InstitutionId institution) && institution != binding.Locator.InstitutionId)
            {
                throw new ArgumentException("Conflicting connection ownership in value input.");
            }

            connections[binding.Locator.ConnectionId] = binding.Locator.InstitutionId;
            foreach (BalanceSnapshot snapshot in account.Snapshots)
            {
                if (!snapshotIds.Add(snapshot.Id))
                {
                    throw new ArgumentException("Duplicate balance snapshot; resolve replay before projection.");
                }

                Register(snapshot.Id.Value, LocalEntityKind.BalanceSnapshot);
                Register(snapshot.ObservationId.Value, LocalEntityKind.Observation);
                Register(snapshot.AccountId.Value, LocalEntityKind.Account);
                Register(snapshot.AccountIncarnationId.Value, LocalEntityKind.AccountIncarnation);
                Register(snapshot.AccountBindingId.Value, LocalEntityKind.AccountBinding);
                var origin = (snapshot.AccountId, snapshot.AccountIncarnationId, snapshot.AccountBindingId);
                if (observationOrigins.TryGetValue(snapshot.ObservationId, out var previousOrigin) && previousOrigin != origin)
                {
                    throw new ArgumentException("One observation cannot have conflicting balance provenance.");
                }

                observationOrigins[snapshot.ObservationId] = origin;
            }

            BalanceSnapshot[] candidates = account.Snapshots.Where(snapshot => snapshot.Kind == kind).ToArray();
            string? currency = binding.Locator.Currency;
            AccountValueStatus status = account.Support == AccountBalanceSupport.IdentityOnly ? AccountValueStatus.Unsupported
                : binding.State != AccountBindingState.Active ? AccountValueStatus.InactiveBinding
                : !CurrencyCatalog.IsRecognized(currency) ? AccountValueStatus.UnrecognizedCurrency
                : candidates.Length > 1 ? AccountValueStatus.Ambiguous
                : candidates.Length == 0 ? AccountValueStatus.Unavailable
                : candidates[0].AccountId != binding.AccountId || candidates[0].AccountIncarnationId != binding.IncarnationId ||
                    candidates[0].AccountBindingId != binding.BindingId ? AccountValueStatus.ProvenanceMismatch
                : candidates[0].Value is null ? AccountValueStatus.Unavailable
                : candidates[0].Value!.Currency != currency ? AccountValueStatus.CurrencyMismatch
                : AccountValueStatus.Available;
            BalanceFreshness freshness = RefreshFreshnessOf(account);
            if (candidates.Length == 1)
            {
                freshness |= FreshnessOf(candidates[0], now, maximumAge);
            }
            AccountValueRow row = new(account, kind, status, candidates, freshness);
            rows.Add(row);
            if (CurrencyCatalog.IsRecognized(currency))
            {
                if (!groups.TryGetValue(currency!, out List<AccountValueRow>? group))
                {
                    groups.Add(currency!, group = []);
                }

                group.Add(row);
            }
        }

        List<CurrencyBalanceTotal> totals = [];
        foreach ((string currency, List<AccountValueRow> group) in groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            totals.Add(new(currency, kind, group));
        }

        return new(rows, totals, now, maximumAge, kind);
    }

    private static BalanceFreshness FreshnessOf(BalanceSnapshot snapshot, DateTimeOffset now, TimeSpan maximumAge)
    {
        BalanceFreshness freshness = snapshot.IsStale ? BalanceFreshness.SourceMarkedStale : BalanceFreshness.None;
        if (snapshot.RetrievedAt > now || snapshot.BankTimestamp > now)
        {
            freshness |= BalanceFreshness.FutureTimestamp;
        }

        if (now - snapshot.RetrievedAt >= maximumAge)
        {
            freshness |= BalanceFreshness.RetrievalExpired;
        }

        if (snapshot.BankTimestamp is DateTimeOffset bankTime)
        {
            if (bankTime > snapshot.RetrievedAt)
            {
                freshness |= BalanceFreshness.InconsistentTimestamps;
            }

            if (now - bankTime >= maximumAge)
            {
                freshness |= BalanceFreshness.BankTimeExpired;
            }
        }
        else
        {
            freshness |= BalanceFreshness.BankTimeMissing;
        }

        return freshness;
    }

    private static BalanceFreshness RefreshFreshnessOf(AccountValueInput account)
    {
        BalanceFreshness freshness = BalanceFreshness.None;
        if (account.RefreshOutcome == AccountRefreshOutcome.Failed)
        {
            freshness |= BalanceFreshness.RefreshFailed;
        }

        if (account.RefreshOutcome == AccountRefreshOutcome.Partial)
        {
            freshness |= BalanceFreshness.RefreshPartial;
        }

        return account.IsOffline ? freshness | BalanceFreshness.Offline : freshness;
    }
}
