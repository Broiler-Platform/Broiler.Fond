namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsAllDiscoveryState
{
    Ready, AwaitingResponse, ExecutionReported, UnavailableReported, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsAllDiscoveryTransition
{
    RequestRecorded, ExecutionReported, UnavailableReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

/// <summary>One returned comparison, owned by the caller. The attempt retains no terminal evidence.</summary>
public sealed class FinTsAllDiscoveryAttemptResult
{
    internal FinTsAllDiscoveryAttemptResult(FinTsAllDiscoveryTransition transition, FinTsAllAccountDiscoveryEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsAllDiscoveryTransition Transition { get; }
    /// <summary>Untrusted evidence is returned once for a bound result, including a review outcome. Never an ingestion authorization.</summary>
    public FinTsAllAccountDiscoveryEvidence? Evidence { get; }
}

/// <summary>Scalar-only lifecycle diagnostics; account identifiers and candidate data are excluded.</summary>
public sealed class FinTsAllDiscoverySnapshot
{
    internal FinTsAllDiscoverySnapshot(FinTsAllDiscoveryState state, int requests, int accepted, TimeSpan remaining,
        FinTsAllDiscoveryIssue issues, FinTsReadContextIssue responseIssues, FinTsSyntaxError? failure)
    { State = state; RequestsRecorded = requests; ResponsesAccepted = accepted; RemainingTime = remaining; Issues = issues; ResponseIssues = responseIssues; Failure = failure; }
    public FinTsAllDiscoveryState State { get; }
    public int RequestsRecorded { get; }
    public int ResponsesAccepted { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsAllDiscoveryIssue Issues { get; }
    public FinTsReadContextIssue ResponseIssues { get; }
    public FinTsSyntaxError? Failure { get; }
}

/// <summary>One synchronized HKSPA-1 all-account attempt. No sending, authentication, reconciliation or durable replay handling.</summary>
public sealed class FinTsAllAccountDiscoveryAttempt
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsAllDiscoveryState _state;
    private FinTsReadRequestContext? _pending;
    private FinTsReadParameterSet? _parameters;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _requests, _accepted;
    private FinTsAllDiscoveryIssue _issues;
    private FinTsReadContextIssue _responseIssues;
    private FinTsSyntaxError? _failure;

    public FinTsAllAccountDiscoveryAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System;
        if (_clock.TimestampFrequency <= 0) { throw new ArgumentException("A positive timestamp frequency is required.", nameof(clock)); }
        _lifetime = lifetime;
    }

    public FinTsAllDiscoverySnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _requests, _accepted, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _responseIssues, _failure);
        }
    }

    public FinTsAllDiscoveryTransition Start(FinTsReadRequestContext request, FinTsReadParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(parameters);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsAllDiscoveryState.Ready) { return IsActive ? FinTsAllDiscoveryTransition.WrongState : FinTsAllDiscoveryTransition.Terminal; }
            _issues = FinTsAllAccountDiscoveryEvidence.RequestIssues(request, parameters, out _);
            if (_issues != FinTsAllDiscoveryIssue.None) { Close(FinTsAllDiscoveryState.NeedsReview); return FinTsAllDiscoveryTransition.RejectedForReview; }
            _startedAt = _lastTimestamp = _clock.GetTimestamp();
            _pending = request; _parameters = parameters;
            _requests = 1;
            _state = FinTsAllDiscoveryState.AwaitingResponse;
            return FinTsAllDiscoveryTransition.RequestRecorded;
        }
    }

    public FinTsAllDiscoveryAttemptResult AcceptResponse(FinTsReadDataSet response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return new(_state == FinTsAllDiscoveryState.Ready ? FinTsAllDiscoveryTransition.WrongState : FinTsAllDiscoveryTransition.Terminal); }
            // Reject foreign scope before expanding account candidates; it cannot exhaust this request's candidate budget.
            var preflight = FinTsReadContextEvidence.ResponseIssues(_pending!, response, out _, out _, out _, CancellationToken.None);
            ObserveClock();
            if (!IsActive) { return new(FinTsAllDiscoveryTransition.Terminal); }
            if ((preflight & (FinTsReadContextIssue.MessageMismatch | FinTsReadContextIssue.ReferenceMismatch)) != 0)
            { _responseIssues = preflight; return new(FinTsAllDiscoveryTransition.ContextMismatch); }
            FinTsAllAccountDiscoveryEvidence evidence;
            try { evidence = FinTsAllAccountDiscoveryEvidence.Evaluate(_pending!, response, _parameters!); }
            catch (FinTsFormatException error)
            {
                ObserveClock();
                if (!IsActive) { return new(FinTsAllDiscoveryTransition.Terminal); }
                _responseIssues = preflight;
                _failure = error.Error;
                Close(FinTsAllDiscoveryState.NeedsReview);
                return new(FinTsAllDiscoveryTransition.RejectedForReview);
            }
            ObserveClock();
            if (!IsActive) { return new(FinTsAllDiscoveryTransition.Terminal); }
            _issues = evidence.Issues; _responseIssues = evidence.ResponseIssues;
            if (!evidence.HasMatchingEvidence)
            { Close(FinTsAllDiscoveryState.NeedsReview); return new(FinTsAllDiscoveryTransition.RejectedForReview, evidence); }
            _accepted = 1;
            bool unavailable = evidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported;
            Close(unavailable ? FinTsAllDiscoveryState.UnavailableReported : FinTsAllDiscoveryState.ExecutionReported);
            return new(unavailable ? FinTsAllDiscoveryTransition.UnavailableReported : FinTsAllDiscoveryTransition.ExecutionReported, evidence);
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsAllDiscoveryState.Ready) { Close(FinTsAllDiscoveryState.Cancelled); } } }
    /// <summary>Local abandonment after parse failure, disconnect or transport failure. No automatic retry.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsAllDiscoveryState.Ready) { Close(FinTsAllDiscoveryState.Stopped); } } }
    private bool IsActive => _state == FinTsAllDiscoveryState.AwaitingResponse;
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        long now = _clock.GetTimestamp();
        if (now < _lastTimestamp) { Close(FinTsAllDiscoveryState.ClockInvalid); return; }
        _lastTimestamp = now;
        _elapsed = _clock.GetElapsedTime(_startedAt, now);
        if (_elapsed < TimeSpan.Zero) { Close(FinTsAllDiscoveryState.ClockInvalid); }
        else if (_elapsed >= _lifetime) { Close(FinTsAllDiscoveryState.TimedOut); }
    }
    private void Close(FinTsAllDiscoveryState state) { _state = state; _pending = null; _parameters = null; }
}
