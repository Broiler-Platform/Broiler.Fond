namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanReadContinuationAttemptState
{
    Ready, AwaitingResponse, ExecutionReported, PartialReported, UnavailableReported, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsPinTanReadContinuationAttemptTransition
{
    CandidateRecorded, ExecutionReported, PartialReported, UnavailableReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

public sealed class FinTsPinTanReadContinuationAttemptResult
{
    internal FinTsPinTanReadContinuationAttemptResult(FinTsPinTanReadContinuationAttemptTransition transition, FinTsPinTanReadContinuationEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsPinTanReadContinuationAttemptTransition Transition { get; }
    /// <summary>Caller-owned second-page evidence returned once for a scoped response, including review or partial observations.
    /// A partial handoff terminates this continuation attempt and authorizes no further page.</summary>
    public FinTsPinTanReadContinuationEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, account, token or credential references.</summary>
public sealed class FinTsPinTanReadContinuationSnapshot
{
    internal FinTsPinTanReadContinuationSnapshot(FinTsPinTanReadContinuationAttemptState state, int candidates, int handled, TimeSpan remaining,
        FinTsPinTanReadIssue issues, FinTsPinTanResponseBindingIssue bindingIssues, FinTsReadContextIssue readIssues)
    { State = state; CandidatesRecorded = candidates; ResponsesHandled = handled; RemainingTime = remaining; Issues = issues; BindingIssues = bindingIssues; ReadIssues = readIssues; }
    public FinTsPinTanReadContinuationAttemptState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanReadIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsReadContextIssue ReadIssues { get; }
}

/// <summary>One local continuation candidate, one absolute deadline and one scoped response handoff. No encoding,
/// sending, authentication, SCA completion or further continuation. Disposal releases pending metadata, not caller-owned evidence.</summary>
public sealed class FinTsPinTanReadContinuationAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsPinTanReadContinuationAttemptState _state;
    private FinTsPinTanReadContinuationRequestBinding? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanReadIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsReadContextIssue _readIssues;

    public FinTsPinTanReadContinuationAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System; _lifetime = lifetime;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
    }

    public FinTsPinTanReadContinuationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _candidates, _handled, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _bindingIssues, _readIssues);
        }
    }

    /// <summary>Records metadata only, without proof of encoding or transmission. The absolute deadline starts here.</summary>
    public FinTsPinTanReadContinuationAttemptTransition Start(FinTsPinTanReadContinuationRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadContinuationAttemptState.Ready) { return IsActive ? FinTsPinTanReadContinuationAttemptTransition.WrongState : FinTsPinTanReadContinuationAttemptTransition.Terminal; }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception) { if (_state == FinTsPinTanReadContinuationAttemptState.Ready) { Close(FinTsPinTanReadContinuationAttemptState.ClockInvalid); } return FinTsPinTanReadContinuationAttemptTransition.Terminal; }
            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadContinuationAttemptState.Ready) { return FinTsPinTanReadContinuationAttemptTransition.Terminal; }
            _pending = candidate; _candidates = 1; _state = FinTsPinTanReadContinuationAttemptState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadContinuationAttemptTransition.CandidateRecorded : FinTsPinTanReadContinuationAttemptTransition.Terminal;
        }
    }

    public FinTsPinTanReadContinuationAttemptResult AcceptResponse(FinTsReadDataSet data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(_state == FinTsPinTanReadContinuationAttemptState.Ready ? FinTsPinTanReadContinuationAttemptTransition.WrongState : FinTsPinTanReadContinuationAttemptTransition.Terminal); }
                var binding = FinTsPinTanReadContinuationResponseBinding.Evaluate(_pending!, data.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadContinuationAttemptTransition.Terminal); }
                const FinTsPinTanResponseBindingIssue foreignScope = FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference | FinTsPinTanResponseBindingIssue.UnknownSegmentReference | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;
                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanReadIssue.BindingNeedsReview; _bindingIssues = binding.Issues; _readIssues = FinTsReadContextIssue.None;
                    return new(FinTsPinTanReadContinuationAttemptTransition.ContextMismatch);
                }
                var evidence = FinTsPinTanReadContinuationEvidence.Evaluate(binding, data, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadContinuationAttemptTransition.Terminal); }
                _issues = evidence.Issues; _bindingIssues = binding.Issues; _readIssues = evidence.ReadIssues; _handled = 1;
                var state = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanReadContinuationAttemptState.ExecutionReported,
                    FinTsReadOutcomeObservation.PartialReported => FinTsPinTanReadContinuationAttemptState.PartialReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanReadContinuationAttemptState.UnavailableReported,
                    _ => FinTsPinTanReadContinuationAttemptState.NeedsReview,
                };
                var transition = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanReadContinuationAttemptTransition.ExecutionReported,
                    FinTsReadOutcomeObservation.PartialReported => FinTsPinTanReadContinuationAttemptTransition.PartialReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanReadContinuationAttemptTransition.UnavailableReported,
                    _ => FinTsPinTanReadContinuationAttemptTransition.RejectedForReview,
                };
                Close(state);
                return new(transition, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadContinuationAttemptState.Ready) { Close(FinTsPinTanReadContinuationAttemptState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanReadContinuationAttemptState.Ready) { Close(FinTsPinTanReadContinuationAttemptState.Cancelled); } } }
    /// <summary>Abandon after parsing/transport failure or disconnect. No automatic retry or credential ownership.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanReadContinuationAttemptState.Ready) { Close(FinTsPinTanReadContinuationAttemptState.Stopped); } } }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
    private bool IsActive => _state == FinTsPinTanReadContinuationAttemptState.AwaitingResponse;
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanReadContinuationAttemptState.Ready) { Close(FinTsPinTanReadContinuationAttemptState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsPinTanReadContinuationAttemptState.ClockInvalid); return; }
            _lastTimestamp = now; _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanReadContinuationAttemptState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanReadContinuationAttemptState.TimedOut); }
        }
        catch (Exception) { if (IsActive) { Close(FinTsPinTanReadContinuationAttemptState.ClockInvalid); } }
    }
    private void Close(FinTsPinTanReadContinuationAttemptState state) { _state = state; _pending = null; }
}
