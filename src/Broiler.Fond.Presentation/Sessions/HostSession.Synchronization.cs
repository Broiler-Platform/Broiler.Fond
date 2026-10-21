using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    private readonly ConcurrentDictionary<(long Epoch, ulong Connection), byte> _bankReservations = new();
    private readonly SemaphoreSlim _bankOperationSlots = new(2);
    internal bool TryReserveBankOperation(long epoch, ulong id) => _bankReservations.TryAdd((epoch, id), 0);
    internal void ReleaseBankOperation(long epoch, ulong id) => _bankReservations.TryRemove((epoch, id), out _);
    internal Task WaitForBankOperationSlotAsync(CancellationToken cancellationToken) => _bankOperationSlots.WaitAsync(cancellationToken);
    internal void ReleaseBankOperationSlot() => _bankOperationSlots.Release();

    /// <summary>
    /// BR-03 entry point for a caller-owned ephemeral PIN and bank-supplied parameter provenance.
    /// Does not collect credentials, select a procedure, discover/activate accounts, or retrieve balances.
    /// BR-04 will compose interactive credential/SCA acquisition around this boundary.
    /// </summary>
    public Task<BankSynchronizationResult> SynchronizeBankConnectionAsync(ConnectionSetupContext context,
        FinTsSessionCredential pin, FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection,
        CancellationToken cancellationToken = default) => SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, null, cancellationToken);

    internal async Task<BankSynchronizationResult> SynchronizeBankConnectionCoreAsync(ConnectionSetupContext context,
        FinTsSessionCredential pin, FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection,
        Func<IFinTsTransport>? transportFactory, CancellationToken cancellationToken = default,
        BankConnectionBatchLease? lease = null, Action<StoreSnapshot>? beforeSave = null, Action? saving = null,
        Func<FinTsTanChallenge, CancellationToken, ValueTask<FinTsSessionCredential?>>? textTanPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(pin);
        BankConnection connection;
        FinTsProductionCandidateFactory candidates;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                _ = RequireCurrentSetup(context);
                if (!ProductConfiguration.IsConfigured || context.Institution.VerificationState != "Confirmed")
                    return new(BankSynchronizationOutcome.Blocked, "Confirm the bank endpoint and configure the assigned application product ID before sign-in.");
                connection = BankConnectionSetup.ToDomain(context.Connection, context.Institution);
                if (connection.State == BankConnectionState.Disabled)
                    return new(BankSynchronizationOutcome.Blocked, "The bank connection is disabled.");
                candidates = new(connection, ProductConfiguration.Registration!, procedures, selection);
                if (connection.IsSynchronized && connection.State == BankConnectionState.Connected && connection.LastSuccessfulContact.HasValue)
                    return new(BankSynchronizationOutcome.AlreadySynchronized, "The current confirmed bank context already has synchronized system state.");
                if (lease is not null && !lease.Matches(this, context.SessionEpoch, context.Connection.Id))
                    return new(BankSynchronizationOutcome.Blocked, "The synchronization operation no longer owns this bank context.");
                if (lease is null && !TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                    return new(BankSynchronizationOutcome.AlreadyRunning, "A bank operation is already active for this connection.");
            }
        }
        catch (OperationCanceledException) { return new(BankSynchronizationOutcome.Cancelled, "Synchronization was cancelled before dispatch."); }
        catch (Exception ex) when (ex is FinTsCandidateException or FinTsFormatException)
        { return new(BankSynchronizationOutcome.UnsupportedCandidate, "The bank parameters, selected procedure, or request version cannot prepare this synchronization. No request was sent."); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return new(BankSynchronizationOutcome.Blocked, "Bank configuration changed or requires review. No request was sent."); }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool committed = false;
        bool received = false;
        bool entered = false;
        bool institutionEntered = false;
        var institutionGate = BankInstitutionGate(context.Institution.Id);
        void Invalidate()
        {
            lock (_syncRoot)
            {
                try { _ = RequireCurrentSetup(context); }
                catch (InvalidOperationException)
                {
                    try { _ = cancellation.CancelAsync(); } catch (ObjectDisposedException) { }
                }
            }
        }
        RefreshContextChanged += Invalidate;
        try
        {
            Invalidate();
            cancellation.Token.ThrowIfCancellationRequested();
            await institutionGate.WaitAsync(cancellation.Token);
            institutionEntered = true;
            await WaitForBankOperationSlotAsync(cancellation.Token);
            entered = true;
            Invalidate();
            cancellation.Token.ThrowIfCancellationRequested();
            using IFinTsTransport transport = transportFactory?.Invoke() ?? CreateBankTransport(context);
            var result = await FinTsSynchronizationExecutionPipeline.ExecuteAsync(connection, transport, pin,
                candidates.SynchronizationOptions(textTanPrompt: textTanPrompt), cancellation.Token);
            Invalidate();
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
                return new(result.Status switch
                {
                    FinTsSynchronizationExecutionStatus.Cancelled => BankSynchronizationOutcome.Cancelled,
                    FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome or
                        FinTsSynchronizationExecutionStatus.RejectedInvalidCredential => BankSynchronizationOutcome.RequiresAuthentication,
                    FinTsSynchronizationExecutionStatus.RejectedMissingCandidate => BankSynchronizationOutcome.UnsupportedCandidate,
                    _ => BankSynchronizationOutcome.Failed
                },
                    "Bank synchronization did not complete. Interactive approval or protocol support may be required; no synchronized state was saved.", result.Status);
            received = true;
            saving?.Invoke();
            lock (_syncRoot)
            {
                var snapshot = RequireCurrentSetup(context);
                cancellation.Token.ThrowIfCancellationRequested();
                // This commit affects connection state only. Balance ingestion remains BR-06.
                _profileStore!.MutateAndSave(latest =>
                {
                    _ = RequireCurrentSetup(context);
                    cancellation.Token.ThrowIfCancellationRequested();
                    var updated = latest with
                    {
                        Connections = latest.Connections with
                        {
                            Connections = latest.Connections.Connections.Select(c => c.Id == context.Connection.Id
                                ? MultibankSnapshotMutator.ToStoreEntry(result.UpdatedConnection) : c).ToArray()
                        }
                    };
                    beforeSave?.Invoke(updated);
                    return updated;
                });
                committed = true;
                ReloadDataInternal();
            }
            return new(BankSynchronizationOutcome.Synchronized, "Bank synchronization was validated, the dialogue closed, and system state was saved.", result.Status);
        }
        catch (OperationCanceledException)
        {
            if (committed) return new(BankSynchronizationOutcome.Synchronized, "Bank system state was saved. Reopen the profile if its display could not be refreshed.");
            return new(cancellationToken.IsCancellationRequested ? BankSynchronizationOutcome.Cancelled : BankSynchronizationOutcome.ContextChanged,
                "Synchronization was cancelled or its profile/connection context changed. No synchronized state was saved.");
        }
        catch (Exception)
        {
            bool current;
            lock (_syncRoot)
            {
                try { _ = RequireCurrentSetup(context); current = true; }
                catch (InvalidOperationException) { current = false; }
            }
            return new(committed ? BankSynchronizationOutcome.Synchronized : !current ? BankSynchronizationOutcome.ContextChanged : received ? BankSynchronizationOutcome.ReceivedNotSaved : BankSynchronizationOutcome.Failed,
                committed ? "Bank system state was saved, but the display could not be refreshed. Reopen the profile to reload it." : received
                    ? "Bank synchronization was received and the dialogue closed, but system state was not saved. Check profile storage access before retrying; previous saved state was retained."
                    : "Synchronization could not be completed. Check bank connectivity and setup; previous saved state was retained.");
        }
        finally
        {
            RefreshContextChanged -= Invalidate;
            if (entered) ReleaseBankOperationSlot();
            if (institutionEntered) institutionGate.Release();
            if (lease is null) ReleaseBankOperation(context.SessionEpoch, context.Connection.Id);
            if (committed) { try { OnStateChanged(); } catch (Exception) { /* Observer failure cannot undo a durable save. */ } }
        }
    }
}
