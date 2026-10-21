using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

public enum BankConnectOutcome { Synchronized, AlreadySynchronized, SetupRequired, ParametersRequired, ParametersAcquired, ApprovalRequired, Busy, Cancelled, Failed, ReceivedNotSaved }
public sealed record BankConnectResult(BankConnectOutcome Outcome, string Message);

/// <summary>Session-owned first-connection entry point. Parameter bootstrap and signed SCA remain explicit dependencies.</summary>
public sealed class BankConnectionSetupCoordinator : IDisposable
{
    private readonly HostSession _session;
    private readonly Func<ConnectionSetupContext, IFinTsTransport>? _transport;
    private readonly Func<ConnectionSetupContext, IFinTsTransport>? _discoveryTransport;
    private readonly Action<Broiler.Fond.Kernel.Storage.StoreSnapshot>? _beforeSave;
    private CancellationTokenSource? _active;
    private int _running;
    private bool _disposed;
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public string ProgressText { get; private set; } = "Online setup: Ready";
    public event EventHandler? Changed;
    internal BankConnectionSetupCoordinator(HostSession session, Func<ConnectionSetupContext, IFinTsTransport>? transport = null,
        Action<Broiler.Fond.Kernel.Storage.StoreSnapshot>? beforeSave = null, Func<ConnectionSetupContext, IFinTsTransport>? discoveryTransport = null)
    { _session = session; _transport = transport; _beforeSave = beforeSave; _discoveryTransport = discoveryTransport; }
    public void Cancel() { try { _ = _active?.CancelAsync(); } catch (ObjectDisposedException) { } }
    public async Task<BankConnectResult> ConnectAsync(ulong connectionId, IBankCredentialPrompt prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (_disposed) return new(BankConnectOutcome.Cancelled, "Online setup has closed.");
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(BankConnectOutcome.Busy, "Online setup is already running. Wait or cancel it before starting again.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        _active = lifetime;
        long epoch = _session.CaptureRefreshState().Epoch;
        void ContextChanged() { if (_session.CaptureRefreshState().Epoch != epoch) Cancel(); }
        _session.RefreshContextChanged += ContextChanged;
        BankConnectResult result = new(BankConnectOutcome.Failed, "Online setup could not complete. Previous saved data was retained.");
        try
        {
            Update("Checking online access setup…");
            lifetime.Token.ThrowIfCancellationRequested();
            var state = _session.CaptureRefreshState();
            if (state.Disposed || state.Snapshot is null || state.SafeMode || state.ReadOnly || !_session.ProductConfiguration.IsConfigured)
                return result = new(BankConnectOutcome.SetupRequired, "Unlock a writable profile outside Safe Mode and configure the assigned application product ID before connecting.");
            var context = _session.GetConnectionSetupContext(connectionId);
            if (context.Institution.VerificationState != "Confirmed" || context.Connection.State == "Disabled")
                return result = new(BankConnectOutcome.SetupRequired, "Confirm the bank endpoint and enable the selected connection before connecting.");
            bool alreadySynchronized = context.Connection.IsSynchronized && context.Connection.State == "Connected" && context.Connection.LastSuccessfulContact.HasValue;
            Update("Checking supported bank parameters…");
            var selected = await _session.BankProcedures.GetBankProcedureAsync(context, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            _ = _session.RequireBatchConnection(context);
            if (alreadySynchronized && selected is not null)
                return result = new(BankConnectOutcome.AlreadySynchronized, "This connection already has synchronized system state. Account discovery and review are required before its first balance refresh; this result does not verify a new sign-in.");
            if (selected is null)
            {
                Update("Downloading public bank parameters…");
                var acquired = await _session.BankParameters.AcquireAsync(context, lifetime.Token);
                if (acquired.Observation is { } observation && prompt is IBankProcedureChoice chooser && _session.UsesSessionBankProcedures &&
                    BankProcedureChoices.Create(observation) is { Count: > 0 } options)
                {
                    Update("Choose a bank-advertised sign-in procedure…");
                    await _session.BankPromptGate.WaitAsync(lifetime.Token);
                    string? choice;
                    void ChoiceContextChanged()
                    {
                        try { _ = _session.RequireBatchConnection(context); }
                        catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
                    }
                    _session.RefreshContextChanged += ChoiceContextChanged;
                    try
                    {
                        ChoiceContextChanged(); lifetime.Token.ThrowIfCancellationRequested();
                        choice = await chooser.ChooseBankProcedureAsync(new(context.Institution.DisplayName, context.Institution.Endpoint, options), lifetime.Token);
                    }
                    finally { _session.RefreshContextChanged -= ChoiceContextChanged; _session.BankPromptGate.Release(); }
                    lifetime.Token.ThrowIfCancellationRequested();
                    _ = _session.RequireBatchConnection(context);
                    if (choice is null) return result = new(BankConnectOutcome.Cancelled, "Procedure selection was cancelled. No banking PIN was requested.");
                    Update("Checking login-specific procedure parameters…");
                    var qualification = await _session.BankParameters.QualifyProcedureAsync(context, choice, lifetime.Token);
                    if (qualification.Outcome != BankProcedureQualificationOutcome.Qualified)
                        return result = new(qualification.Outcome == BankProcedureQualificationOutcome.Cancelled ? BankConnectOutcome.Cancelled :
                            qualification.Outcome == BankProcedureQualificationOutcome.Busy ? BankConnectOutcome.Busy : BankConnectOutcome.ApprovalRequired, qualification.Message);
                    selected = await _session.BankProcedures.GetBankProcedureAsync(_session.GetConnectionSetupContext(connectionId), lifetime.Token);
                    if (selected is null) return result = new(BankConnectOutcome.ParametersRequired, "The selected procedure expired or its setup changed. Choose again before signing in.");
                }
                else return result = new(acquired.Outcome switch
                {
                    BankParameterAcquisitionOutcome.Acquired or BankParameterAcquisitionOutcome.Cached => BankConnectOutcome.ParametersAcquired,
                    BankParameterAcquisitionOutcome.Cancelled => BankConnectOutcome.Cancelled,
                    BankParameterAcquisitionOutcome.Busy => BankConnectOutcome.Busy,
                    BankParameterAcquisitionOutcome.SetupRequired => BankConnectOutcome.SetupRequired,
                    _ => BankConnectOutcome.ParametersRequired
                }, acquired.Message);
            }
            Update("Signing in and synchronizing bank access…");
            var synchronized = await _session.AuthenticateAndSynchronizeBankConnectionCoreAsync(context, prompt,
                selected.Procedures, selected.Selection, _transport is null ? null : () => _transport(context), lifetime.Token,
                beforeSave: _beforeSave, saving: () => Update("Saving synchronized bank access…"), awaitingApproval: () => Update("Awaiting bank approval — enter the TAN…"));
            result = synchronized.Outcome switch
            {
                BankSynchronizationOutcome.Synchronized => new(BankConnectOutcome.Synchronized, "Bank access was synchronized and saved. Discover and review bank accounts next before refreshing balances. No balances were retrieved by this step."),
                BankSynchronizationOutcome.ReceivedNotSaved => new(BankConnectOutcome.ReceivedNotSaved, "Bank synchronization was received and the dialogue closed, but the new system ID could not be saved. Check profile storage access before retrying. Previous saved connection state remains authoritative."),
                BankSynchronizationOutcome.AlreadySynchronized => new(BankConnectOutcome.AlreadySynchronized, "Bank access already has synchronized system state. Discover and review accounts before the first balance refresh."),
                BankSynchronizationOutcome.RequiresAuthentication or BankSynchronizationOutcome.UnsupportedCandidate => new(BankConnectOutcome.ApprovalRequired, "Bank access could not complete with the selected authentication procedure. Review login details and supported text-TAN approval requirements; credentials were not retried."),
                BankSynchronizationOutcome.AlreadyRunning => new(BankConnectOutcome.Busy, "Another bank operation owns this connection. Wait for it to finish or cancel it."),
                BankSynchronizationOutcome.Cancelled or BankSynchronizationOutcome.ContextChanged => new(BankConnectOutcome.Cancelled, "Online setup was cancelled or its profile or connection changed. Start again in the intended profile."),
                BankSynchronizationOutcome.Blocked => new(BankConnectOutcome.SetupRequired, "Review the selected bank login, confirmed endpoint and application product configuration before connecting."),
                _ => result
            };
            return result;
        }
        catch (OperationCanceledException) { return result = new(BankConnectOutcome.Cancelled, "Online setup was cancelled, expired or its context changed."); }
        catch (InvalidOperationException) { return result = new(BankConnectOutcome.SetupRequired, "The selected profile or bank setup is unavailable or changed. Review it and start again."); }
        catch (Exception) { return result; }
        finally
        {
            _session.RefreshContextChanged -= ContextChanged;
            _active = null;
            Volatile.Write(ref _running, 0);
            Update($"Online setup: {result.Outcome}");
        }
    }
    public async Task<BankDiscoveryResult> DiscoverAsync(ulong connectionId, IBankCredentialPrompt prompt,
        IBankAccountDiscoveryReview reviewer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt); ArgumentNullException.ThrowIfNull(reviewer);
        if (_disposed) return new(BankDiscoveryOutcome.Cancelled, "Online setup has closed.");
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return new(BankDiscoveryOutcome.Blocked, "Online setup is already running. Wait or cancel it before discovering accounts.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        _active = lifetime;
        long epoch = _session.CaptureRefreshState().Epoch;
        void ContextChanged() { if (_session.CaptureRefreshState().Epoch != epoch) Cancel(); }
        _session.RefreshContextChanged += ContextChanged;
        BankDiscoveryResult result = new(BankDiscoveryOutcome.Failed, "Account discovery could not complete. Review bank setup before retrying.");
        try
        {
            Update("Checking account discovery setup…");
            lifetime.Token.ThrowIfCancellationRequested();
            var state = _session.CaptureRefreshState();
            if (state.Disposed || state.Snapshot is null || state.SafeMode || state.ReadOnly || !_session.ProductConfiguration.IsConfigured)
                return result = new(BankDiscoveryOutcome.Blocked, "Unlock a writable profile outside Safe Mode and configure the application product ID first.");
            var context = _session.GetConnectionSetupContext(connectionId);
            if (context.Institution.VerificationState != "Confirmed" || !context.Connection.IsSynchronized || context.Connection.State != "Connected")
                return result = new(BankDiscoveryOutcome.Blocked, "Use Connect to confirm and synchronize bank access before discovering accounts.");
            var selected = await _session.BankProcedures.GetBankProcedureAsync(context, lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            _ = _session.RequireBatchConnection(context);
            if (selected is null)
                return result = new(BankDiscoveryOutcome.Blocked, "Use Connect to choose and qualify a bank procedure again, then discover accounts.");
            Update("Discovering bank accounts — review is required before saving…");
            result = await _session.DiscoverAndReviewBankAccountsCoreAsync(context, prompt, reviewer,
                selected.Procedures, selected.Selection, _discoveryTransport is null ? null : () => _discoveryTransport(context), lifetime.Token,
                reviewing: () => Update("Review bank accounts — confirm only the intended associations…"),
                saving: () => Update("Saving reviewed bank accounts and qualified capabilities…"));
            if (result.Outcome == BankDiscoveryOutcome.Saved)
                result = result with { Explanation = result.Explanation + " No balances were downloaded. Refresh balances next for eligible reviewed accounts." };
            return result;
        }
        catch (OperationCanceledException) { return result = new(BankDiscoveryOutcome.Cancelled, "Discovery was cancelled, expired or its context changed."); }
        catch (InvalidOperationException) { return result = new(BankDiscoveryOutcome.ContextChanged, "Bank setup changed or is unavailable. Use Connect, then discover accounts again."); }
        catch (Exception) { return result; }
        finally
        {
            _session.RefreshContextChanged -= ContextChanged;
            _active = null;
            Volatile.Write(ref _running, 0);
            Update($"Account discovery: {result.Outcome}");
        }
    }
    private void Update(string text)
    {
        ProgressText = text;
        if (_disposed) return;
        foreach (EventHandler handler in Changed?.GetInvocationList() ?? [])
        { try { handler(this, EventArgs.Empty); } catch (Exception) { } }
    }
    public void Dispose() { _disposed = true; Cancel(); }
}
