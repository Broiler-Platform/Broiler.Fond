using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Presentation.Services;

public sealed record BankProcedureSelection(FinTsPinTanProcedureContext Procedures, FinTsPinTanSignatureSelection Selection);
public interface IBankProcedureProvider
{
    /// <summary>Explicit, qualified provenance for this bank context. Null means unavailable; never select a fallback procedure.</summary>
    Task<BankProcedureSelection?> GetBankProcedureAsync(ConnectionSetupContext context, CancellationToken cancellationToken = default);
}
public sealed record BankBatchRequest(IReadOnlyList<ulong>? ConnectionIds = null, IReadOnlyList<ulong>? AccountIds = null, bool DiscoverAccounts = false);
public enum BankBatchAccountOutcome { Updated, Unchanged, Unavailable, Unsupported, Failed, Cancelled }
public enum BankBatchConnectionOutcome { Completed, Partial, Blocked, Failed, Cancelled, AlreadyRunning }
public enum BankBatchBlockReason { None, ProfileUnavailable, ConnectionSetupRequired, DiscoveryRequired, ProcedureRequired, RetryBackoff, ReviewUnavailable, InvalidSelection }
public enum BankBatchStage { Checking, Waiting, SigningIn, Discovering, Reviewing, Reading, Saving, Completed }
public sealed record BankBatchProgress(ulong ConnectionId, BankBatchStage Stage, ulong? AccountId = null);
public sealed record BankBatchAccountResult(ulong ConnectionId, ulong AccountId, BankBatchAccountOutcome Outcome, string Explanation,
    BankBalanceOutcome? Detail = null, string? Currency = null, decimal? Booked = null, decimal? Available = null,
    int SavedObservations = 0, DateTimeOffset? RetrievedAt = null)
{
    public bool IsDurableSuccess => Outcome is BankBatchAccountOutcome.Updated or BankBatchAccountOutcome.Unchanged;
}
public sealed record BankBatchConnectionResult(ulong ConnectionId, BankBatchConnectionOutcome Outcome, string Explanation,
    IReadOnlyList<BankBatchAccountResult> Accounts, BankBatchBlockReason BlockReason = BankBatchBlockReason.None);
public sealed record BankBatchCurrencyTotal(string Currency, decimal? RefreshedBookedTotal, int SavedAccounts, int OtherAccounts, bool Overflowed);
public sealed record BankBatchResult(IReadOnlyList<BankBatchConnectionResult> Connections, IReadOnlyList<BankBatchAccountResult> UnassignedAccounts,
    string? Explanation = null)
{
    public IEnumerable<BankBatchAccountResult> Accounts => Connections.SelectMany(c => c.Accounts).Concat(UnassignedAccounts);
    public int Updated => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Updated);
    public int Unchanged => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Unchanged);
    public int Unavailable => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Unavailable);
    public int Unsupported => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Unsupported);
    public int Failed => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Failed);
    public int Cancelled => Accounts.Count(a => a.Outcome == BankBatchAccountOutcome.Cancelled);
    public int SavedObservations => Accounts.Sum(a => a.IsDurableSuccess ? a.SavedObservations : 0);
    /// <summary>Only this run's durable successes; excludes old cached values and partitions currencies.</summary>
    public IReadOnlyList<BankBatchCurrencyTotal> CurrencyTotals => Accounts.Where(a => a.Currency is not null).GroupBy(a => a.Currency!)
        .Select(group =>
        {
            var saved = group.Where(a => a.IsDurableSuccess).ToArray();
            Money? sum = null;
            bool overflow = false;
            try { foreach (var row in saved.Where(a => a.Booked.HasValue)) { var value = new Money(row.Booked!.Value, group.Key); sum = sum is null ? value : sum.Add(value); } }
            catch (Exception ex) when (ex is OverflowException or ArgumentException) { overflow = true; sum = null; }
            return new BankBatchCurrencyTotal(group.Key, sum?.Amount, saved.Length, group.Count() - saved.Length, overflow);
        }).OrderBy(t => t.Currency, StringComparer.Ordinal).ToList().AsReadOnly();
}

public interface IBankBatchRefreshService : IDisposable
{
    Task<BankBatchResult> RefreshAsync(BankBatchRequest request, IBankCredentialPrompt prompt, IBankProcedureProvider procedures,
        IBankAccountDiscoveryReview? reviewer = null, IProgress<BankBatchProgress>? progress = null, CancellationToken cancellationToken = default);
}
