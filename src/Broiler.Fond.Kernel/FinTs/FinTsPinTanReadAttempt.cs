namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanReadAttemptState
{
    Ready, AwaitingResponse, ExecutionReported, PartialReported, UnavailableReported, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsPinTanReadAttemptTransition
{
    CandidateRecorded, ExecutionReported, PartialReported, UnavailableReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

public sealed class FinTsPinTanReadAttemptResult
{
    internal FinTsPinTanReadAttemptResult(FinTsPinTanReadAttemptTransition transition, FinTsPinTanReadEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsPinTanReadAttemptTransition Transition { get; }
    /// <summary>Caller-owned evidence returned once for a scoped response, including review or partial observations.
    /// A partial handoff terminates this first-read attempt and authorizes no continuation.</summary>
    public FinTsPinTanReadEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, account, token or credential references.</summary>
public sealed class FinTsPinTanReadSnapshot
{
    internal FinTsPinTanReadSnapshot(FinTsPinTanReadAttemptState state, int candidates, int handled, TimeSpan remaining,
        FinTsPinTanReadIssue issues, FinTsPinTanResponseBindingIssue bindingIssues, FinTsReadContextIssue readIssues)
    { State = state; CandidatesRecorded = candidates; ResponsesHandled = handled; RemainingTime = remaining; Issues = issues; BindingIssues = bindingIssues; ReadIssues = readIssues; }
    public FinTsPinTanReadAttemptState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanReadIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsReadContextIssue ReadIssues { get; }
}

/// <summary>One local first-read candidate, one absolute deadline and one scoped response handoff. No encoding,
/// sending, authentication, SCA completion or continuation. Disposal releases pending metadata, not caller-owned evidence.</summary>
public sealed class FinTsPinTanReadAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsPinTanReadAttemptState _state;
    private FinTsPinTanReadRequestBinding? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanReadIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsReadContextIssue _readIssues;

    public FinTsPinTanReadAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System; _lifetime = lifetime;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
    }

    public FinTsPinTanReadSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _candidates, _handled, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _bindingIssues, _readIssues);
        }
    }

    /// <summary>Records metadata only, without proof of encoding or transmission. The absolute deadline starts here.</summary>
    public FinTsPinTanReadAttemptTransition Start(FinTsPinTanReadRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadAttemptState.Ready) { return IsActive ? FinTsPinTanReadAttemptTransition.WrongState : FinTsPinTanReadAttemptTransition.Terminal; }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception) { if (_state == FinTsPinTanReadAttemptState.Ready) { Close(FinTsPinTanReadAttemptState.ClockInvalid); } return FinTsPinTanReadAttemptTransition.Terminal; }
            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadAttemptState.Ready) { return FinTsPinTanReadAttemptTransition.Terminal; }
            _pending = candidate; _candidates = 1; _state = FinTsPinTanReadAttemptState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadAttemptTransition.CandidateRecorded : FinTsPinTanReadAttemptTransition.Terminal;
        }
    }

    public FinTsPinTanReadAttemptResult AcceptResponse(FinTsReadDataSet data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(_state == FinTsPinTanReadAttemptState.Ready ? FinTsPinTanReadAttemptTransition.WrongState : FinTsPinTanReadAttemptTransition.Terminal); }
                var binding = FinTsPinTanReadResponseBinding.Evaluate(_pending!, data.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadAttemptTransition.Terminal); }
                const FinTsPinTanResponseBindingIssue foreignScope = FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference | FinTsPinTanResponseBindingIssue.UnknownSegmentReference | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;
                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanReadIssue.BindingNeedsReview; _bindingIssues = binding.Issues; _readIssues = FinTsReadContextIssue.None;
                    return new(FinTsPinTanReadAttemptTransition.ContextMismatch);
                }
                var evidence = FinTsPinTanReadEvidence.Evaluate(binding, data, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadAttemptTransition.Terminal); }
                _issues = evidence.Issues; _bindingIssues = binding.Issues; _readIssues = evidence.ReadIssues; _handled = 1;
                var state = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanReadAttemptState.ExecutionReported,
                    FinTsReadOutcomeObservation.PartialReported => FinTsPinTanReadAttemptState.PartialReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanReadAttemptState.UnavailableReported,
                    _ => FinTsPinTanReadAttemptState.NeedsReview,
                };
                var transition = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanReadAttemptTransition.ExecutionReported,
                    FinTsReadOutcomeObservation.PartialReported => FinTsPinTanReadAttemptTransition.PartialReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanReadAttemptTransition.UnavailableReported,
                    _ => FinTsPinTanReadAttemptTransition.RejectedForReview,
                };
                Close(state);
                return new(transition, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadAttemptState.Ready) { Close(FinTsPinTanReadAttemptState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanReadAttemptState.Ready) { Close(FinTsPinTanReadAttemptState.Cancelled); } } }
    /// <summary>Abandon after parsing/transport failure or disconnect. No automatic retry or credential ownership.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanReadAttemptState.Ready) { Close(FinTsPinTanReadAttemptState.Stopped); } } }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
    private bool IsActive => _state == FinTsPinTanReadAttemptState.AwaitingResponse;
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanReadAttemptState.Ready) { Close(FinTsPinTanReadAttemptState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsPinTanReadAttemptState.ClockInvalid); return; }
            _lastTimestamp = now; _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanReadAttemptState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanReadAttemptState.TimedOut); }
        }
        catch (Exception) { if (IsActive) { Close(FinTsPinTanReadAttemptState.ClockInvalid); } }
    }
    private void Close(FinTsPinTanReadAttemptState state) { _state = state; _pending = null; }
}
