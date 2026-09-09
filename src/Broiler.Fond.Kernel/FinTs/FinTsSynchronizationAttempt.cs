namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsSynchronizationState
{
    Ready, AwaitingSynchronization, AwaitingCloseRequest, AwaitingCloseResponse,
    ReinitializationRequired, Aborted, NeedsReview, Cancelled, TimedOut, Stopped, ClockInvalid,
}

public enum FinTsSynchronizationTransition
{
    RequestRecorded, CloseRequired, CloseRequestRecorded, ReinitializationRequired,
    AbortReported, RejectedForReview, ContextMismatch, WrongState, Terminal,
}

/// <summary>One-time caller-owned evidence handoff. No result authorizes recovery application or business requests.</summary>
public sealed class FinTsSynchronizationAttemptResult
{
    internal FinTsSynchronizationAttemptResult(FinTsSynchronizationTransition transition,
        FinTsSynchronizationEvidence? synchronization = null, FinTsDialogueEndEvidence? closing = null)
    { Transition = transition; SynchronizationEvidence = synchronization; ClosingEvidence = closing; }
    public FinTsSynchronizationTransition Transition { get; }
    public FinTsSynchronizationEvidence? SynchronizationEvidence { get; }
    public FinTsDialogueEndEvidence? ClosingEvidence { get; }
}

/// <summary>Scalar-only diagnostics; no identifiers, recovered values or source objects.</summary>
public sealed class FinTsSynchronizationSnapshot
{
    internal FinTsSynchronizationSnapshot(FinTsSynchronizationState state, int requests, int accepted,
        TimeSpan remaining, FinTsSynchronizationIssue synchronizationIssues, FinTsDialogueEndIssue closingIssues)
    { State = state; RequestsRecorded = requests; ResponsesAccepted = accepted; RemainingTime = remaining; SynchronizationIssues = synchronizationIssues; ClosingIssues = closingIssues; }
    public FinTsSynchronizationState State { get; }
    public int RequestsRecorded { get; }
    public int ResponsesAccepted { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsSynchronizationIssue SynchronizationIssues { get; }
    public FinTsDialogueEndIssue ClosingIssues { get; }
}

/// <summary>One local synchronization followed by an explicit close, sharing an absolute monotonic deadline.
/// No sending, authentication, recovery application, automatic reinitialization or durable replay state.</summary>
public sealed class FinTsSynchronizationAttempt
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsSynchronizationState _state;
    private FinTsUnsignedSynchronizationRequest? _pendingSynchronization;
    private FinTsSynchronizationProfile _profile;
    private FinTsSynchronizationRecoveryContext? _recovery;
    private string? _dialogue;
    private FinTsUnsignedDialogueEndRequest? _pendingClose;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _requests, _accepted;
    private FinTsSynchronizationIssue _synchronizationIssues;
    private FinTsDialogueEndIssue _closingIssues;

    public FinTsSynchronizationAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System;
        if (_clock.TimestampFrequency <= 0) { throw new ArgumentException("A positive timestamp frequency is required.", nameof(clock)); }
        _lifetime = lifetime;
    }

    public FinTsSynchronizationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _requests, _accepted, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _synchronizationIssues, _closingIssues);
        }
    }

    public FinTsSynchronizationTransition Start(FinTsUnsignedSynchronizationRequest request,
        FinTsSynchronizationProfile profile, FinTsSynchronizationRecoveryContext? recoveryContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsSynchronizationState.Ready) { return Unavailable; }
            _synchronizationIssues = FinTsSynchronizationEvidence.RequestIssues(request, profile, recoveryContext);
            if (_synchronizationIssues != FinTsSynchronizationIssue.None)
            { Close(FinTsSynchronizationState.NeedsReview); return FinTsSynchronizationTransition.RejectedForReview; }
            _startedAt = _lastTimestamp = _clock.GetTimestamp();
            _pendingSynchronization = request; _profile = profile; _recovery = recoveryContext;
            _requests = 1; _state = FinTsSynchronizationState.AwaitingSynchronization;
            return FinTsSynchronizationTransition.RequestRecorded;
        }
    }

    public FinTsSynchronizationAttemptResult AcceptSynchronization(FinTsSynchronizationDataSet response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsSynchronizationState.AwaitingSynchronization) { return new(Unavailable); }
            var evidence = FinTsSynchronizationEvidence.Evaluate(_pendingSynchronization!, response, _profile, _recovery);
            ObserveClock();
            if (!IsActive) { return new(FinTsSynchronizationTransition.Terminal); }
            _synchronizationIssues = evidence.Issues;
            if ((evidence.Issues & (FinTsSynchronizationIssue.MessageMismatch | FinTsSynchronizationIssue.ReferenceMismatch)) != 0)
            { return new(FinTsSynchronizationTransition.ContextMismatch); }
            if (!evidence.HasMatchingEvidence)
            { Close(FinTsSynchronizationState.NeedsReview); return new(FinTsSynchronizationTransition.RejectedForReview, synchronization: evidence); }
            _accepted = 1; _dialogue = evidence.ReportedDialogueId;
            _pendingSynchronization = null; _recovery = null; _profile = default;
            _state = FinTsSynchronizationState.AwaitingCloseRequest;
            return new(FinTsSynchronizationTransition.CloseRequired, synchronization: evidence);
        }
    }

    /// <summary>Records the caller's next request. This restricted flow permits only client/bank counters 2/2 after first-message synchronization.</summary>
    public FinTsSynchronizationTransition RecordClose(FinTsUnsignedDialogueEndRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsSynchronizationState.AwaitingCloseRequest) { return Unavailable; }
            if (request.Request.DialogueId != _dialogue || request.Frame.MessageNumber != 2 || request.ExpectedBankMessageNumber != 2)
            { _closingIssues = FinTsDialogueEndIssue.MessageMismatch; return FinTsSynchronizationTransition.ContextMismatch; }
            _closingIssues = FinTsDialogueEndIssue.None;
            _pendingClose = request; _dialogue = null;
            _requests = 2; _state = FinTsSynchronizationState.AwaitingCloseResponse;
            return FinTsSynchronizationTransition.CloseRequestRecorded;
        }
    }

    public FinTsSynchronizationAttemptResult AcceptClose(FinTsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsSynchronizationState.AwaitingCloseResponse) { return new(Unavailable); }
            var evidence = FinTsDialogueEndEvidence.Evaluate(_pendingClose!, response);
            ObserveClock();
            if (!IsActive) { return new(FinTsSynchronizationTransition.Terminal); }
            _closingIssues = evidence.Issues;
            if ((evidence.Issues & (FinTsDialogueEndIssue.MessageMismatch | FinTsDialogueEndIssue.ReferenceMismatch)) != 0)
            { return new(FinTsSynchronizationTransition.ContextMismatch); }
            if (!evidence.HasMatchingEvidence)
            { Close(FinTsSynchronizationState.NeedsReview); return new(FinTsSynchronizationTransition.RejectedForReview, closing: evidence); }
            _accepted = 2;
            bool aborted = evidence.Outcome == FinTsDialogueEndOutcome.AbortReported;
            Close(aborted ? FinTsSynchronizationState.Aborted : FinTsSynchronizationState.ReinitializationRequired);
            return new(aborted ? FinTsSynchronizationTransition.AbortReported : FinTsSynchronizationTransition.ReinitializationRequired, closing: evidence);
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsSynchronizationState.Ready) { Close(FinTsSynchronizationState.Cancelled); } } }
    /// <summary>Local abandonment after parse, disconnect or transport failure. Does not send a close or retry.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsSynchronizationState.Ready) { Close(FinTsSynchronizationState.Stopped); } } }
    private bool IsActive => _state is FinTsSynchronizationState.AwaitingSynchronization or FinTsSynchronizationState.AwaitingCloseRequest or FinTsSynchronizationState.AwaitingCloseResponse;
    private FinTsSynchronizationTransition Unavailable => IsActive || _state == FinTsSynchronizationState.Ready ? FinTsSynchronizationTransition.WrongState : FinTsSynchronizationTransition.Terminal;
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        long now = _clock.GetTimestamp();
        if (now < _lastTimestamp) { Close(FinTsSynchronizationState.ClockInvalid); return; }
        _lastTimestamp = now;
        _elapsed = _clock.GetElapsedTime(_startedAt, now);
        if (_elapsed < TimeSpan.Zero) { Close(FinTsSynchronizationState.ClockInvalid); }
        else if (_elapsed >= _lifetime) { Close(FinTsSynchronizationState.TimedOut); }
    }
    private void Close(FinTsSynchronizationState state)
    {
        _state = state; _pendingSynchronization = null; _recovery = null; _profile = default; _dialogue = null; _pendingClose = null;
    }
}
