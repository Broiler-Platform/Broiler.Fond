using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Presentation.Services;

public enum BalanceRefreshOutcome
{
    Unavailable, ProfileLocked, ProfileReadOnly, SafeMode, MissingConnection, MissingAccount,
    MissingLogin, EndpointUnconfirmed, ManualAccount, UnverifiedBinding, UnsupportedOperation,
    Backoff, Disabled, AlreadyRunning, Cancelled, ContextChanged, Failed, ProductConfigurationMissing, ConfigurationInvalid
}

public enum BalanceRefreshStage { Preflight, Waiting, Executing, Completed }

/// <summary>Null connection IDs means all configured connections; no network work is implied.</summary>
public sealed record BalanceRefreshRequest(IReadOnlyList<ulong>? ConnectionIds = null);
public sealed record BalanceRefreshIssue(BalanceRefreshOutcome Code, string Explanation, string RecoveryAction);
public sealed record BalanceRefreshAccountResult(ulong AccountId, IReadOnlyList<BalanceRefreshIssue> Issues, int CommittedBalances = 0);
public sealed record BalanceRefreshConnectionResult(ulong ConnectionId, BalanceRefreshOutcome Outcome, BalanceRefreshStage Stage,
    IReadOnlyList<BalanceRefreshIssue> Issues, IReadOnlyList<BalanceRefreshAccountResult> Accounts, int CommittedBalances = 0);
public sealed record BalanceRefreshResult(IReadOnlyList<BalanceRefreshIssue> Issues, IReadOnlyList<BalanceRefreshConnectionResult> Connections,
    IReadOnlyList<BalanceRefreshAccountResult> UnassignedAccounts)
{
    public int CommittedBalances => Connections.Sum(c => c.CommittedBalances);
}
public sealed record BalanceRefreshProgress(ulong ConnectionId, BalanceRefreshStage Stage);

/// <summary>Identity captured before dispatch; record equality protects login/endpoint configuration too.</summary>
public sealed record BalanceRefreshContext(Guid ProfileId, Guid BranchId, long SessionEpoch,
    StoreConnectionEntry Connection, StoreInstitutionEntry Institution, IReadOnlyList<StoreBindingEntry> Bindings,
    IReadOnlyList<StoreCapabilityEntry> Capabilities, IReadOnlyList<ulong> EligibleAccountIds)
{
    // The exact endpoint plus confirmation state is the endpoint revision for BR-01.
    public string EndpointRevision => Institution.Endpoint + "\n" + Institution.VerificationState;
}

public interface IBankRefreshService : IDisposable
{
    Task<BalanceRefreshResult> RefreshAsync(BalanceRefreshRequest request,
        IProgress<BalanceRefreshProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Integration seam for later milestones. It receives no writable profile store.
/// BR-01 cannot report committed success: bank reads and durable ingestion arrive in BR-03–BR-06.
/// Implementations must honor cancellation, use only EligibleAccountIds for bank reads,
/// and route interactive prompts through the supplied gate.
/// </summary>
internal interface IBalanceRefreshOperation
{
    Task ExecuteAsync(BalanceRefreshContext context, BalanceRefreshInteraction interaction, CancellationToken cancellationToken);
}

internal sealed class BalanceRefreshInteraction(SemaphoreSlim gate, CancellationToken operationCancellation)
{
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> prompt, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operationCancellation, cancellationToken);
        await gate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            return await prompt(cancellation.Token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
