namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanAllAccountDiscoveryState
{
    Ready,
    AwaitingResponse,
    ExecutionReported,
    UnavailableReported,
    NeedsReview,
    Cancelled,
    TimedOut,
    Stopped,
    ClockInvalid,
}

public enum FinTsPinTanAllAccountDiscoveryTransition
{
    CandidateRecorded,
    ExecutionReported,
    UnavailableReported,
    RejectedForReview,
    ContextMismatch,
    WrongState,
    Terminal,
}

public sealed class FinTsPinTanAllAccountDiscoveryResult
{
    internal FinTsPinTanAllAccountDiscoveryResult(
        FinTsPinTanAllAccountDiscoveryTransition transition,
        FinTsPinTanAllAccountEvidence? evidence = null)
    {
        Transition = transition;
        Evidence = evidence;
    }

    public FinTsPinTanAllAccountDiscoveryTransition Transition { get; }
    /// <summary>Caller-owned evidence returned once for a bound response, including a review or unavailable observation.
    /// Never an account ingestion authorization.</summary>
    public FinTsPinTanAllAccountEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, account, or credential references.</summary>
public sealed class FinTsPinTanAllAccountDiscoverySnapshot
{
    internal FinTsPinTanAllAccountDiscoverySnapshot(
        FinTsPinTanAllAccountDiscoveryState state,
        int candidates,
        int handled,
        TimeSpan remaining,
        FinTsPinTanAllAccountIssue issues,
        FinTsPinTanResponseBindingIssue bindingIssues,
        FinTsReadContextIssue responseIssues)
    {
        State = state;
        CandidatesRecorded = candidates;
        ResponsesHandled = handled;
        RemainingTime = remaining;
        Issues = issues;
        BindingIssues = bindingIssues;
        ResponseIssues = responseIssues;
    }

    public FinTsPinTanAllAccountDiscoveryState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanAllAccountIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsReadContextIssue ResponseIssues { get; }
}

/// <summary>One local all-account discovery candidate, one absolute deadline, and one scoped response handoff.
/// No encoding, sending, authentication, or account ingestion. Disposal releases pending metadata, not caller-owned evidence.</summary>
public sealed class FinTsPinTanAllAccountDiscoveryAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsPinTanAllAccountDiscoveryState _state;
    private FinTsPinTanAllAccountRequestBinding? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanAllAccountIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsReadContextIssue _responseIssues;

    public FinTsPinTanAllAccountDiscoveryAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        _clock = clock ?? TimeProvider.System;
        _lifetime = lifetime;

        bool valid;
        try { valid = _clock.TimestampFrequency > 0; }
        catch (Exception) { valid = false; }
        if (!valid)
        {
            throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock));
        }
    }

    public FinTsPinTanAllAccountDiscoverySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(
                _state,
                _candidates,
                _handled,
                IsActive ? _lifetime - _elapsed : TimeSpan.Zero,
                _issues,
                _bindingIssues,
                _responseIssues);
        }
    }

    /// <summary>Records candidate metadata only, without proof of encoding or transmission. The absolute deadline starts here.</summary>
    public FinTsPinTanAllAccountDiscoveryTransition Start(
        FinTsPinTanAllAccountRequestBinding candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        lock (_gate)
        {
            ObserveClock();
            CheckCancellation(cancellationToken);

            if (_state != FinTsPinTanAllAccountDiscoveryState.Ready)
            {
                return IsActive
                    ? FinTsPinTanAllAccountDiscoveryTransition.WrongState
                    : FinTsPinTanAllAccountDiscoveryTransition.Terminal;
            }

            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception)
            {
                if (_state == FinTsPinTanAllAccountDiscoveryState.Ready)
                {
                    Close(FinTsPinTanAllAccountDiscoveryState.ClockInvalid);
                }
                return FinTsPinTanAllAccountDiscoveryTransition.Terminal;
            }

            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanAllAccountDiscoveryState.Ready)
            {
                return FinTsPinTanAllAccountDiscoveryTransition.Terminal;
            }

            _pending = candidate;
            _candidates = 1;
            _state = FinTsPinTanAllAccountDiscoveryState.AwaitingResponse;

            ObserveClock();
            CheckCancellation(cancellationToken);

            return IsActive
                ? FinTsPinTanAllAccountDiscoveryTransition.CandidateRecorded
                : FinTsPinTanAllAccountDiscoveryTransition.Terminal;
        }
    }

    public FinTsPinTanAllAccountDiscoveryResult AcceptResponse(
        FinTsReadDataSet data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        lock (_gate)
        {
            try
            {
                ObserveClock();
                CheckCancellation(cancellationToken);

                if (!IsActive)
                {
                    return new(_state == FinTsPinTanAllAccountDiscoveryState.Ready
                        ? FinTsPinTanAllAccountDiscoveryTransition.WrongState
                        : FinTsPinTanAllAccountDiscoveryTransition.Terminal);
                }

                var binding = FinTsPinTanAllAccountResponseBinding.Evaluate(_pending!, data.Source, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive)
                {
                    return new(FinTsPinTanAllAccountDiscoveryTransition.Terminal);
                }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanAllAccountIssue.BindingNeedsReview;
                    _bindingIssues = binding.Issues;
                    _responseIssues = FinTsReadContextIssue.None;
                    return new(FinTsPinTanAllAccountDiscoveryTransition.ContextMismatch);
                }

                var evidence = FinTsPinTanAllAccountEvidence.Evaluate(binding, data, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive)
                {
                    return new(FinTsPinTanAllAccountDiscoveryTransition.Terminal);
                }

                _issues = evidence.Issues;
                _bindingIssues = binding.Issues;
                _responseIssues = evidence.ResponseIssues;
                _handled = 1;

                var state = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanAllAccountDiscoveryState.ExecutionReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanAllAccountDiscoveryState.UnavailableReported,
                    _ => FinTsPinTanAllAccountDiscoveryState.NeedsReview,
                };

                var transition = evidence.Outcome switch
                {
                    FinTsReadOutcomeObservation.ExecutionReported => FinTsPinTanAllAccountDiscoveryTransition.ExecutionReported,
                    FinTsReadOutcomeObservation.UnavailableReported => FinTsPinTanAllAccountDiscoveryTransition.UnavailableReported,
                    _ => FinTsPinTanAllAccountDiscoveryTransition.RejectedForReview,
                };

                Close(state);
                return new(transition, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanAllAccountDiscoveryState.Ready)
                {
                    Close(FinTsPinTanAllAccountDiscoveryState.Cancelled);
                }
                throw;
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanAllAccountDiscoveryState.Ready)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.Cancelled);
            }
        }
    }

    /// <summary>Abandon after transport failure or disconnect. No automatic retry.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanAllAccountDiscoveryState.Ready)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.Stopped);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private bool IsActive => _state == FinTsPinTanAllAccountDiscoveryState.AwaitingResponse;

    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanAllAccountDiscoveryState.Ready)
        {
            Close(FinTsPinTanAllAccountDiscoveryState.Cancelled);
        }
        token.ThrowIfCancellationRequested();
    }

    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.ClockInvalid);
                return;
            }

            _lastTimestamp = now;
            _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }

            if (_elapsed < TimeSpan.Zero)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.ClockInvalid);
            }
            else if (_elapsed >= _lifetime)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.TimedOut);
            }
        }
        catch (Exception)
        {
            if (IsActive)
            {
                Close(FinTsPinTanAllAccountDiscoveryState.ClockInvalid);
            }
        }
    }

    private void Close(FinTsPinTanAllAccountDiscoveryState state)
    {
        _state = state;
        _pending = null;
    }
}
