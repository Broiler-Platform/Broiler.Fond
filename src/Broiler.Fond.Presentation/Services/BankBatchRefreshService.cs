using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

/// <summary>Account-granular durable refresh. One scoped PIN per connection, separate closed dialogues per account.</summary>
internal sealed class BankBatchRefreshService : IBankBatchRefreshService
{
    private readonly HostSession _session;
    private readonly SemaphoreSlim _workers;
    private readonly TimeProvider _clock;
    private readonly Func<ulong, ulong?, IFinTsTransport>? _transport;
    private readonly Action<ulong, StoreSnapshot>? _beforeSave;
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _runs = new();
    private long _nextRun;
    private volatile bool _disposed;

    internal BankBatchRefreshService(HostSession session, int maximumConcurrency = 2, TimeProvider? clock = null,
        Func<ulong, ulong?, IFinTsTransport>? transport = null, Action<ulong, StoreSnapshot>? beforeSave = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrency, 1);
        _session = session; _workers = new(maximumConcurrency); _clock = clock ?? TimeProvider.System; _transport = transport; _beforeSave = beforeSave;
    }

    public async Task<BankBatchResult> RefreshAsync(BankBatchRequest request, IBankCredentialPrompt prompt, IBankProcedureProvider procedures,
        IBankAccountDiscoveryReview? reviewer = null, IProgress<BankBatchProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(prompt); ArgumentNullException.ThrowIfNull(procedures);
        var state = _session.CaptureRefreshState();
        var snapshot = state.Snapshot;
        ulong[] ids = (request.ConnectionIds ?? snapshot?.Connections.Connections.Select(c => c.Id).ToArray() ?? []).Distinct().ToArray();
        ulong[]? accountIds = request.AccountIds?.Distinct().ToArray();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long runId = Interlocked.Increment(ref _nextRun);
        _runs.TryAdd(runId, run);
        if (_disposed) await run.CancelAsync();
        try
        {
            var results = await Task.WhenAll(ids.Select(id => RunConnectionAsync(id)));
            var assigned = results.SelectMany(c => c.Accounts).Select(a => a.AccountId).ToHashSet();
            var unassigned = (accountIds ?? snapshot?.Accounts.Accounts.Select(a => a.Id).ToArray() ?? []).Where(id => !assigned.Contains(id) &&
                (accountIds is not null || !snapshot!.Accounts.Bindings.Any(b => b.AccountId == id)))
                .Select(id => new BankBatchAccountResult(0, id, BankBatchAccountOutcome.Unavailable, "This account has no source in the selected bank connections.")).ToList().AsReadOnly();
            return new(results.ToList().AsReadOnly(), unassigned, ids.Length == 0 ? "No bank connections were selected." : null);
        }
        finally { _runs.TryRemove(runId, out _); }

        async Task<BankBatchConnectionResult> RunConnectionAsync(ulong id)
        {
            var rows = new Dictionary<ulong, BankBatchAccountResult>();
            ulong[] inventory = snapshot?.Accounts.Bindings.Where(b => b.ConnectionId == id && (accountIds is null || accountIds.Contains(b.AccountId)))
                .Select(b => b.AccountId).Distinct().ToArray() ?? [];
            var backoff = _session.GetBatchBackoff();
            ConnectionSetupContext? setup = null;
            BankConnectionBatchLease? lease = null;
            SemaphoreSlim? institutionGate = null;
            bool institutionEntered = false, workerEntered = false;
            FinTsSessionCredential? pin = null;
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(run.Token);
            lifetime.CancelAfter(TimeSpan.FromMinutes(5));
            using var clear = lifetime.Token.Register(() => pin?.Dispose());
            void Invalidate()
            {
                if (setup is null) return;
                try { _ = _session.RequireBatchConnection(setup, allowContactChanges: true); }
                catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
            }
            void Report(BankBatchStage stage, ulong? account = null)
            { try { progress?.Report(new(id, stage, account)); } catch (Exception) { /* An observer cannot undo a bank commit. */ } }
            void Fill(BankBatchAccountOutcome outcome, string reason, BankBalanceOutcome? detail = null)
            {
                foreach (ulong account in inventory)
                    rows.TryAdd(account, new(id, account, outcome, reason, detail, snapshot?.Accounts.Accounts.FirstOrDefault(a => a.Id == account)?.Currency));
            }
            BankBatchConnectionResult Finish(BankBatchConnectionOutcome outcome, string reason, BankBatchBlockReason blockReason = BankBatchBlockReason.None) =>
                new(id, rows.Values.Any(a => a.IsDurableSuccess) && rows.Values.Any(a => !a.IsDurableSuccess) ? BankBatchConnectionOutcome.Partial : outcome,
                    reason, rows.Values.OrderBy(a => a.AccountId).ToList().AsReadOnly(), blockReason);
            _session.RefreshContextChanged += Invalidate;
            try
            {
                Report(BankBatchStage.Checking);
                lifetime.Token.ThrowIfCancellationRequested();
                if (snapshot is null || state.Disposed || state.ReadOnly || state.SafeMode || !_session.ProductConfiguration.IsConfigured)
                { Fill(BankBatchAccountOutcome.Unavailable, "Unlock a writable profile outside Safe Mode and configure the assigned application product ID."); return Finish(BankBatchConnectionOutcome.Blocked, "Profile or product configuration blocks bank access.", BankBatchBlockReason.ProfileUnavailable); }
                if (!snapshot.Connections.Connections.Any(c => c.Id == id) || accountIds is { Length: 0 })
                { Fill(BankBatchAccountOutcome.Unavailable, "The connection is missing or no accounts were selected."); return Finish(BankBatchConnectionOutcome.Blocked, "No eligible selection is available.", BankBatchBlockReason.InvalidSelection); }
                setup = _session.GetConnectionSetupContext(id);
                if (setup.SessionEpoch != state.Epoch || setup.ProfileId != snapshot.Profile.ProfileId || setup.BranchId != snapshot.Profile.BranchId)
                    throw new OperationCanceledException(lifetime.Token);
                _ = _session.RequireBatchConnection(setup);
                if (setup.Connection.State != "Connected" || !setup.Connection.IsSynchronized || setup.Institution.VerificationState != "Confirmed")
                { Fill(BankBatchAccountOutcome.Unavailable, "The bank connection is disabled, unconfirmed, or requires synchronization."); return Finish(BankBatchConnectionOutcome.Blocked, "Confirm and synchronize this connection before batch refresh.", BankBatchBlockReason.ConnectionSetupRequired); }
                if (request.DiscoverAccounts && reviewer is null)
                { Fill(BankBatchAccountOutcome.Unavailable, "Discovery requires an explicit account reviewer."); return Finish(BankBatchConnectionOutcome.Blocked, "No account reviewer is available.", BankBatchBlockReason.ReviewUnavailable); }
                if (!request.DiscoverAccounts)
                {
                    inventory = Preflight(inventory);
                    if (inventory.All(a => rows.ContainsKey(a))) return Finish(BankBatchConnectionOutcome.Blocked, "No verified supported account is eligible.", BankBatchBlockReason.DiscoveryRequired);
                }
                if (backoff.IsBackoffActive(new(id), _clock.GetUtcNow(), out _))
                { Fill(BankBatchAccountOutcome.Unavailable, "This connection is in a retry backoff window. Wait before trying again."); return Finish(BankBatchConnectionOutcome.Blocked, "Connection retry backoff is active.", BankBatchBlockReason.RetryBackoff); }
                if (!_session.TryReserveBankOperation(state.Epoch, id))
                { Fill(BankBatchAccountOutcome.Unavailable, "Another operation already owns this bank connection.", BankBalanceOutcome.AlreadyRunning); return Finish(BankBatchConnectionOutcome.AlreadyRunning, "A bank operation is already running."); }
                lease = new(_session, state.Epoch, id);
                institutionGate = _session.BankInstitutionGate(setup.Institution.Id);
                Report(BankBatchStage.Waiting);
                await institutionGate.WaitAsync(lifetime.Token); institutionEntered = true;
                await _workers.WaitAsync(lifetime.Token); workerEntered = true;
                Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
                if (backoff.IsBackoffActive(new(id), _clock.GetUtcNow(), out _))
                { Fill(BankBatchAccountOutcome.Unavailable, "Connection retry backoff became active while waiting."); return Finish(BankBatchConnectionOutcome.Blocked, "Connection retry backoff is active.", BankBatchBlockReason.RetryBackoff); }
                var selected = await procedures.GetBankProcedureAsync(setup, lifetime.Token);
                Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
                if (selected is null)
                { Fill(BankBatchAccountOutcome.Unsupported, "Bank procedure provenance is unavailable; no sign-in was started."); return Finish(BankBatchConnectionOutcome.Blocked, "No supported bank procedure is configured.", BankBatchBlockReason.ProcedureRequired); }
                _ = new FinTsProductionCandidateFactory(BankConnectionSetup.ToDomain(setup.Connection, setup.Institution),
                    _session.ProductConfiguration.Registration!, selected.Procedures, selected.Selection);
                Report(BankBatchStage.SigningIn);
                await _session.BankPromptGate.WaitAsync(lifetime.Token);
                try
                {
                    Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
                    pin = await prompt.AcquireBankCredentialAsync(new("Refresh Bank Accounts", "Enter your banking PIN, not the profile passphrase. It will be used for the selected accounts of this connection.",
                        setup.Institution.DisplayName), lifetime.Token);
                }
                finally { _session.BankPromptGate.Release(); }
                Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
                if (pin is null) { Fill(BankBatchAccountOutcome.Cancelled, "Bank sign-in was cancelled."); return Finish(BankBatchConnectionOutcome.Cancelled, "Bank sign-in was cancelled."); }
                if (request.DiscoverAccounts)
                {
                    Report(BankBatchStage.Discovering);
                    var discovered = await _session.DiscoverBankAccountsCoreAsync(_session.GetConnectionSetupContext(id), pin, selected.Procedures, selected.Selection,
                        _transport is null ? null : () => _transport(id, null), lifetime.Token, lease);
                    if (discovered.Review is not { } review)
                    {
                        if (discovered.ProtocolStatus == FinTsReadExecutionStatus.RejectedTransportFailure)
                            backoff.RecordTransientFailure(new(id), "Discovery transport failed.", _clock.GetUtcNow());
                        var outcome = discovered.Outcome is BankDiscoveryOutcome.Cancelled or BankDiscoveryOutcome.ContextChanged ? BankBatchAccountOutcome.Cancelled :
                            discovered.Outcome == BankDiscoveryOutcome.Unsupported ? BankBatchAccountOutcome.Unsupported : BankBatchAccountOutcome.Failed;
                        Fill(outcome, discovered.Explanation);
                        return Finish(outcome == BankBatchAccountOutcome.Cancelled ? BankBatchConnectionOutcome.Cancelled : BankBatchConnectionOutcome.Failed,
                            "Discovery did not qualify accounts for balance reads.");
                    }
                    Report(BankBatchStage.Reviewing);
                    IReadOnlyCollection<int>? confirmed;
                    await _session.BankPromptGate.WaitAsync(lifetime.Token);
                    try { confirmed = await reviewer!.ReviewBankAccountsAsync(review, lifetime.Token); }
                    finally { _session.BankPromptGate.Release(); }
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (confirmed is null) { Fill(BankBatchAccountOutcome.Cancelled, "Discovery review was cancelled."); return Finish(BankBatchConnectionOutcome.Cancelled, "Discovery review was cancelled."); }
                    var reviewed = _session.ConfirmBankAccountDiscovery(review, confirmed);
                    if (reviewed.Outcome != BankDiscoveryOutcome.Saved)
                    { Fill(BankBatchAccountOutcome.Failed, reviewed.Explanation); return Finish(BankBatchConnectionOutcome.Failed, "Discovery review could not be saved."); }
                    var latest = _session.RequireBatchConnection(setup, allowContactChanges: true);
                    inventory = latest.Accounts.Bindings.Where(b => b.ConnectionId == id && (accountIds is null || accountIds.Contains(b.AccountId)))
                        .Select(b => b.AccountId).Distinct().ToArray();
                    inventory = Preflight(inventory);
                }
                foreach (ulong account in inventory.Where(a => !rows.ContainsKey(a)))
                {
                    Invalidate(); lifetime.Token.ThrowIfCancellationRequested();
                    Report(BankBatchStage.Reading, account);
                    BankBalanceContext target;
                    try { target = _session.GetBankBalanceContext(account); }
                    catch (InvalidOperationException) { rows[account] = new(id, account, BankBatchAccountOutcome.Unavailable, "The verified account source changed before its read."); continue; }
                    var before = Values(_session.CurrentSnapshot!, account);
                    var result = await _session.RefreshBankAccountBalanceReservedAsync(target, pin, selected.Procedures, selected.Selection,
                        _transport is null ? null : () => _transport(id, account), _beforeSave is null ? null : s => _beforeSave(account, s), lifetime.Token, lease,
                        () => Report(BankBatchStage.Saving, account));
                    if (result.IsSuccess)
                    {
                        var committed = result.SavedBalance!;
                        var after = (committed.Currency, committed.Booked, committed.Available, committed.CreditLine);
                        rows[account] = new(id, account, before == after ? BankBatchAccountOutcome.Unchanged : BankBatchAccountOutcome.Updated,
                            "The bank balance was validated and durably saved.", result.Outcome, after.Currency, after.Booked, after.Available, result.SavedObservations,
                            committed.RetrievedAt);
                        backoff.Reset(new(id));
                    }
                    else
                    {
                        var outcome = result.Outcome switch
                        {
                            BankBalanceOutcome.Cancelled or BankBalanceOutcome.ContextChanged => BankBatchAccountOutcome.Cancelled,
                            BankBalanceOutcome.Unsupported => BankBatchAccountOutcome.Unsupported,
                            BankBalanceOutcome.Blocked or BankBalanceOutcome.AlreadyRunning => BankBatchAccountOutcome.Unavailable,
                            _ => BankBatchAccountOutcome.Failed
                        };
                        rows[account] = new(id, account, outcome, result.Explanation, result.Outcome, target.Binding.Locator.Currency);
                        if (result.ProtocolStatus == FinTsReadExecutionStatus.RejectedTransportFailure)
                        {
                            backoff.RecordTransientFailure(new(id), "Balance transport failed.", _clock.GetUtcNow());
                            Fill(BankBatchAccountOutcome.Unavailable, "Further reads on this connection were stopped after a transport failure; retry backoff is active."); break;
                        }
                        if (result.Outcome is BankBalanceOutcome.RequiresAuthentication or BankBalanceOutcome.Cancelled or BankBalanceOutcome.ContextChanged)
                        { pin.Dispose(); Fill(outcome, "Further reads stopped after rejected authentication, cancellation, or changed context.", result.Outcome); break; }
                    }
                }
                return Finish(rows.Values.Any(a => a.IsDurableSuccess) ? BankBatchConnectionOutcome.Completed :
                    rows.Values.Any(a => a.Outcome == BankBatchAccountOutcome.Failed) ? BankBatchConnectionOutcome.Failed :
                    rows.Values.Any(a => a.Outcome == BankBatchAccountOutcome.Cancelled) ? BankBatchConnectionOutcome.Cancelled : BankBatchConnectionOutcome.Blocked,
                    "Connection refresh finished. Only durable account saves count as updated or unchanged.");
            }
            catch (OperationCanceledException)
            { Fill(BankBatchAccountOutcome.Cancelled, "Refresh was cancelled, expired, or its bank/profile context changed."); return Finish(BankBatchConnectionOutcome.Cancelled, "Connection refresh was cancelled."); }
            catch (FinTsCandidateException)
            { Fill(BankBatchAccountOutcome.Unsupported, "The explicit bank procedure or request version is unsupported."); return Finish(BankBatchConnectionOutcome.Blocked, "Bank request preparation is unsupported."); }
            catch (Exception)
            {
                var current = _session.CaptureRefreshState();
                bool cancelled = lifetime.IsCancellationRequested || current.Epoch != state.Epoch || current.Snapshot is null;
                Fill(cancelled ? BankBatchAccountOutcome.Cancelled : BankBatchAccountOutcome.Failed,
                    cancelled ? "The bank/profile context changed or refresh was cancelled." : "This bank refresh failed. Existing saved balances remain authoritative.");
                return Finish(cancelled ? BankBatchConnectionOutcome.Cancelled : BankBatchConnectionOutcome.Failed, "Connection refresh could not be completed.");
            }
            finally
            {
                _session.RefreshContextChanged -= Invalidate;
                pin?.Dispose();
                if (workerEntered) _workers.Release();
                if (institutionEntered) institutionGate!.Release();
                lease?.Dispose();
                Report(BankBatchStage.Completed);
            }

            ulong[] Preflight(ulong[] accounts)
            {
                foreach (ulong account in accounts)
                {
                    try
                    {
                        var target = _session.GetBankBalanceContext(account);
                        if (target.Connection.Connection.Id != id) throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException)
                    {
                        var current = _session.CaptureRefreshState().Snapshot;
                        bool active = current?.Accounts.Bindings.Any(b => b.AccountId == account && b.ConnectionId == id && b.Connector == "fints" && b.State == "Active") == true;
                        rows[account] = new(id, account, active ? BankBatchAccountOutcome.Unsupported : BankBatchAccountOutcome.Unavailable,
                            "This account requires verified source identity and a supported balance capability.", Currency: current?.Accounts.Accounts.FirstOrDefault(a => a.Id == account)?.Currency);
                    }
                }
                return accounts;
            }
        }
    }

    private static (string Currency, decimal? Booked, decimal? Available, decimal? Credit) Values(StoreSnapshot snapshot, ulong account)
    {
        decimal? Amount(string kind)
        {
            string? text = snapshot.Balances.Balances.Where(b => b.AccountId == account && b.Kind == kind).OrderByDescending(b => b.RetrievedAt).ThenByDescending(b => b.Id).FirstOrDefault()?.Amount;
            return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
        }
        return (snapshot.Accounts.Accounts.Single(a => a.Id == account).Currency, Amount("Booked"), Amount("Available"), Amount("CreditLine"));
    }
    public void Dispose()
    {
        _disposed = true;
        foreach (var run in _runs.Values) { try { _ = run.CancelAsync(); } catch (ObjectDisposedException) { } }
    }
}
