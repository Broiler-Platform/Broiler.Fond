namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Stage of an assembled FinTS 3.0 PIN/TAN synchronization dialogue.</summary>
public enum FinTsPinTanSynchronizationSessionStage
{
    Synchronization = 1,
    Closing = 2,
    Terminal = 3,
}

/// <summary>State of an assembled FinTS 3.0 PIN/TAN synchronization session attempt.</summary>
public enum FinTsPinTanSynchronizationSessionState
{
    Ready,
    AwaitingSynchronizationResponse,
    AwaitingCloseRequest,
    AwaitingCloseResponse,
    ReinitializationRequired,
    Aborted,
    NeedsReview,
    Cancelled,
    TimedOut,
    Stopped,
    ClockInvalid,
}

/// <summary>Transitions produced by an assembled PIN/TAN synchronization session attempt.</summary>
public enum FinTsPinTanSynchronizationSessionTransition
{
    CandidateRecorded,
    CloseRequired,
    CloseCandidateRecorded,
    ReinitializationRequired,
    AbortReported,
    RejectedForReview,
    ContextMismatch,
    ReplayRejected,
    WrongState,
    Terminal,
}

/// <summary>Scalar diagnostic issues for an assembled PIN/TAN synchronization session attempt.</summary>
[Flags]
public enum FinTsPinTanSynchronizationSessionIssue
{
    None = 0,
    DialogueIdMismatch = 1 << 0,
    MessageNumberMismatch = 1 << 1,
    ReplayDetected = 1 << 2,
    BindingNeedsReview = 1 << 3,
    SynchronizationNeedsReview = 1 << 4,
    ClosingBindingNeedsReview = 1 << 5,
    ClosingStatusNeedsReview = 1 << 6,
    WrongState = 1 << 7,
    TimeoutExceeded = 1 << 8,
    Cancelled = 1 << 9,
    Stopped = 1 << 10,
    ClockFailure = 1 << 11,
    StageOutOfSequence = 1 << 12,
    AbortReported = 1 << 13,
}

/// <summary>Caller-owned evidence container produced by an assembled PIN/TAN synchronization session attempt.</summary>
public sealed class FinTsPinTanSynchronizationSessionResult
{
    internal FinTsPinTanSynchronizationSessionResult(
        FinTsPinTanSynchronizationSessionTransition transition,
        FinTsPinTanSynchronizationSessionStage stage,
        FinTsPinTanSynchronizationEvidence? synchronizationEvidence = null,
        FinTsPinTanDialogueEndEvidence? closingEvidence = null)
    {
        Transition = transition;
        Stage = stage;
        SynchronizationEvidence = synchronizationEvidence;
        ClosingEvidence = closingEvidence;
    }

    public FinTsPinTanSynchronizationSessionTransition Transition { get; }
    public FinTsPinTanSynchronizationSessionStage Stage { get; }
    public FinTsPinTanSynchronizationEvidence? SynchronizationEvidence { get; }
    public FinTsPinTanDialogueEndEvidence? ClosingEvidence { get; }
}

/// <summary>Scalar-only diagnostics snapshot for an assembled PIN/TAN synchronization session attempt.</summary>
public sealed class FinTsPinTanSynchronizationSessionSnapshot
{
    internal FinTsPinTanSynchronizationSessionSnapshot(
        FinTsPinTanSynchronizationSessionState state,
        FinTsPinTanSynchronizationSessionStage stage,
        int requestsRecorded,
        int responsesHandled,
        int clientMessageNumber,
        int bankMessageNumber,
        TimeSpan remainingTime,
        FinTsPinTanSynchronizationSessionIssue issues,
        FinTsPinTanSynchronizationIssue synchronizationIssues,
        FinTsSynchronizationIssue synchronizationDetails,
        FinTsPinTanResponseBindingIssue bindingIssues,
        FinTsPinTanDialogueEndIssue closingIssues,
        FinTsPinTanResponseBindingIssue closingBindingIssues)
    {
        State = state;
        Stage = stage;
        RequestsRecorded = requestsRecorded;
        ResponsesHandled = responsesHandled;
        ClientMessageNumber = clientMessageNumber;
        BankMessageNumber = bankMessageNumber;
        RemainingTime = remainingTime;
        Issues = issues;
        SynchronizationIssues = synchronizationIssues;
        SynchronizationDetails = synchronizationDetails;
        BindingIssues = bindingIssues;
        ClosingIssues = closingIssues;
        ClosingBindingIssues = closingBindingIssues;
    }

    public FinTsPinTanSynchronizationSessionState State { get; }
    public FinTsPinTanSynchronizationSessionStage Stage { get; }
    public int RequestsRecorded { get; }
    public int ResponsesHandled { get; }
    public int ClientMessageNumber { get; }
    public int BankMessageNumber { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanSynchronizationSessionIssue Issues { get; }
    public FinTsPinTanSynchronizationIssue SynchronizationIssues { get; }
    public FinTsSynchronizationIssue SynchronizationDetails { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsPinTanDialogueEndIssue ClosingIssues { get; }
    public FinTsPinTanResponseBindingIssue ClosingBindingIssues { get; }
}

/// <summary>
/// Coordinates a two-stage assembled FinTS 3.0 PIN/TAN synchronization dialogue (HKSYN followed by HKEND)
/// under a single monotonic deadline.
/// </summary>
public sealed class FinTsPinTanSynchronizationSessionAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;

    private FinTsPinTanSynchronizationSessionState _state;
    private FinTsPinTanSynchronizationSessionStage _stage;

    private FinTsPinTanRequestBinding? _pendingSynchronization;
    private FinTsSynchronizationRecoveryContext? _recovery;
    private FinTsPinTanDialogueEndRequestBinding? _pendingClosing;

    private string? _dialogueId;
    private int _lastClientNumber;
    private int _lastBankNumber;

    private int _requestsRecorded;
    private int _responsesHandled;

    private long _startedAt;
    private long _lastTimestamp;
    private TimeSpan _elapsed;

    private FinTsPinTanSynchronizationSessionIssue _issues;
    private FinTsPinTanSynchronizationIssue _synchronizationIssues;
    private FinTsSynchronizationIssue _synchronizationDetails;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsPinTanDialogueEndIssue _closingIssues;
    private FinTsPinTanResponseBindingIssue _closingBindingIssues;

    private FinTsPinTanSynchronizationEvidence? _synchronizationEvidence;
    private FinTsPinTanDialogueEndEvidence? _closingEvidence;

    public FinTsPinTanSynchronizationSessionAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be positive and not exceed fifteen minutes.");
        }

        _clock = clock ?? TimeProvider.System;
        _lifetime = lifetime;

        bool clockValid;
        try { clockValid = _clock.TimestampFrequency > 0; }
        catch (Exception) { clockValid = false; }

        if (!clockValid)
        {
            throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock));
        }

        _state = FinTsPinTanSynchronizationSessionState.Ready;
        _stage = FinTsPinTanSynchronizationSessionStage.Synchronization;
    }

    public FinTsPinTanSynchronizationSessionSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(
                _state,
                _stage,
                _requestsRecorded,
                _responsesHandled,
                _lastClientNumber,
                _lastBankNumber,
                IsActive ? _lifetime - _elapsed : TimeSpan.Zero,
                _issues,
                _synchronizationIssues,
                _synchronizationDetails,
                _bindingIssues,
                _closingIssues,
                _closingBindingIssues);
        }
    }

    /// <summary>
    /// Starts the synchronization dialogue with client message 1 (HKSYN). Begins the single monotonic deadline.
    /// </summary>
    public FinTsPinTanSynchronizationSessionTransition StartSynchronization(
        FinTsPinTanRequestBinding candidate,
        FinTsSynchronizationRecoveryContext? recoveryContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock();
            CheckCancellation(cancellationToken);

            if (_state != FinTsPinTanSynchronizationSessionState.Ready)
            {
                return IsActive ? FinTsPinTanSynchronizationSessionTransition.WrongState : FinTsPinTanSynchronizationSessionTransition.Terminal;
            }

            if (candidate.SignatureEvidence.Request.Synchronization is null)
            {
                _issues = FinTsPinTanSynchronizationSessionIssue.SynchronizationNeedsReview;
                _synchronizationIssues = FinTsPinTanSynchronizationIssue.RequestKindMismatch;
                Close(FinTsPinTanSynchronizationSessionState.NeedsReview);
                return FinTsPinTanSynchronizationSessionTransition.RejectedForReview;
            }

            if (candidate.MessageNumber != 1)
            {
                _issues = FinTsPinTanSynchronizationSessionIssue.MessageNumberMismatch;
                Close(FinTsPinTanSynchronizationSessionState.NeedsReview);
                return FinTsPinTanSynchronizationSessionTransition.RejectedForReview;
            }

            var profile = candidate.ProfileVersion == 1 ? FinTsSynchronizationProfile.PinTan1 : FinTsSynchronizationProfile.PinTan2;
            var reqIssues = FinTsSynchronizationEvidence.RequestIssues(candidate.SignatureEvidence.Request.Synchronization, profile, recoveryContext);
            if (reqIssues != FinTsSynchronizationIssue.None)
            {
                _issues = FinTsPinTanSynchronizationSessionIssue.SynchronizationNeedsReview;
                _synchronizationIssues = FinTsPinTanSynchronizationIssue.SynchronizationNeedsReview;
                _synchronizationDetails = reqIssues;
                Close(FinTsPinTanSynchronizationSessionState.NeedsReview);
                return FinTsPinTanSynchronizationSessionTransition.RejectedForReview;
            }

            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception)
            {
                if (_state == FinTsPinTanSynchronizationSessionState.Ready)
                {
                    Close(FinTsPinTanSynchronizationSessionState.ClockInvalid);
                }
                return FinTsPinTanSynchronizationSessionTransition.Terminal;
            }

            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanSynchronizationSessionState.Ready)
            {
                return FinTsPinTanSynchronizationSessionTransition.Terminal;
            }

            _pendingSynchronization = candidate;
            _recovery = recoveryContext;
            _requestsRecorded = 1;
            _lastClientNumber = 1;
            _state = FinTsPinTanSynchronizationSessionState.AwaitingSynchronizationResponse;
            _stage = FinTsPinTanSynchronizationSessionStage.Synchronization;

            ObserveClock();
            CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanSynchronizationSessionTransition.CandidateRecorded : FinTsPinTanSynchronizationSessionTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the synchronization request (bank message 1).</summary>
    public FinTsPinTanSynchronizationSessionResult AcceptSynchronizationResponse(
        FinTsSynchronizationDataSet data,
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
                    return new(
                        _state == FinTsPinTanSynchronizationSessionState.Ready
                            ? FinTsPinTanSynchronizationSessionTransition.WrongState
                            : FinTsPinTanSynchronizationSessionTransition.Terminal,
                        _stage);
                }

                if (_lastBankNumber > 0 &&
                    _dialogueId is not null &&
                    FinTsReadContextEvidence.Dialog(data.Source.Frame) == _dialogueId &&
                    data.Source.Frame.MessageNumber <= _lastBankNumber)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.ReplayDetected;
                    return new(FinTsPinTanSynchronizationSessionTransition.ReplayRejected, _stage);
                }

                if (_state != FinTsPinTanSynchronizationSessionState.AwaitingSynchronizationResponse)
                {
                    return new(FinTsPinTanSynchronizationSessionTransition.WrongState, _stage);
                }

                var binding = FinTsPinTanResponseBinding.Evaluate(_pendingSynchronization!, data.Source, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationSessionTransition.Terminal, _stage); }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.BindingNeedsReview;
                    _bindingIssues = binding.Issues;
                    _synchronizationIssues = FinTsPinTanSynchronizationIssue.None;
                    _synchronizationDetails = FinTsSynchronizationIssue.None;
                    return new(FinTsPinTanSynchronizationSessionTransition.ContextMismatch, _stage);
                }

                var evidence = FinTsPinTanSynchronizationEvidence.Evaluate(binding, data, _recovery, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationSessionTransition.Terminal, _stage); }

                _bindingIssues = binding.Issues;
                _synchronizationIssues = evidence.Issues;
                _synchronizationDetails = evidence.SynchronizationIssues;

                // Check for bank abort (9800)
                bool isAbort = data.Source.ReplySegments.Any(static s => s.Replies.Any(static r => r.Code == "9800"));
                if (isAbort)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.AbortReported;
                    _responsesHandled++;
                    _synchronizationEvidence = evidence;
                    _pendingSynchronization = null;
                    _recovery = null;
                    Close(FinTsPinTanSynchronizationSessionState.Aborted);
                    return new(FinTsPinTanSynchronizationSessionTransition.AbortReported, _stage, evidence);
                }

                if (!evidence.HasMatchingEvidence)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.SynchronizationNeedsReview;
                    _responsesHandled++;
                    _synchronizationEvidence = evidence;
                    _pendingSynchronization = null;
                    _recovery = null;
                    Close(FinTsPinTanSynchronizationSessionState.NeedsReview);
                    return new(FinTsPinTanSynchronizationSessionTransition.RejectedForReview, _stage, evidence);
                }

                // Successful synchronization response
                _dialogueId = FinTsReadContextEvidence.Dialog(data.Source.Frame);
                _lastBankNumber = data.Source.Frame.MessageNumber;
                _synchronizationEvidence = evidence;
                _responsesHandled++;
                _pendingSynchronization = null;
                _recovery = null;

                _state = FinTsPinTanSynchronizationSessionState.AwaitingCloseRequest;
                _stage = FinTsPinTanSynchronizationSessionStage.Closing;
                _issues = FinTsPinTanSynchronizationSessionIssue.None;

                return new(FinTsPinTanSynchronizationSessionTransition.CloseRequired, _stage, evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanSynchronizationSessionState.Ready)
                {
                    Close(FinTsPinTanSynchronizationSessionState.Cancelled);
                }
                throw;
            }
        }
    }

    /// <summary>Evaluates bank response for the synchronization request (bank message 1) directly from a FinTsResponse.</summary>
    public FinTsPinTanSynchronizationSessionResult AcceptSynchronizationResponse(
        FinTsResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return AcceptSynchronizationResponse(FinTsSynchronizationDataSet.Parse(response), cancellationToken);
    }

    /// <summary>
    /// Records the dialogue termination candidate (HKEND) with sequential client and expected bank counters.
    /// </summary>
    public FinTsPinTanSynchronizationSessionTransition RecordClosingCandidate(
        FinTsPinTanDialogueEndRequestBinding candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock();
            CheckCancellation(cancellationToken);

            if (!IsActive) { return FinTsPinTanSynchronizationSessionTransition.Terminal; }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.ReplayDetected;
                return FinTsPinTanSynchronizationSessionTransition.ReplayRejected;
            }

            if (_state != FinTsPinTanSynchronizationSessionState.AwaitingCloseRequest)
            {
                return FinTsPinTanSynchronizationSessionTransition.WrongState;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.DialogueIdMismatch;
                return FinTsPinTanSynchronizationSessionTransition.ContextMismatch;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 ||
                candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.MessageNumberMismatch;
                return FinTsPinTanSynchronizationSessionTransition.ContextMismatch;
            }

            if (!candidate.Context.HasMatchingEvidence ||
                !ReferenceEquals(candidate.Context.SynchronizationEvidence, _synchronizationEvidence))
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.ClosingBindingNeedsReview;
                return FinTsPinTanSynchronizationSessionTransition.ContextMismatch;
            }

            _pendingClosing = candidate;
            _lastClientNumber = candidate.MessageNumber;
            _requestsRecorded++;
            _state = FinTsPinTanSynchronizationSessionState.AwaitingCloseResponse;

            ObserveClock();
            CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded : FinTsPinTanSynchronizationSessionTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the closing request (bank message 2).</summary>
    public FinTsPinTanSynchronizationSessionResult AcceptClosingResponse(
        FinTsResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock();
                CheckCancellation(cancellationToken);

                if (!IsActive) { return new(FinTsPinTanSynchronizationSessionTransition.Terminal, _stage); }

                if (_lastBankNumber > 0 &&
                    _dialogueId is not null &&
                    FinTsReadContextEvidence.Dialog(response.Frame) == _dialogueId &&
                    response.Frame.MessageNumber <= _lastBankNumber)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.ReplayDetected;
                    return new(FinTsPinTanSynchronizationSessionTransition.ReplayRejected, _stage);
                }

                if (_state != FinTsPinTanSynchronizationSessionState.AwaitingCloseResponse)
                {
                    return new(FinTsPinTanSynchronizationSessionTransition.WrongState, _stage);
                }

                var binding = FinTsPinTanDialogueEndResponseBinding.Evaluate(_pendingClosing!, response, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationSessionTransition.Terminal, _stage); }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.ClosingBindingNeedsReview;
                    _closingBindingIssues = binding.Issues;
                    return new(FinTsPinTanSynchronizationSessionTransition.ContextMismatch, _stage);
                }

                var evidence = FinTsPinTanDialogueEndEvidence.Evaluate(binding, cancellationToken);
                ObserveClock();
                CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanSynchronizationSessionTransition.Terminal, _stage); }

                _closingIssues = evidence.Issues;
                _closingBindingIssues = binding.Issues;
                // Preserve the caller's handoff before terminal cleanup releases session ownership.
                var synchronizationEvidence = _synchronizationEvidence;

                if (evidence.Outcome == FinTsDialogueEndOutcome.ClosureReported)
                {
                    _lastBankNumber = response.Frame.MessageNumber;
                    _closingEvidence = evidence;
                    _responsesHandled++;
                    _pendingClosing = null;
                    Close(FinTsPinTanSynchronizationSessionState.ReinitializationRequired);
                    return new(
                        FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired,
                        _stage,
                        synchronizationEvidence,
                        evidence);
                }

                if (evidence.Outcome == FinTsDialogueEndOutcome.AbortReported)
                {
                    _issues |= FinTsPinTanSynchronizationSessionIssue.AbortReported;
                    _closingEvidence = evidence;
                    _responsesHandled++;
                    _pendingClosing = null;
                    Close(FinTsPinTanSynchronizationSessionState.Aborted);
                    return new(
                        FinTsPinTanSynchronizationSessionTransition.AbortReported,
                        _stage,
                        synchronizationEvidence,
                        evidence);
                }

                // Unresolved / review
                _issues |= FinTsPinTanSynchronizationSessionIssue.ClosingStatusNeedsReview;
                _closingEvidence = evidence;
                _responsesHandled++;
                _pendingClosing = null;
                Close(FinTsPinTanSynchronizationSessionState.NeedsReview);
                return new(
                    FinTsPinTanSynchronizationSessionTransition.RejectedForReview,
                    _stage,
                    synchronizationEvidence,
                    evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanSynchronizationSessionState.Ready)
                {
                    Close(FinTsPinTanSynchronizationSessionState.Cancelled);
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
            if (IsActive || _state == FinTsPinTanSynchronizationSessionState.Ready)
            {
                Close(FinTsPinTanSynchronizationSessionState.Cancelled);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanSynchronizationSessionState.Ready)
            {
                Close(FinTsPinTanSynchronizationSessionState.Stopped);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private bool IsActive =>
        _state is FinTsPinTanSynchronizationSessionState.AwaitingSynchronizationResponse or
                  FinTsPinTanSynchronizationSessionState.AwaitingCloseRequest or
                  FinTsPinTanSynchronizationSessionState.AwaitingCloseResponse;

    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanSynchronizationSessionState.Ready)
        {
            Close(FinTsPinTanSynchronizationSessionState.Cancelled);
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
                _issues |= FinTsPinTanSynchronizationSessionIssue.ClockFailure;
                Close(FinTsPinTanSynchronizationSessionState.ClockInvalid);
                return;
            }
            // Convert once from the original start; summing rounded intervals can lose time.
            var elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            _lastTimestamp = now;
            _elapsed = elapsed;
            if (_elapsed < TimeSpan.Zero)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.ClockFailure;
                Close(FinTsPinTanSynchronizationSessionState.ClockInvalid);
            }
            else if (_elapsed >= _lifetime)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.TimeoutExceeded;
                Close(FinTsPinTanSynchronizationSessionState.TimedOut);
            }
        }
        catch (Exception)
        {
            // A clock callback may already have stopped/cancelled the attempt.
            if (IsActive)
            {
                _issues |= FinTsPinTanSynchronizationSessionIssue.ClockFailure;
                Close(FinTsPinTanSynchronizationSessionState.ClockInvalid);
            }
        }
    }

    private void Close(FinTsPinTanSynchronizationSessionState terminal)
    {
        _state = terminal;
        _stage = FinTsPinTanSynchronizationSessionStage.Terminal;
        _pendingSynchronization = null;
        _recovery = null;
        _pendingClosing = null;
        _synchronizationEvidence = null;
        _closingEvidence = null;
        _dialogueId = null;
    }
}
