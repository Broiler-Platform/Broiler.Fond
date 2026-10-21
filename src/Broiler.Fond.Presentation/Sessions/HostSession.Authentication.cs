using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    internal SemaphoreSlim BankPromptGate { get; } = new(1);

    /// <summary>Collects a separate banking PIN immediately before synchronization.
    /// Owns it through completion; never reads the profile passphrase or retries authentication.</summary>
    public Task<BankSynchronizationResult> AuthenticateAndSynchronizeBankConnectionAsync(
        ConnectionSetupContext context, IBankCredentialPrompt prompt, FinTsPinTanProcedureContext procedures,
        FinTsPinTanSignatureSelection selection, CancellationToken cancellationToken = default) =>
        AuthenticateAndSynchronizeBankConnectionCoreAsync(context, prompt, procedures, selection, null, cancellationToken);

    internal async Task<BankSynchronizationResult> AuthenticateAndSynchronizeBankConnectionCoreAsync(
        ConnectionSetupContext context, IBankCredentialPrompt prompt, FinTsPinTanProcedureContext procedures,
        FinTsPinTanSignatureSelection selection,
        Func<Broiler.Fond.Kernel.FinTs.Transport.IFinTsTransport>? transportFactory, CancellationToken cancellationToken = default,
        Action<Broiler.Fond.Kernel.Storage.StoreSnapshot>? beforeSave = null, Action? saving = null, Action? awaitingApproval = null)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(5));
        FinTsSessionCredential? pin = null;
        using var cancelCredential = lifetime.Token.Register(() => { lock (_syncRoot) { pin?.Dispose(); } });
        void Invalidate()
        {
            lock (_syncRoot)
            {
                try { _ = RequireCurrentSetup(context); }
                catch (InvalidOperationException)
                {
                    pin?.Dispose();
                    try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { }
                }
            }
        }
        RefreshContextChanged += Invalidate;
        bool entered = false;
        BankConnectionBatchLease? lease = null;
        var promptContext = SynchronizationContext.Current;
        async Task<FinTsSessionCredential?> PromptTanAsync(FinTsTanChallenge challenge, CancellationToken ct)
        {
            async Task<FinTsSessionCredential?> OnUi()
            {
                await BankPromptGate.WaitAsync(ct);
                try
                {
                    Invalidate(); ct.ThrowIfCancellationRequested(); awaitingApproval?.Invoke();
                    string text = System.Text.Encoding.Latin1.GetString(challenge.Text!.CopyValueBytes());
                    text = new string(text.Where(c => !char.IsControl(c)).Take(240).ToArray());
                    var matched = selection.ProfileVersion == 2
                        ? procedures.Parameters.Advertisements.SingleOrDefault(a => a.Source.Version == selection.TanSegmentVersion)?.Procedures.SingleOrDefault(p => p.SecurityFunction == selection.SecurityFunction)
                        : null;
                    string procedureName = matched is null
                        ? "Selected bank procedure"
                        : $"{new string(System.Text.Encoding.Latin1.GetString(matched.Name.CopyValueBytes()).Where(c => !char.IsControl(c)).Take(80).ToArray())} ({matched.SecurityFunction})";
                    return await prompt.AcquireBankCredentialAsync(new("Approve Bank Sign-in", text + "\nEnter the TAN for this bank sign-in. Do not enter the banking PIN or profile passphrase.",
                        context.Institution.DisplayName, IsTan: true, TanProcedureName: procedureName), ct);
                }
                finally { BankPromptGate.Release(); }
            }
            if (promptContext is null || SynchronizationContext.Current == promptContext) return await OnUi();
            var completion = new TaskCompletionSource<FinTsSessionCredential?>(TaskCreationOptions.RunContinuationsAsynchronously);
            promptContext.Post(async _ =>
            {
                try { completion.TrySetResult(await OnUi()); }
                catch (OperationCanceledException) { completion.TrySetCanceled(ct); }
                catch (Exception) { completion.TrySetException(new InvalidOperationException("Bank approval interaction failed.")); }
            }, null);
            return await completion.Task;
        }
        try
        {
            Invalidate();
            lifetime.Token.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                _ = RequireCurrentSetup(context);
                if (!ProductConfiguration.IsConfigured || context.Institution.VerificationState != "Confirmed")
                    return new(BankSynchronizationOutcome.Blocked, "Confirm the bank endpoint and configure the application product ID before entering a banking PIN.");
                var connection = BankConnectionSetup.ToDomain(context.Connection, context.Institution);
                if (connection.State == Broiler.Fond.Kernel.Domain.Connections.BankConnectionState.Disabled)
                    return new(BankSynchronizationOutcome.Blocked, "The bank connection is disabled.");
                _ = new FinTsProductionCandidateFactory(connection, ProductConfiguration.Registration!, procedures, selection);
                if (connection.IsSynchronized && connection.State == Broiler.Fond.Kernel.Domain.Connections.BankConnectionState.Connected && connection.LastSuccessfulContact.HasValue)
                    return new(BankSynchronizationOutcome.AlreadySynchronized, "The confirmed connection already has synchronized system state.");
                if (!TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                    return new(BankSynchronizationOutcome.AlreadyRunning, "Another operation owns this bank connection. No banking PIN was requested.");
                lease = new(this, context.SessionEpoch, context.Connection.Id);
            }
            await BankPromptGate.WaitAsync(lifetime.Token);
            entered = true;
            Invalidate();
            lifetime.Token.ThrowIfCancellationRequested();
            var acquired = await prompt.AcquireBankCredentialAsync(new("Banking PIN", "Enter your banking PIN, not the profile passphrase.", context.Institution.DisplayName), lifetime.Token);
            lock (_syncRoot) { pin = acquired; }
            BankPromptGate.Release(); entered = false;
            Invalidate();
            lifetime.Token.ThrowIfCancellationRequested();
            if (pin is null) return new(BankSynchronizationOutcome.Cancelled, "Bank sign-in was cancelled.");
            var state = pin.GetSnapshot();
            if (state.Kind != FinTsCredentialKind.Pin || state.State != FinTsCredentialState.Available)
                return new(BankSynchronizationOutcome.RequiresAuthentication, "An available banking PIN is required. Start sign-in again.");
            // Network scheduling must not hold the prompt gate; later interactive steps acquire it independently.
            return await SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, transportFactory, lifetime.Token, lease, beforeSave, saving,
                (challenge, ct) => new ValueTask<FinTsSessionCredential?>(PromptTanAsync(challenge, ct)));
        }
        catch (OperationCanceledException)
        { return new(BankSynchronizationOutcome.Cancelled, "Bank sign-in was cancelled, expired, or its connection context changed."); }
        catch (FinTsCandidateException)
        { return new(BankSynchronizationOutcome.UnsupportedCandidate, "The selected bank authentication procedure is unsupported. No sign-in was started."); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return new(BankSynchronizationOutcome.Blocked, "Bank sign-in could not be prepared. Review the connection configuration."); }
        catch (Exception)
        { return new(BankSynchronizationOutcome.Failed, "Bank sign-in failed. Start sign-in again; credentials were not retained."); }
        finally
        {
            RefreshContextChanged -= Invalidate;
            pin?.Dispose();
            lease?.Dispose();
            if (entered) BankPromptGate.Release();
        }
    }
}
