namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanDialogueEndAttemptState
{
    Ready, AwaitingResponse, ReinitializationRequired, Aborted, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsPinTanDialogueEndAttemptTransition
{
    CandidateRecorded, ReinitializationRequired, AbortReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

public sealed class FinTsPinTanDialogueEndAttemptResult
{
    internal FinTsPinTanDialogueEndAttemptResult(FinTsPinTanDialogueEndAttemptTransition transition, FinTsPinTanDialogueEndEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsPinTanDialogueEndAttemptTransition Transition { get; }
    /// <summary>Caller-owned evidence returned once for a scoped response, including review.
    /// Reported closure requires fresh initialization; a reported abort must not trigger another close.</summary>
    public FinTsPinTanDialogueEndEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, credential or identity references.</summary>
public sealed class FinTsPinTanDialogueEndSnapshot
{
    internal FinTsPinTanDialogueEndSnapshot(FinTsPinTanDialogueEndAttemptState state, int candidates, int handled, TimeSpan remaining,
        FinTsPinTanDialogueEndIssue issues, FinTsPinTanResponseBindingIssue bindingIssues)
    { State = state; CandidatesRecorded = candidates; ResponsesHandled = handled; RemainingTime = remaining; Issues = issues; BindingIssues = bindingIssues; }
    public FinTsPinTanDialogueEndAttemptState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanDialogueEndIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
}

/// <summary>One local assembled closing candidate and one response handoff. No encoding, sending or authentication.
/// The attempt takes no credentials/output buffers; disposal releases pending metadata, not caller-owned objects.</summary>
public sealed class FinTsPinTanDialogueEndAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsPinTanDialogueEndAttemptState _state;
    private FinTsPinTanDialogueEndRequestBinding? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanDialogueEndIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;

    public FinTsPinTanDialogueEndAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System; _lifetime = lifetime;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
    }

    public FinTsPinTanDialogueEndSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _candidates, _handled, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _bindingIssues);
        }
    }

    /// <summary>Records metadata only; it does not prove encoding or transmission. The absolute deadline begins here.</summary>
    public FinTsPinTanDialogueEndAttemptTransition Start(FinTsPinTanDialogueEndRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanDialogueEndAttemptState.Ready) { return IsActive ? FinTsPinTanDialogueEndAttemptTransition.WrongState : FinTsPinTanDialogueEndAttemptTransition.Terminal; }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception) { if (_state == FinTsPinTanDialogueEndAttemptState.Ready) { Close(FinTsPinTanDialogueEndAttemptState.ClockInvalid); } return FinTsPinTanDialogueEndAttemptTransition.Terminal; }
            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanDialogueEndAttemptState.Ready) { return FinTsPinTanDialogueEndAttemptTransition.Terminal; }
            _pending = candidate; _candidates = 1; _state = FinTsPinTanDialogueEndAttemptState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanDialogueEndAttemptTransition.CandidateRecorded : FinTsPinTanDialogueEndAttemptTransition.Terminal;
        }
    }

    public FinTsPinTanDialogueEndAttemptResult AcceptResponse(FinTsResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(_state == FinTsPinTanDialogueEndAttemptState.Ready ? FinTsPinTanDialogueEndAttemptTransition.WrongState : FinTsPinTanDialogueEndAttemptTransition.Terminal); }
                var binding = FinTsPinTanDialogueEndResponseBinding.Evaluate(_pending!, response, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanDialogueEndAttemptTransition.Terminal); }
                const FinTsPinTanResponseBindingIssue foreignScope = FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference | FinTsPinTanResponseBindingIssue.UnknownSegmentReference;
                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanDialogueEndIssue.BindingNeedsReview; _bindingIssues = binding.Issues;
                    return new(FinTsPinTanDialogueEndAttemptTransition.ContextMismatch);
                }
                var evidence = FinTsPinTanDialogueEndEvidence.Evaluate(binding, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanDialogueEndAttemptTransition.Terminal); }
                _issues = evidence.Issues; _bindingIssues = binding.Issues; _handled = 1;
                var state = evidence.Outcome switch
                {
                    FinTsDialogueEndOutcome.ClosureReported => FinTsPinTanDialogueEndAttemptState.ReinitializationRequired,
                    FinTsDialogueEndOutcome.AbortReported => FinTsPinTanDialogueEndAttemptState.Aborted,
                    _ => FinTsPinTanDialogueEndAttemptState.NeedsReview,
                };
                var transition = evidence.Outcome switch
                {
                    FinTsDialogueEndOutcome.ClosureReported => FinTsPinTanDialogueEndAttemptTransition.ReinitializationRequired,
                    FinTsDialogueEndOutcome.AbortReported => FinTsPinTanDialogueEndAttemptTransition.AbortReported,
                    _ => FinTsPinTanDialogueEndAttemptTransition.RejectedForReview,
                };
                Close(state);
                return new(transition, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanDialogueEndAttemptState.Ready) { Close(FinTsPinTanDialogueEndAttemptState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanDialogueEndAttemptState.Ready) { Close(FinTsPinTanDialogueEndAttemptState.Cancelled); } } }
    /// <summary>Abandon after parsing/transport failure or disconnect. No automatic retry or credential ownership.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsPinTanDialogueEndAttemptState.Ready) { Close(FinTsPinTanDialogueEndAttemptState.Stopped); } } }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
    private bool IsActive => _state == FinTsPinTanDialogueEndAttemptState.AwaitingResponse;
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanDialogueEndAttemptState.Ready) { Close(FinTsPinTanDialogueEndAttemptState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsPinTanDialogueEndAttemptState.ClockInvalid); return; }
            _lastTimestamp = now; _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanDialogueEndAttemptState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanDialogueEndAttemptState.TimedOut); }
        }
        catch (Exception) { if (IsActive) { Close(FinTsPinTanDialogueEndAttemptState.ClockInvalid); } }
    }
    private void Close(FinTsPinTanDialogueEndAttemptState state) { _state = state; _pending = null; }
}
