using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

/// <summary>BR-01 coordination only; production online execution remains unavailable.</summary>
internal sealed class BankRefreshService : IBankRefreshService
{
    private readonly HostSession _session;
    private readonly IBalanceRefreshOperation? _operation;
    private readonly ConnectionBackoffTracker _backoff;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentDictionary<(long Epoch, ulong Connection), ActiveOperation> _active = new();
    private volatile bool _disposed;

    internal BankRefreshService(HostSession session, IBalanceRefreshOperation? operation = null,
        ConnectionBackoffTracker? backoff = null, TimeProvider? clock = null, int maximumConcurrency = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _session = session;
        _operation = operation;
        _backoff = backoff ?? new ConnectionBackoffTracker();
        _clock = clock ?? TimeProvider.System;
        _slots = new(maximumConcurrency);
        _session.RefreshContextChanged += OnContextChanged;
    }

    public async Task<BalanceRefreshResult> RefreshAsync(BalanceRefreshRequest request,
        IProgress<BalanceRefreshProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RefreshSessionState state = _session.CaptureRefreshState();
        var global = ProfileIssues(state, _disposed || cancellationToken.IsCancellationRequested);
        if (!_session.ProductConfiguration.IsConfigured)
            global.Add(new(BalanceRefreshOutcome.ProductConfigurationMissing, _session.ProductConfiguration.Error!, "Ask the product owner to configure the assigned FinTS application product ID."));

        StoreSnapshot? snapshot = state.Snapshot;
        ulong[] ids = (request.ConnectionIds ?? snapshot?.Connections.Connections.Select(c => c.Id).ToArray() ?? []).Distinct().ToArray();
        if (ids.Length == 0)
            global.Add(Issue(BalanceRefreshOutcome.MissingConnection));

        var unassigned = snapshot?.Accounts.Accounts.Where(a => !snapshot.Accounts.Bindings.Any(b => b.AccountId == a.Id))
            .Select(a => new BalanceRefreshAccountResult(a.Id, [Issue(BalanceRefreshOutcome.UnverifiedBinding)])).ToArray() ?? [];
        var tasks = ids.Select(id => RunConnectionAsync(id, state, global, progress, cancellationToken)).ToArray();
        BalanceRefreshConnectionResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new(global.AsReadOnly(), results, unassigned);
    }

    private async Task<BalanceRefreshConnectionResult> RunConnectionAsync(ulong id, RefreshSessionState state,
        IReadOnlyList<BalanceRefreshIssue> global, IProgress<BalanceRefreshProgress>? progress, CancellationToken cancellationToken)
    {
        var issues = new List<BalanceRefreshIssue>(global);
        StoreSnapshot? snapshot = state.Snapshot;
        StoreConnectionEntry? connection = snapshot?.Connections.Connections.FirstOrDefault(c => c.Id == id);
        StoreInstitutionEntry? institution = snapshot?.Institutions.Institutions.FirstOrDefault(i => i.Id == connection?.InstitutionId);
        var bindings = snapshot?.Accounts.Bindings.Where(b => b.ConnectionId == id).ToArray() ?? [];
        var capabilities = snapshot?.Accounts.Capabilities.Where(c => c.ConnectionId == id).ToArray() ?? [];
        var accounts = bindings.GroupBy(b => b.AccountId).Select(group =>
        {
            var accountIssues = new List<BalanceRefreshIssue>();
            StoreBindingEntry[] candidates = group.ToArray();
            if (candidates.All(b => b.Connector == "Manual"))
                accountIssues.Add(Issue(BalanceRefreshOutcome.ManualAccount));
            if (candidates.Count(b => b.Connector != "Manual" && b.State == "Active") != 1)
                accountIssues.Add(Issue(BalanceRefreshOutcome.UnverifiedBinding));
            if (!capabilities.Any(c => c.AccountId == group.Key && c.OperationCode == "HKSAL" && c.OperationVersion > 0 && c.Status == "Active"))
                accountIssues.Add(Issue(BalanceRefreshOutcome.UnsupportedOperation));
            return new BalanceRefreshAccountResult(group.Key, accountIssues.AsReadOnly());
        }).ToArray();

        if (connection is null || institution is null)
            issues.Add(Issue(BalanceRefreshOutcome.MissingConnection));
        else
        {
            try { _ = BankConnectionSetup.ToDomain(connection, institution); }
            catch (ArgumentException)
            {
                issues.Add(new(BalanceRefreshOutcome.ConfigurationInvalid,
                "Saved bank settings need review.", "Edit the bank login and endpoint settings before signing in."));
            }
            if (connection.State == "Disabled")
                issues.Add(Issue(BalanceRefreshOutcome.Disabled));
            if (string.IsNullOrWhiteSpace(connection.UserId) || connection.UserId == "default-user")
                issues.Add(Issue(BalanceRefreshOutcome.MissingLogin));
            if (institution.VerificationState != "Confirmed" || !Uri.TryCreate(institution.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                issues.Add(Issue(BalanceRefreshOutcome.EndpointUnconfirmed));
            if (_backoff.IsBackoffActive(new ConnectionId(id), _clock.GetUtcNow(), out _))
                issues.Add(Issue(BalanceRefreshOutcome.Backoff));
        }
        if (accounts.Length == 0)
            issues.Add(Issue(BalanceRefreshOutcome.MissingAccount));
        else if (accounts.All(a => a.Issues.Count > 0))
            issues.AddRange(accounts.SelectMany(a => a.Issues).Distinct());

        progress?.Report(new(id, BalanceRefreshStage.Preflight));
        if (issues.Count > 0)
            return Result(issues[0].Code, issues, BalanceRefreshStage.Preflight);
        if (_operation is null)
            return Result(BalanceRefreshOutcome.Unavailable, [Issue(BalanceRefreshOutcome.Unavailable)], BalanceRefreshStage.Preflight);

        var context = new BalanceRefreshContext(snapshot!.Profile.ProfileId, snapshot.Profile.BranchId, state.Epoch,
            connection!, institution!, bindings, capabilities, accounts.Where(a => a.Issues.Count == 0).Select(a => a.AccountId).ToArray());
        using var active = new ActiveOperation(context, cancellationToken);
        var key = (state.Epoch, id);
        if (!_active.TryAdd(key, active))
            return Result(BalanceRefreshOutcome.AlreadyRunning, [Issue(BalanceRefreshOutcome.AlreadyRunning)], BalanceRefreshStage.Waiting);
        if (!_session.TryReserveBankOperation(state.Epoch, id))
        {
            _active.TryRemove(key, out _);
            return Result(BalanceRefreshOutcome.AlreadyRunning, [Issue(BalanceRefreshOutcome.AlreadyRunning)], BalanceRefreshStage.Waiting);
        }
        bool entered = false;
        bool sessionEntered = false;
        try
        {
            CheckContext();
            progress?.Report(new(id, BalanceRefreshStage.Waiting));
            await _slots.WaitAsync(active.Token).ConfigureAwait(false);
            entered = true;
            await _session.WaitForBankOperationSlotAsync(active.Token).ConfigureAwait(false);
            sessionEntered = true;
            CheckContext();
            // Backoff can start while this connection waits for a concurrency slot.
            if (_backoff.IsBackoffActive(new ConnectionId(id), _clock.GetUtcNow(), out _))
                return Result(BalanceRefreshOutcome.Backoff, [Issue(BalanceRefreshOutcome.Backoff)], BalanceRefreshStage.Preflight);
            progress?.Report(new(id, BalanceRefreshStage.Executing));
            await _operation.ExecuteAsync(context, new BalanceRefreshInteraction(_session.BankPromptGate, active.Token), active.Token).ConfigureAwait(false);
            CheckContext();
            // No durable read/commit is implemented in BR-01; never report a fake success.
            return Result(BalanceRefreshOutcome.Unavailable, [Issue(BalanceRefreshOutcome.Unavailable)], BalanceRefreshStage.Completed);
        }
        catch (OperationCanceledException) when (active.Token.IsCancellationRequested)
        {
            var outcome = !_session.IsRefreshContextCurrent(context) ? BalanceRefreshOutcome.ContextChanged : BalanceRefreshOutcome.Cancelled;
            return Result(outcome, [Issue(outcome)], BalanceRefreshStage.Completed);
        }
        catch (Exception)
        {
            return Result(BalanceRefreshOutcome.Failed, [Issue(BalanceRefreshOutcome.Failed)], BalanceRefreshStage.Completed);
        }
        finally
        {
            if (entered) _slots.Release();
            if (sessionEntered) _session.ReleaseBankOperationSlot();
            _active.TryRemove(key, out _);
            _session.ReleaseBankOperation(state.Epoch, id);
            progress?.Report(new(id, BalanceRefreshStage.Completed));
        }

        void CheckContext()
        {
            if (_disposed || !_session.IsRefreshContextCurrent(context)) active.Cancel();
            active.Token.ThrowIfCancellationRequested();
        }
        BalanceRefreshConnectionResult Result(BalanceRefreshOutcome outcome, IReadOnlyList<BalanceRefreshIssue> reasons, BalanceRefreshStage stage) =>
            new(id, outcome, stage, reasons, accounts);
    }

    private void OnContextChanged()
    {
        foreach (ActiveOperation active in _active.Values)
            if (!_session.IsRefreshContextCurrent(active.Context)) active.Cancel();
    }

    internal static List<BalanceRefreshIssue> ProfileIssues(RefreshSessionState state, bool cancelled)
    {
        var issues = new List<BalanceRefreshIssue>();
        if (cancelled || state.Disposed) issues.Add(Issue(BalanceRefreshOutcome.Cancelled));
        if (state.Snapshot is null) issues.Add(Issue(BalanceRefreshOutcome.ProfileLocked));
        if (state.ReadOnly) issues.Add(Issue(BalanceRefreshOutcome.ProfileReadOnly));
        if (state.SafeMode) issues.Add(Issue(BalanceRefreshOutcome.SafeMode));
        return issues;
    }

    public void Dispose()
    {
        _disposed = true;
        _session.RefreshContextChanged -= OnContextChanged;
        foreach (ActiveOperation active in _active.Values) active.Cancel();
        // Gates remain alive until outstanding operations unwind; no OS wait handles are created.
    }

    private sealed class ActiveOperation(BalanceRefreshContext context, CancellationToken cancellationToken) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        public BalanceRefreshContext Context { get; } = context;
        public CancellationToken Token => _cancellation.Token;
        public void Cancel()
        {
            try { _ = _cancellation.CancelAsync(); }
            catch (ObjectDisposedException) { /* Operation already completed. */ }
        }
        public void Dispose() => _cancellation.Dispose();
    }

    private static BalanceRefreshIssue Issue(BalanceRefreshOutcome code) => code switch
    {
        BalanceRefreshOutcome.ProfileLocked => new(code, "The profile is locked.", "Unlock the profile."),
        BalanceRefreshOutcome.ProfileReadOnly => new(code, "The profile is read-only.", "Open a writable profile before refreshing."),
        BalanceRefreshOutcome.SafeMode => new(code, "Safe Mode blocks bank operations.", "Resolve the Safe Mode condition first."),
        BalanceRefreshOutcome.MissingConnection => new(code, "The bank connection is missing.", "Configure or select an existing bank connection."),
        BalanceRefreshOutcome.MissingAccount => new(code, "No account is bound to this connection.", "Discover and verify the bank accounts."),
        BalanceRefreshOutcome.MissingLogin => new(code, "Bank login details are missing.", "Enter the bank user ID in connection setup."),
        BalanceRefreshOutcome.EndpointUnconfirmed => new(code, "The HTTPS bank endpoint is not confirmed.", "Verify and confirm the exact bank endpoint."),
        BalanceRefreshOutcome.ManualAccount => new(code, "Manual account details are not bank verification.", "Discover and review this account with the bank."),
        BalanceRefreshOutcome.UnverifiedBinding => new(code, "The account has no unique active bank binding.", "Review its discovered identity before refreshing."),
        BalanceRefreshOutcome.UnsupportedOperation => new(code, "No supported balance operation is recorded.", "Discover bank capabilities; this account may not support balance retrieval."),
        BalanceRefreshOutcome.Backoff => new(code, "The bank connection is in a retry backoff window.", "Wait for backoff to expire before retrying."),
        BalanceRefreshOutcome.Disabled => new(code, "The connection is disabled.", "Enable the connection before refreshing."),
        BalanceRefreshOutcome.AlreadyRunning => new(code, "A refresh is already active for this connection.", "Wait for it to finish or cancel it."),
        BalanceRefreshOutcome.Cancelled => new(code, "Refresh was cancelled.", "Start a new refresh when ready."),
        BalanceRefreshOutcome.ContextChanged => new(code, "The profile or connection changed during refresh.", "Review the current context and start again."),
        BalanceRefreshOutcome.Failed => new(code, "The refresh workflow failed; no balances were committed.", "Retry or contact support if the problem persists."),
        _ => new(code, "Online balance execution and persistence are not yet connected.", "This build cannot refresh online balances yet.")
    };
}
