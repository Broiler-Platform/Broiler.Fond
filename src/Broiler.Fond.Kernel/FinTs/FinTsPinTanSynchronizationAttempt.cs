namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanSynchronizationAttemptState
{
    Ready, AwaitingResponse, CloseAndReinitializeRequired, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsPinTanSynchronizationAttemptTransition
{
    CandidateRecorded, CloseAndReinitializeRequired, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

public sealed class FinTsPinTanSynchronizationAttemptResult
{
    internal FinTsPinTanSynchronizationAttemptResult(FinTsPinTanSynchronizationAttemptTransition transition, FinTsPinTanSynchronizationEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsPinTanSynchronizationAttemptTransition Transition { get; }
    /// <summary>Caller-owned evidence returned once for a scoped response, including review.
    /// A matching handoff always requires closing and reinitialization; it never acknowledges closure.</summary>
    public FinTsPinTanSynchronizationEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, credential or identity references.</summary>
public sealed class FinTsPinTanSynchronizationSnapshot
{
    internal FinTsPinTanSynchronizationSnapshot(FinTsPinTanSynchronizationAttemptState state, int candidates, int handled, TimeSpan remaining,
        FinTsPinTanSynchronizationIssue issues, FinTsPinTanResponseBindingIssue bindingIssues, FinTsSynchronizationIssue synchronizationIssues)
    { State = state; CandidatesRecorded = candidates; ResponsesHandled = handled; RemainingTime = remaining; Issues = issues; BindingIssues = bindingIssues; SynchronizationIssues = synchronizationIssues; }
    public FinTsPinTanSynchronizationAttemptState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanSynchronizationIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsSynchronizationIssue SynchronizationIssues { get; }
}

/// <summary>One local assembled synchronization candidate and one response handoff. No encoding, sending or authentication.
/// The attempt takes no credentials/output buffers; disposal releases pending metadata, not caller-owned objects.</summary>
public sealed class FinTsPinTanSynchronizationAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsPinTanSynchronizationAttemptState _state;
    private FinTsPinTanRequestBinding? _pending;
    private FinTsSynchronizationRecoveryContext? _recovery;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanSynchronizationIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsSynchronizationIssue _synchronizationIssues;

    public FinTsPinTanSynchronizationAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System; _lifetime = lifetime;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
    }

    public FinTsPinTanSynchronizationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _candidates, _handled, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _bindingIssues, _synchronizationIssues);
        }
    }

    /// <summary>Records metadata only; it does not prove encoding or transmission. The absolute deadline begins here.</summary>
    public FinTsPinTanSynchronizationAttemptTransition Start(FinTsPinTanRequestBinding candidate,
        FinTsSynchronizationRecoveryContext? recoveryContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanSynchronizationAttemptState.Ready) { return IsActive ? FinTsPinTanSynchronizationAttemptTransition.WrongState : FinTsPinTanSynchronizationAttemptTransition.Terminal; }
            if (candidate.SignatureEvidence.Request.Synchronization is null)
            { _issues = FinTsPinTanSynchronizationIssue.RequestKindMismatch; Close(FinTsPinTanSynchronizationAttemptState.NeedsReview); return FinTsPinTanSynchronizationAttemptTransition.RejectedForReview; }
            var profile = candidate.ProfileVersion == 1 ? FinTsSynchronizationProfile.PinTan1 : FinTsSynchronizationProfile.PinTan2;
            _synchronizationIssues = FinTsSynchronizationEvidence.RequestIssues(candidate.SignatureEvidence.Request.Synchronization, profile, recoveryContext);
            if (_synchronizationIssues != FinTsSynchronizationIssue.None)
            { _issues = FinTsPinTanSynchronizationIssue.SynchronizationNeedsReview; Close(FinTsPinTanSynchronizationAttemptState.NeedsReview); return FinTsPinTanSynchronizationAttemptTransition.RejectedForReview; }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception) { if (_state == FinTsPinTanSynchronizationAttemptState.Ready) { Close(FinTsPinTanSynchronizationAttemptState.ClockInvalid); } return FinTsPinTanSynchronizationAttemptTransition.Terminal; }
            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanSynchronizationAttemptState.Ready) { return FinTsPinTanSynchronizationAttemptTransition.Terminal; }
            _pending = candidate; _recovery = recoveryContext; _candidates = 1; _state = FinTsPinTanSynchronizationAttemptState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanSynchronizationAttemptTransition.CandidateRecorded : FinTsPinTanSynchronizationAttemptTransition.Terminal;
        }
    }

    public FinTsPinTanSynchronizationAttemptResult AcceptResponse(FinTsSynchronizationDataSet data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(_state == FinTsPinTanSynchronizationAttemptState.Ready ? FinTsPinTanSynchronizationAttemptTransition.WrongState : FinTsPinTanSynchronizationAttemptTransition.Terminal); }
                var binding = FinTsPinTanResponseBinding.Evaluate(_pending!, data.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationAttemptTransition.Terminal); }
                const FinTsPinTanResponseBindingIssue foreignScope = FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference | FinTsPinTanResponseBindingIssue.UnknownSegmentReference | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;
                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanSynchronizationIssue.BindingNeedsReview; _bindingIssues = binding.Issues; _synchronizationIssues = FinTsSynchronizationIssue.None;
                    return new(FinTsPinTanSynchronizationAttemptTransition.ContextMismatch);
                }
                var evidence = FinTsPinTanSynchronizationEvidence.Evaluate(binding, data, _recovery, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationAttemptTransition.Terminal); }
                _issues = evidence.Issues; _bindingIssues = binding.Issues; _synchronizationIssues = evidence.SynchronizationIssues; _handled = 1;
                Close(evidence.HasMatchingEvidence ? FinTsPinTanSynchronizationAttemptState.CloseAndReinitializeRequired : FinTsPinTanSynchronizationAttemptState.NeedsReview);
                return new(evidence.HasMatchingEvidence ? FinTsPinTanSynchronizationAttemptTransition.CloseAndReinitializeRequired : FinTsPinTanSynchronizationAttemptTransition.RejectedForReview, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanSynchronizationAttemptState.Ready) { Close(FinTsPinTanSynchronizationAttemptState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanSynchronizationAttemptState.Ready) { Close(FinTsPinTanSynchronizationAttemptState.Cancelled); } } }
    /// <summary>Abandon after parsing/transport failure or disconnect. No automatic retry or credential ownership.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanSynchronizationAttemptState.Ready) { Close(FinTsPinTanSynchronizationAttemptState.Stopped); } } }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
    private bool IsActive => _state == FinTsPinTanSynchronizationAttemptState.AwaitingResponse;
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanSynchronizationAttemptState.Ready) { Close(FinTsPinTanSynchronizationAttemptState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsPinTanSynchronizationAttemptState.ClockInvalid); return; }
            _lastTimestamp = now; _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanSynchronizationAttemptState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanSynchronizationAttemptState.TimedOut); }
        }
        catch (Exception) { if (IsActive) { Close(FinTsPinTanSynchronizationAttemptState.ClockInvalid); } }
    }
    private void Close(FinTsPinTanSynchronizationAttemptState state) { _state = state; _pending = null; _recovery = null; }
}
