using System.Collections.Concurrent;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

public enum BankParameterAcquisitionOutcome { Acquired, Cached, SetupRequired, UnsupportedResponse, CloseFailed, TransportFailed, Busy, Cancelled }
public sealed record BankParameterAcquisitionResult(BankParameterAcquisitionOutcome Outcome, string Message, FinTsBankParameterObservation? Observation = null);
public enum BankProcedureQualificationOutcome { Qualified, SetupRequired, Unsupported, CloseFailed, Failed, Busy, Cancelled }
public sealed record BankProcedureQualificationResult(BankProcedureQualificationOutcome Outcome, string Message);

/// <summary>Short-lived public bank observations scoped to the confirmed profile/endpoint/login context. No credentials or selected procedures.</summary>
public sealed class BankParameterAcquisitionService : IDisposable
{
    private sealed record Entry(ConnectionSetupContext Context, FinTsBankParameterObservation Observation, long Started);
    private readonly HostSession _session;
    private readonly Func<ConnectionSetupContext, IFinTsTransport>? _transport;
    private readonly ConcurrentDictionary<ulong, Entry> _entries = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly CancellationToken _shutdownToken;
    private bool _disposed;
    internal BankParameterAcquisitionService(HostSession session, Func<ConnectionSetupContext, IFinTsTransport>? transport)
    { _session = session; _transport = transport; _shutdownToken = _shutdown.Token; session.RefreshContextChanged += Invalidate; }
    public FinTsBankParameterObservation? GetCurrent(ConnectionSetupContext context)
    {
        Invalidate();
        if (_disposed) return null;
        try { _ = _session.RequireBatchConnection(context, allowContactChanges: true); }
        catch (InvalidOperationException) { return null; }
        return _entries.TryGetValue(context.Connection.Id, out var entry) && entry.Context.SessionEpoch == context.SessionEpoch &&
            entry.Context.ProfileId == context.ProfileId && entry.Context.BranchId == context.BranchId ? entry.Observation : null;
    }
    private void Invalidate()
    {
        foreach (var entry in _entries)
        {
            try
            {
                _ = _session.RequireBatchConnection(entry.Value.Context, allowContactChanges: true);
                if (System.Diagnostics.Stopwatch.GetElapsedTime(entry.Value.Started) < TimeSpan.FromMinutes(10)) continue;
            }
            catch (InvalidOperationException) { }
            _entries.TryRemove(entry.Key, out _);
        }
    }
    public async Task<BankParameterAcquisitionResult> AcquireAsync(ConnectionSetupContext context, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        void Changed()
        {
            try { _ = _session.RequireBatchConnection(context, allowContactChanges: true); }
            catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
        }
        _session.RefreshContextChanged += Changed;
        bool reserved = false, institutionEntered = false, workerEntered = false;
        var institution = _session.BankInstitutionGate(context.Institution.Id);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            _ = _session.RequireBatchConnection(context);
            if (_disposed || !_session.ProductConfiguration.IsConfigured || context.Institution.VerificationState != "Confirmed" || context.Connection.State == "Disabled")
                return new(BankParameterAcquisitionOutcome.SetupRequired, "Confirm the bank endpoint and configure the assigned product ID before downloading parameters.");
            if (GetCurrent(context) is { } cached)
                return new(BankParameterAcquisitionOutcome.Cached, "Current public bank parameters are available. No banking PIN was used.", cached);
            if (!_session.TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                return new(BankParameterAcquisitionOutcome.Busy, "Another operation owns this bank connection. Wait or cancel it before trying again.");
            reserved = true;
            await institution.WaitAsync(lifetime.Token); institutionEntered = true;
            await _session.WaitForBankOperationSlotAsync(lifetime.Token); workerEntered = true;
            Changed(); lifetime.Token.ThrowIfCancellationRequested();
            var registration = _session.ProductConfiguration.Registration!;
            using var transport = _transport?.Invoke(context) ?? _session.CreateBankTransport(context);
            var result = await FinTsBankParameterBootstrap.ExecuteAsync(transport, new Uri(context.Institution.Endpoint), "280",
                context.Institution.BankCode!, registration.ProductIdentifier, registration.ProductVersion, lifetime.Token);
            Changed(); lifetime.Token.ThrowIfCancellationRequested();
            if (result is { Status: FinTsBankBootstrapStatus.Acquired, Observation: { } observation })
            {
                _entries[context.Connection.Id] = new(context, observation, System.Diagnostics.Stopwatch.GetTimestamp());
                return new(BankParameterAcquisitionOutcome.Acquired, "Public bank parameters were downloaded and the anonymous dialogue closed. Procedure selection and login-specific qualification are still required; no PIN was sent.", observation);
            }
            return result.Status switch
            {
                FinTsBankBootstrapStatus.Cancelled => new(BankParameterAcquisitionOutcome.Cancelled, "Bank parameter download was cancelled or expired."),
                FinTsBankBootstrapStatus.TransportFailed => new(BankParameterAcquisitionOutcome.TransportFailed, "The bank parameter exchange failed. Check the confirmed endpoint and connectivity before retrying."),
                FinTsBankBootstrapStatus.CloseFailed => new(BankParameterAcquisitionOutcome.CloseFailed, "Bank parameters were received but the closing exchange failed. They were not retained; retry when connectivity is restored."),
                _ => new(BankParameterAcquisitionOutcome.UnsupportedResponse, "The bank parameter response could not be validated for this endpoint and bank. No sign-in was started. This build supports a bounded anonymous FinTS 3.0 parameter exchange; other layouts or login-required downloads need further support.")
            };
        }
        catch (OperationCanceledException) { return new(BankParameterAcquisitionOutcome.Cancelled, "Parameter download was cancelled or the bank/profile context changed."); }
        catch (InvalidOperationException) { return new(BankParameterAcquisitionOutcome.SetupRequired, "The profile or bank setup changed or is unavailable. Review it before downloading parameters."); }
        catch (Exception) { return new(BankParameterAcquisitionOutcome.TransportFailed, "Bank parameters could not be downloaded. Review endpoint and connectivity; no sign-in was completed."); }
        finally
        {
            _session.RefreshContextChanged -= Changed;
            if (workerEntered) _session.ReleaseBankOperationSlot();
            if (institutionEntered) institution.Release();
            if (reserved) _session.ReleaseBankOperation(context.SessionEpoch, context.Connection.Id);
        }
    }
    public async Task<BankProcedureQualificationResult> QualifyProcedureAsync(ConnectionSetupContext context, string optionId, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        bool reserved = false, institutionEntered = false, workerEntered = false;
        var institution = _session.BankInstitutionGate(context.Institution.Id);
        void Changed()
        {
            try { _ = _session.RequireBatchConnection(context, allowContactChanges: true); }
            catch (InvalidOperationException) { try { _ = lifetime.CancelAsync(); } catch (ObjectDisposedException) { } }
        }
        _session.RefreshContextChanged += Changed;
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var observation = GetCurrent(context);
            if (observation is null || !_session.UsesSessionBankProcedures)
                return new(BankProcedureQualificationOutcome.SetupRequired, "Download current bank parameters first. A host-owned procedure provider must manage its own selection.");
            var option = BankProcedureChoices.Create(observation).SingleOrDefault(o => o.Id == optionId && o.CanSelect);
            if (option is null) return new(BankProcedureQualificationOutcome.Unsupported, "The selected procedure is not a supported current bank advertisement. No login request was sent.");
            if (!_session.TryReserveBankOperation(context.SessionEpoch, context.Connection.Id))
                return new(BankProcedureQualificationOutcome.Busy, "Another bank operation owns this connection. Wait or cancel it before checking the procedure.");
            reserved = true;
            await institution.WaitAsync(lifetime.Token); institutionEntered = true;
            await _session.WaitForBankOperationSlotAsync(lifetime.Token); workerEntered = true;
            Changed(); lifetime.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(GetCurrent(context), observation))
                return new(BankProcedureQualificationOutcome.SetupRequired, "Bank parameters expired or changed. Download them and choose again.");
            using var transport = _transport?.Invoke(context) ?? _session.CreateBankTransport(context);
            var selection = new FinTsPinTanSignatureSelection(option.ProfileVersion, option.SecurityFunction, option.TanSegmentVersion);
            var result = await FinTsProcedureQualification.ExecuteAsync(transport, BankConnectionSetup.ToDomain(context.Connection, context.Institution),
                _session.ProductConfiguration.Registration!, selection, lifetime.Token);
            Changed(); lifetime.Token.ThrowIfCancellationRequested();
            if (result is { Status: FinTsProcedureQualificationStatus.Qualified, Procedures: { } procedures })
            {
                if (!BankProcedureChoices.Matches(observation, procedures, option))
                {
                    _entries.TryRemove(context.Connection.Id, out _);
                    return new(BankProcedureQualificationOutcome.SetupRequired, "The bank changed this procedure's definition. Download current parameters and choose again; no PIN was requested.");
                }
                _session.SelectBankProcedure(context, new(procedures, selection));
                return new(BankProcedureQualificationOutcome.Qualified, "The explicitly selected procedure has matching login-specific parameter evidence. No PIN was sent; authentication and synchronization are next.");
            }
            return result.Status switch
            {
                FinTsProcedureQualificationStatus.Cancelled => new(BankProcedureQualificationOutcome.Cancelled, "Procedure qualification was cancelled or expired."),
                FinTsProcedureQualificationStatus.CloseFailed => new(BankProcedureQualificationOutcome.CloseFailed, "Procedure parameters were received but the closing exchange failed. The selection was not retained."),
                FinTsProcedureQualificationStatus.TransportFailed => new(BankProcedureQualificationOutcome.Failed, "The login-specific parameter exchange failed. Review connectivity and the confirmed endpoint before retrying."),
                _ => new(BankProcedureQualificationOutcome.Unsupported, "The response did not qualify this procedure for the configured bank login. No PIN was sent. Login-required parameter downloads, unsupported layouts or approval requirements need further support.")
            };
        }
        catch (OperationCanceledException) { return new(BankProcedureQualificationOutcome.Cancelled, "Procedure qualification was cancelled or the profile/bank setup changed."); }
        catch (Exception) { return new(BankProcedureQualificationOutcome.Failed, "The bank procedure could not be qualified. Review current bank setup; no selection was saved."); }
        finally
        {
            _session.RefreshContextChanged -= Changed;
            if (workerEntered) _session.ReleaseBankOperationSlot();
            if (institutionEntered) institution.Release();
            if (reserved) _session.ReleaseBankOperation(context.SessionEpoch, context.Connection.Id);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel(); _shutdown.Dispose();
        _session.RefreshContextChanged -= Invalidate; _entries.Clear();
    }
}
