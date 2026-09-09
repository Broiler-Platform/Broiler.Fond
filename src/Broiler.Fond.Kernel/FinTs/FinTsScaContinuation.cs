namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsScaState
{
    Ready, AwaitingUserContinuation, AwaitingExternalNotification, WaitingToQuery, AwaitingQueryResponse,
    UserContinuationRecorded, ExemptionReported, ExecutionReported, Cancelled, TimedOut, Stopped,
    NeedsReview, QueryLimitReached, CounterExhausted, ClockInvalid,
}

public enum FinTsScaTransition
{
    Started, UserContinuationRecorded, QueryRecorded, PendingAccepted, ExemptionReported, ExecutionReported,
    ReplayRejected, WrongState, ContextMismatch, TooEarly, TriggerNotAllowed, Terminal, RejectedForReview,
}

public enum FinTsScaQueryTrigger { Manual, Automatic }

/// <summary>Scalar-only snapshot; no raw bank, request or challenge data in diagnostics.</summary>
public sealed class FinTsScaSnapshot
{
    internal FinTsScaSnapshot(FinTsScaState state, int queries, int maximum, TimeSpan remaining, TimeSpan wait)
    { State = state; QueriesRecorded = queries; MaximumQueries = maximum; RemainingTime = remaining; QueryWait = wait; }
    public FinTsScaState State { get; }
    public int QueriesRecorded { get; }
    public int MaximumQueries { get; }
    public TimeSpan RemainingTime { get; }
    public TimeSpan QueryWait { get; }
}

/// <summary>
/// One local synthetic SCA attempt. Records intent/evidence only; never sends, schedules, authenticates or accepts credentials.
/// Replay rejection is per instance and ends with this in-memory attempt; there is no durable replay protection.
/// </summary>
public sealed class FinTsScaContinuation
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    public const int MaximumLocalQueries = 128;
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _localQueryLimit;
    private readonly bool _allowAutomaticQueries;
    private FinTsScaState _state;
    private FinTsTanChallengeEvidence? _initial;
    private FinTsTanChallenge? _challenge;
    private FinTsTanRequestContext? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed, _queryNotBefore;
    private int _queries, _maximumQueries, _lastClientNumber, _lastBankNumber;

    public FinTsScaContinuation(TimeSpan lifetime, int maximumQueries = 32, bool allowAutomaticQueries = false, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        if (maximumQueries is < 0 or > MaximumLocalQueries) { throw new ArgumentOutOfRangeException(nameof(maximumQueries)); }
        _clock = clock ?? TimeProvider.System;
        if (_clock.TimestampFrequency <= 0) { throw new ArgumentException("A positive timestamp frequency is required.", nameof(clock)); }
        _lifetime = lifetime;
        _localQueryLimit = maximumQueries;
        _allowAutomaticQueries = allowAutomaticQueries;
    }

    public FinTsScaSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _queries, _maximumQueries, IsActive ? _lifetime - _elapsed : TimeSpan.Zero,
                _state == FinTsScaState.WaitingToQuery && _queryNotBefore > _elapsed ? _queryNotBefore - _elapsed : TimeSpan.Zero);
        }
    }

    /// <summary>Untrusted current evidence for a future display adapter. Terminal states release the model's reference.</summary>
    public FinTsTanChallenge? CurrentChallenge { get { lock (_gate) { ObserveClock(); return _challenge; } } }

    public FinTsScaTransition Start(FinTsTanChallengeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsScaState.Ready) { return IsActive ? FinTsScaTransition.WrongState : FinTsScaTransition.Terminal; }
            if (evidence.Issues != FinTsTanContextIssue.None || evidence.Request.Process is not ("1" or "4") ||
                evidence.Procedure.IsDecoupled && evidence.Request.Process != "4" ||
                evidence.Observation is not (FinTsTanOutcomeObservation.ChallengeReported or FinTsTanOutcomeObservation.ExemptionReported))
            { Close(FinTsScaState.NeedsReview); return FinTsScaTransition.RejectedForReview; }
            if (evidence.Observation == FinTsTanOutcomeObservation.ExemptionReported)
            { Close(FinTsScaState.ExemptionReported); return FinTsScaTransition.ExemptionReported; }
            _startedAt = _lastTimestamp = _clock.GetTimestamp();
            _initial = evidence;
            _challenge = evidence.Challenge;
            _lastClientNumber = evidence.Request.Frame.MessageNumber;
            _lastBankNumber = evidence.Response.Source.Frame.MessageNumber;
            var procedure = evidence.Procedure;
            if (_lastClientNumber == 9999 || _lastBankNumber == 9999)
            { Close(FinTsScaState.CounterExhausted); return FinTsScaTransition.Terminal; }
            if (procedure.IsDecoupled && procedure.Method.HeaderText() == "Decoupled")
            {
                _maximumQueries = Math.Min(_localQueryLimit, procedure.MaximumStatusQueries!.Value);
                if (_maximumQueries == 0) { Close(FinTsScaState.QueryLimitReached); return FinTsScaTransition.Terminal; }
                _queryNotBefore = TimeSpan.FromSeconds(procedure.FirstQueryDelaySeconds!.Value);
                _state = FinTsScaState.WaitingToQuery;
            }
            else { _state = procedure.IsDecoupled ? FinTsScaState.AwaitingExternalNotification : FinTsScaState.AwaitingUserContinuation; }
            return FinTsScaTransition.Started;
        }
    }

    /// <summary>Records a single local handoff intent. No TAN, signature or bank request is accepted or generated.</summary>
    public FinTsScaTransition RecordUserContinuation()
    {
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return _state == FinTsScaState.Ready ? FinTsScaTransition.WrongState : FinTsScaTransition.Terminal; }
            if (_state != FinTsScaState.AwaitingUserContinuation) { return FinTsScaTransition.WrongState; }
            Close(FinTsScaState.UserContinuationRecorded);
            return FinTsScaTransition.UserContinuationRecorded;
        }
    }

    /// <summary>Records one caller-created synthetic query. Recording is not permission to send it.</summary>
    public FinTsScaTransition RecordStatusQuery(FinTsTanRequestContext request, FinTsScaQueryTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(trigger)) { throw new ArgumentOutOfRangeException(nameof(trigger)); }
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return _state == FinTsScaState.Ready ? FinTsScaTransition.WrongState : FinTsScaTransition.Terminal; }
            if (_state != FinTsScaState.WaitingToQuery) { return FinTsScaTransition.WrongState; }
            var initial = _initial!;
            if (request.Process != "S" || request.Frame.Syntax.Segments.Count != 3 || request.Segment.Version != initial.Procedure.SegmentVersion ||
                !Same(Dialog(request.Frame), Dialog(initial.Response.Source.Frame)) || !Same(request.OrderReference, initial.Challenge!.OrderReference) ||
                !SameOptional(request.MediumName, initial.Request.MediumName) ||
                !request.Operation.IsEmpty && !Same(request.Operation, initial.Request.Operation)) { return FinTsScaTransition.ContextMismatch; }
            if (request.Frame.MessageNumber <= _lastClientNumber || request.ExpectedBankMessageNumber <= _lastBankNumber) { return FinTsScaTransition.ReplayRejected; }
            if (request.Frame.MessageNumber != _lastClientNumber + 1 || request.ExpectedBankMessageNumber != _lastBankNumber + 1) { return FinTsScaTransition.ContextMismatch; }
            var procedure = initial.Procedure;
            bool allowed = trigger == FinTsScaQueryTrigger.Automatic
                ? _allowAutomaticQueries && procedure.AutomatedQueriesAllowed == true
                : procedure.AutomatedQueriesAllowed == false || procedure.ManualConfirmationAllowed == true;
            if (!allowed) { return FinTsScaTransition.TriggerNotAllowed; }
            // Local conservative policy applies the minimum wait to both manual and automatic attempts.
            if (_elapsed < _queryNotBefore) { return FinTsScaTransition.TooEarly; }
            if (_queries >= _maximumQueries) { Close(FinTsScaState.QueryLimitReached); return FinTsScaTransition.Terminal; }
            _queries++;
            _pending = request;
            _state = FinTsScaState.AwaitingQueryResponse;
            return FinTsScaTransition.QueryRecorded;
        }
    }

    public FinTsScaTransition AcceptStatusResponse(FinTsTanChallengeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return _state == FinTsScaState.Ready ? FinTsScaTransition.WrongState : FinTsScaTransition.Terminal; }
            var initial = _initial!;
            if (Same(Dialog(evidence.Response.Source.Frame), Dialog(initial.Response.Source.Frame)) && evidence.Response.Source.Frame.MessageNumber <= _lastBankNumber)
            { return FinTsScaTransition.ReplayRejected; }
            if (_state != FinTsScaState.AwaitingQueryResponse) { return FinTsScaTransition.WrongState; }
            if (!ReferenceEquals(evidence.Request, _pending) || !ReferenceEquals(evidence.Parameters, initial.Parameters) ||
                !ReferenceEquals(evidence.Procedure, initial.Procedure) || !ReferenceEquals(evidence.Permissions, initial.Permissions))
            { return FinTsScaTransition.ContextMismatch; }
            if (evidence.Issues != FinTsTanContextIssue.None || evidence.Observation is not (FinTsTanOutcomeObservation.DecoupledPendingReported or FinTsTanOutcomeObservation.ExecutionReported))
            { Close(FinTsScaState.NeedsReview); return FinTsScaTransition.RejectedForReview; }
            _lastClientNumber = _pending!.Frame.MessageNumber;
            _lastBankNumber = evidence.Response.Source.Frame.MessageNumber;
            _pending = null;
            if (evidence.Observation == FinTsTanOutcomeObservation.ExecutionReported)
            { Close(FinTsScaState.ExecutionReported); return FinTsScaTransition.ExecutionReported; }
            if (_lastClientNumber == 9999 || _lastBankNumber == 9999)
            { Close(FinTsScaState.CounterExhausted); return FinTsScaTransition.Terminal; }
            if (_queries == _maximumQueries) { Close(FinTsScaState.QueryLimitReached); return FinTsScaTransition.Terminal; }
            _challenge = evidence.Challenge;
            _queryNotBefore = _elapsed + TimeSpan.FromSeconds(initial.Procedure.SubsequentQueryDelaySeconds!.Value);
            _state = FinTsScaState.WaitingToQuery;
            return FinTsScaTransition.PendingAccepted;
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsScaState.Ready) { Close(FinTsScaState.Cancelled); } } }
    /// <summary>Terminal local abandonment, for example a transport timeout or disconnect. No automatic retry.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsScaState.Ready) { Close(FinTsScaState.Stopped); } } }

    private bool IsActive => _state is FinTsScaState.AwaitingUserContinuation or FinTsScaState.AwaitingExternalNotification or
        FinTsScaState.WaitingToQuery or FinTsScaState.AwaitingQueryResponse;
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        long now = _clock.GetTimestamp();
        if (now < _lastTimestamp) { Close(FinTsScaState.ClockInvalid); return; }
        _lastTimestamp = now;
        _elapsed = _clock.GetElapsedTime(_startedAt, now);
        if (_elapsed < TimeSpan.Zero) { Close(FinTsScaState.ClockInvalid); }
        else if (_elapsed >= _lifetime) { Close(FinTsScaState.TimedOut); }
    }
    private void Close(FinTsScaState state)
    {
        _state = state;
        _initial = null;
        _challenge = null;
        _pending = null;
    }
    private static FinTsDataElement Dialog(FinTsMessageFrame frame) => frame.Syntax.Segments[0].Fields[2].Elements[0];
    private static bool SameOptional(FinTsDataElement? left, FinTsDataElement? right) =>
        (left is null || left.IsEmpty) && (right is null || right.IsEmpty) || Same(left, right);
    private static bool Same(FinTsDataElement? left, FinTsDataElement? right) => left is not null && right is not null && left.IsBinary == right.IsBinary &&
        left.CopyValueBytes().AsSpan().SequenceEqual(right.CopyValueBytes());
}
