namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsInitializationState
{
    Ready, AwaitingResponse, ExecutionReported, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsInitializationTransition
{
    RequestRecorded, ExecutionReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

/// <summary>One caller-owned comparison. Terminal evidence is never retained by the attempt.</summary>
public sealed class FinTsInitializationAttemptResult
{
    internal FinTsInitializationAttemptResult(FinTsInitializationTransition transition, FinTsInitializationEvidence? evidence = null)
    { Transition = transition; Evidence = evidence; }
    public FinTsInitializationTransition Transition { get; }
    /// <summary>Returned once for a bound result, including review. Never an authenticated session or activation authorization.</summary>
    public FinTsInitializationEvidence? Evidence { get; }
}

/// <summary>Scalar-only diagnostics, excluding customer, user, system and reported dialogue identifiers.</summary>
public sealed class FinTsInitializationSnapshot
{
    internal FinTsInitializationSnapshot(FinTsInitializationState state, int requests, int accepted, TimeSpan remaining, FinTsInitializationIssue issues)
    { State = state; RequestsRecorded = requests; ResponsesAccepted = accepted; RemainingTime = remaining; Issues = issues; }
    public FinTsInitializationState State { get; }
    public int RequestsRecorded { get; }
    public int ResponsesAccepted { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsInitializationIssue Issues { get; }
}

/// <summary>One synchronized local initialization attempt. No sending, authentication, active dialogue or durable replay state.</summary>
public sealed class FinTsInitializationAttempt
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsInitializationState _state;
    private FinTsUnsignedInitializationRequest? _pending;
    private string? _expectedUserId;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _requests, _accepted;
    private FinTsInitializationIssue _issues;

    public FinTsInitializationAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System;
        if (_clock.TimestampFrequency <= 0) { throw new ArgumentException("A positive timestamp frequency is required.", nameof(clock)); }
        _lifetime = lifetime;
    }

    public FinTsInitializationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _requests, _accepted, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues);
        }
    }

    public FinTsInitializationTransition Start(FinTsUnsignedInitializationRequest request, string? expectedUserId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsInitializationState.Ready) { return IsActive ? FinTsInitializationTransition.WrongState : FinTsInitializationTransition.Terminal; }
            _issues = FinTsInitializationEvidence.RequestIssues(request, expectedUserId);
            if (_issues != FinTsInitializationIssue.None) { Close(FinTsInitializationState.NeedsReview); return FinTsInitializationTransition.RejectedForReview; }
            _startedAt = _lastTimestamp = _clock.GetTimestamp();
            _pending = request; _expectedUserId = expectedUserId;
            _requests = 1; _state = FinTsInitializationState.AwaitingResponse;
            return FinTsInitializationTransition.RequestRecorded;
        }
    }

    public FinTsInitializationAttemptResult AcceptResponse(FinTsParameterSet parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return new(_state == FinTsInitializationState.Ready ? FinTsInitializationTransition.WrongState : FinTsInitializationTransition.Terminal); }
            var scope = FinTsInitializationEvidence.ResponseScopeIssues(_pending!, parameters.Source, out _, CancellationToken.None);
            ObserveClock();
            if (!IsActive) { return new(FinTsInitializationTransition.Terminal); }
            if ((scope & (FinTsInitializationIssue.MessageMismatch | FinTsInitializationIssue.ReferenceMismatch)) != 0)
            { _issues = scope; return new(FinTsInitializationTransition.ContextMismatch); }
            var evidence = FinTsInitializationEvidence.Evaluate(_pending!, parameters, _expectedUserId);
            ObserveClock();
            if (!IsActive) { return new(FinTsInitializationTransition.Terminal); }
            _issues = evidence.Issues;
            if (!evidence.HasMatchingEvidence)
            { Close(FinTsInitializationState.NeedsReview); return new(FinTsInitializationTransition.RejectedForReview, evidence); }
            _accepted = 1;
            Close(FinTsInitializationState.ExecutionReported);
            return new(FinTsInitializationTransition.ExecutionReported, evidence);
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Cancelled); } } }
    /// <summary>Local abandonment after parse failure, disconnect or transport failure. No automatic retry.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Stopped); } } }
    private bool IsActive => _state == FinTsInitializationState.AwaitingResponse;
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        long now = _clock.GetTimestamp();
        if (now < _lastTimestamp) { Close(FinTsInitializationState.ClockInvalid); return; }
        _lastTimestamp = now;
        _elapsed = _clock.GetElapsedTime(_startedAt, now);
        if (_elapsed < TimeSpan.Zero) { Close(FinTsInitializationState.ClockInvalid); }
        else if (_elapsed >= _lifetime) { Close(FinTsInitializationState.TimedOut); }
    }
    private void Close(FinTsInitializationState state) { _state = state; _pending = null; _expectedUserId = null; }
}
