using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanReadSessionState
{
    Ready,
    AwaitingInitializationResponse,
    DialogueOpen,
    AwaitingAllAccountResponse,
    AwaitingBalanceFirstReadResponse,
    WaitingForBalanceContinuation,
    AwaitingBalanceContinuationResponse,
    OperationCompleted,
    PageLimitReached,
    AwaitingClosingResponse,
    Closed,
    Aborted,
    NeedsReview,
    Cancelled,
    TimedOut,
    Stopped,
    ClockInvalid,
}

public enum FinTsPinTanReadSessionStage
{
    Initialization,
    Operation,
    Closing,
    Terminal,
}

public enum FinTsPinTanReadSessionTransition
{
    CandidateRecorded,
    DialogueOpen,
    PartialAccepted,
    ExecutionReported,
    UnavailableReported,
    Closed,
    AbortReported,
    ContextMismatch,
    ReplayRejected,
    RejectedForReview,
    WrongState,
    Terminal,
}

[Flags]
public enum FinTsPinTanReadSessionIssue
{
    None = 0,
    BindingNeedsReview = 1 << 0,
    InitializationNeedsReview = 1 << 1,
    RequirementsNeedReview = 1 << 2,
    ProceduresNeedReview = 1 << 3,
    OperationNeedsReview = 1 << 4,
    ClosingNeedsReview = 1 << 5,
    DialogueIdMismatch = 1 << 6,
    MessageNumberMismatch = 1 << 7,
    ReplayDetected = 1 << 8,
    PageLimitReached = 1 << 9,
    StageOutOfSequence = 1 << 10,
    EvidenceSourceMismatch = 1 << 11,
}

public sealed class FinTsPinTanReadSessionResult
{
    internal FinTsPinTanReadSessionResult(
        FinTsPinTanReadSessionTransition transition,
        FinTsPinTanReadSessionStage stage,
        FinTsPinTanInitializationRequirementsEvidence? requirementsEvidence = null,
        FinTsPinTanAllAccountEvidence? allAccountEvidence = null,
        FinTsPinTanReadEvidence? balanceFirstPageEvidence = null,
        FinTsPinTanReadContinuationEvidence? balanceContinuationEvidence = null,
        IReadOnlyList<FinTsBalanceReport>? aggregatedBalances = null,
        FinTsPinTanDialogueEndEvidence? closingEvidence = null)
    {
        Transition = transition;
        Stage = stage;
        RequirementsEvidence = requirementsEvidence;
        AllAccountEvidence = allAccountEvidence;
        BalanceFirstPageEvidence = balanceFirstPageEvidence;
        BalanceContinuationEvidence = balanceContinuationEvidence;
        AggregatedBalances = aggregatedBalances is null
            ? ReadOnlyCollection<FinTsBalanceReport>.Empty
            : new ReadOnlyCollection<FinTsBalanceReport>(aggregatedBalances.ToList());
        ClosingEvidence = closingEvidence;
    }

    public FinTsPinTanReadSessionTransition Transition { get; }
    public FinTsPinTanReadSessionStage Stage { get; }
    public FinTsPinTanInitializationRequirementsEvidence? RequirementsEvidence { get; }
    public FinTsPinTanAllAccountEvidence? AllAccountEvidence { get; }
    public FinTsPinTanReadEvidence? BalanceFirstPageEvidence { get; }
    public FinTsPinTanReadContinuationEvidence? BalanceContinuationEvidence { get; }
    public ReadOnlyCollection<FinTsBalanceReport> AggregatedBalances { get; }
    public FinTsPinTanDialogueEndEvidence? ClosingEvidence { get; }
    public bool HasMatchingEvidence =>
        (RequirementsEvidence?.HasMatchingEvidence ?? false) ||
        (AllAccountEvidence?.HasMatchingEvidence ?? false) ||
        (BalanceFirstPageEvidence?.HasMatchingEvidence ?? false) ||
        (BalanceContinuationEvidence?.HasMatchingEvidence ?? false) ||
        (ClosingEvidence?.HasMatchingEvidence ?? false);
}

/// <summary>Scalar-only lifecycle diagnostics; no accounts, credentials, continuation tokens, or response payloads.</summary>
public sealed class FinTsPinTanReadSessionSnapshot
{
    internal FinTsPinTanReadSessionSnapshot(
        FinTsPinTanReadSessionState state,
        FinTsPinTanReadSessionStage stage,
        int requestsRecorded,
        int responsesHandled,
        int clientMessageNumber,
        int bankMessageNumber,
        int pagesAccepted,
        int balancesReported,
        int accountsDiscovered,
        TimeSpan remainingTime,
        FinTsPinTanReadSessionIssue issues,
        FinTsPinTanResponseBindingIssue bindingIssues,
        FinTsPinTanInitializationIssue initializationIssues,
        FinTsPinTanInitializationRequirementsIssue requirementsIssues,
        FinTsReadContextIssue readIssues,
        FinTsPinTanReadContinuationIssue continuationIssues,
        FinTsPinTanAllAccountCapabilityIssue allAccountCapabilityIssues,
        FinTsPinTanAllAccountIssue allAccountIssues,
        FinTsPinTanDialogueEndIssue closingIssues)
    {
        State = state;
        Stage = stage;
        RequestsRecorded = requestsRecorded;
        ResponsesHandled = responsesHandled;
        ClientMessageNumber = clientMessageNumber;
        BankMessageNumber = bankMessageNumber;
        PagesAccepted = pagesAccepted;
        BalancesReported = balancesReported;
        AccountsDiscovered = accountsDiscovered;
        RemainingTime = remainingTime;
        Issues = issues;
        BindingIssues = bindingIssues;
        InitializationIssues = initializationIssues;
        RequirementsIssues = requirementsIssues;
        ReadIssues = readIssues;
        ContinuationIssues = continuationIssues;
        AllAccountCapabilityIssues = allAccountCapabilityIssues;
        AllAccountIssues = allAccountIssues;
        ClosingIssues = closingIssues;
    }

    public FinTsPinTanReadSessionState State { get; }
    public FinTsPinTanReadSessionStage Stage { get; }
    public int RequestsRecorded { get; }
    public int ResponsesHandled { get; }
    public int ClientMessageNumber { get; }
    public int BankMessageNumber { get; }
    public int PagesAccepted { get; }
    public int BalancesReported { get; }
    public int AccountsDiscovered { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanReadSessionIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsPinTanInitializationIssue InitializationIssues { get; }
    public FinTsPinTanInitializationRequirementsIssue RequirementsIssues { get; }
    public FinTsReadContextIssue ReadIssues { get; }
    public FinTsPinTanReadContinuationIssue ContinuationIssues { get; }
    public FinTsPinTanAllAccountCapabilityIssue AllAccountCapabilityIssues { get; }
    public FinTsPinTanAllAccountIssue AllAccountIssues { get; }
    public FinTsPinTanDialogueEndIssue ClosingIssues { get; }
}

/// <summary>
/// Top-level coordinator for an assembled PIN/TAN read session across initialization, operation (all-account discovery or balance refresh),
/// and closing stages. Enforces a single absolute deadline, strict dialogue ID pinning, monotonic message counters, and replay defenses.
/// Releases retained candidates, evidence and dialogue data on every terminal transition.
/// Caller-owned results and collection snapshots remain usable after cleanup.
/// Build operation and closing candidates from this session's accepted initialization,
/// and continuation candidates from the exact accepted predecessor evidence.
/// </summary>
public sealed class FinTsPinTanReadSessionAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumBalancePages;

    private FinTsPinTanReadSessionState _state;
    private FinTsPinTanReadSessionStage _stage;

    private FinTsPinTanRequestBinding? _pendingInitialization;
    private FinTsPinTanAllAccountRequestBinding? _pendingAllAccount;
    private FinTsPinTanReadRequestBinding? _pendingBalanceFirstRead;
    private FinTsPinTanReadContinuationRequestBinding? _pendingBalanceContinuation;
    private FinTsPinTanDialogueEndRequestBinding? _pendingClosing;

    private FinTsPinTanInitializationRequirementsEvidence? _requirementsEvidence;
    private FinTsPinTanInitializationProcedureEvidence? _procedureEvidence;
    private FinTsPinTanInitializationEvidence? _initializationEvidence;
    private FinTsPinTanAllAccountEvidence? _allAccountEvidence;
    private FinTsPinTanReadEvidence? _balanceFirstPageEvidence;
    private readonly List<FinTsPinTanReadContinuationEvidence> _balanceContinuationPages = [];
    private readonly List<FinTsBalanceReport> _balances = [];
    private FinTsPinTanDialogueEndEvidence? _closingEvidence;

    private string? _dialogueId;
    private int _lastClientNumber;
    private int _lastBankNumber;
    private int _requestsRecorded;
    private int _responsesHandled;
    private int _balancePages;
    private int _balancesReported;
    private int _accountsDiscovered;

    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;

    private FinTsPinTanReadSessionIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsPinTanInitializationIssue _initializationIssues;
    private FinTsPinTanInitializationRequirementsIssue _requirementsIssues;
    private FinTsReadContextIssue _readIssues;
    private FinTsPinTanReadContinuationIssue _continuationIssues;
    private FinTsPinTanAllAccountCapabilityIssue _allAccountCapabilityIssues;
    private FinTsPinTanAllAccountIssue _allAccountIssues;
    private FinTsPinTanDialogueEndIssue _closingIssues;

    public FinTsPinTanReadSessionAttempt(TimeSpan lifetime, int maximumBalancePages = 16, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        if (maximumBalancePages is < 1 or > FinTsReadContextEvidence.MaximumPages) { throw new ArgumentOutOfRangeException(nameof(maximumBalancePages)); }
        _clock = clock ?? TimeProvider.System;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
        _lifetime = lifetime;
        _maximumBalancePages = maximumBalancePages;
        _state = FinTsPinTanReadSessionState.Ready;
        _stage = FinTsPinTanReadSessionStage.Initialization;
    }

    public FinTsPinTanReadSessionSnapshot GetSnapshot()
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
                _balancePages,
                _balancesReported,
                _accountsDiscovered,
                IsActive ? _lifetime - _elapsed : TimeSpan.Zero,
                _issues,
                _bindingIssues,
                _initializationIssues,
                _requirementsIssues,
                _readIssues,
                _continuationIssues,
                _allAccountCapabilityIssues,
                _allAccountIssues,
                _closingIssues);
        }
    }

    public string? DialogueId { get { lock (_gate) return _dialogueId; } }
    public FinTsPinTanInitializationRequirementsEvidence? RequirementsEvidence { get { lock (_gate) return _requirementsEvidence; } }
    public FinTsPinTanInitializationProcedureEvidence? ProcedureEvidence { get { lock (_gate) return _procedureEvidence; } }
    public FinTsPinTanInitializationEvidence? InitializationEvidence { get { lock (_gate) return _initializationEvidence; } }
    public FinTsPinTanAllAccountEvidence? AllAccountEvidence { get { lock (_gate) return _allAccountEvidence; } }
    public FinTsPinTanReadEvidence? BalanceFirstPageEvidence { get { lock (_gate) return _balanceFirstPageEvidence; } }
    /// <summary>A caller-owned snapshot of accepted continuation pages, empty after termination.</summary>
    public ReadOnlyCollection<FinTsPinTanReadContinuationEvidence> BalanceContinuationPages { get { lock (_gate) return _balanceContinuationPages.ToList().AsReadOnly(); } }
    /// <summary>A caller-owned snapshot of accepted balances, empty after termination.</summary>
    public ReadOnlyCollection<FinTsBalanceReport> AggregatedBalances { get { lock (_gate) return _balances.ToList().AsReadOnly(); } }
    /// <summary>Terminal cleanup releases this reference. Closing evidence is handed out in the closing result.</summary>
    public FinTsPinTanDialogueEndEvidence? ClosingEvidence { get { lock (_gate) return _closingEvidence; } }

    public string? CurrentContinuationToken
    {
        get
        {
            lock (_gate)
            {
                ObserveClock();
                if (_state != FinTsPinTanReadSessionState.WaitingForBalanceContinuation) { return null; }
                return _balanceContinuationPages.Count > 0
                    ? _balanceContinuationPages[^1].ReportedContinuationToken
                    : _balanceFirstPageEvidence?.ReportedContinuationToken;
            }
        }
    }

    internal object? PendingCandidate
    {
        get
        {
            lock (_gate)
            {
                return (object?)_pendingInitialization ??
                       (object?)_pendingAllAccount ??
                       (object?)_pendingBalanceFirstRead ??
                       (object?)_pendingBalanceContinuation ??
                       (object?)_pendingClosing;
            }
        }
    }

    /// <summary>Starts dialogue initialization with an assembled initialization request candidate (client message 1). The absolute session deadline begins here.</summary>
    public FinTsPinTanReadSessionTransition StartInitialization(FinTsPinTanRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadSessionState.Ready)
            {
                return IsActive ? FinTsPinTanReadSessionTransition.WrongState : FinTsPinTanReadSessionTransition.Terminal;
            }

            if (candidate.SignatureEvidence.Request.Initialization is null)
            {
                _issues = FinTsPinTanReadSessionIssue.StageOutOfSequence;
                Close(FinTsPinTanReadSessionState.NeedsReview);
                return FinTsPinTanReadSessionTransition.RejectedForReview;
            }

            if (candidate.MessageNumber != 1)
            {
                _issues = FinTsPinTanReadSessionIssue.MessageNumberMismatch;
                Close(FinTsPinTanReadSessionState.NeedsReview);
                return FinTsPinTanReadSessionTransition.RejectedForReview;
            }

            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception)
            {
                if (_state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.ClockInvalid); }
                return FinTsPinTanReadSessionTransition.Terminal;
            }

            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadSessionState.Ready) { return FinTsPinTanReadSessionTransition.Terminal; }

            _pendingInitialization = candidate;
            _requestsRecorded = 1;
            _lastClientNumber = 1;
            _state = FinTsPinTanReadSessionState.AwaitingInitializationResponse;
            _stage = FinTsPinTanReadSessionStage.Initialization;

            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadSessionTransition.CandidateRecorded : FinTsPinTanReadSessionTransition.Terminal;
        }
    }

    public FinTsPinTanReadSessionResult AcceptInitializationResponse(FinTsParameterSet parameters, CancellationToken cancellationToken = default) =>
        AcceptInitializationCore(parameters, null, null, null, null, cancellationToken);

    public FinTsPinTanReadSessionResult AcceptInitializationResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions);
        return AcceptInitializationCore(procedures.Source, procedures, permissions, null, null, cancellationToken);
    }

    public FinTsPinTanReadSessionResult AcceptInitializationResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        FinTsPinTanParameterSet pinTan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(pinTan);
        return AcceptInitializationCore(procedures.Source, procedures, permissions, pinTan, null, cancellationToken);
    }

    public FinTsPinTanReadSessionResult AcceptInitializationResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        FinTsPinTanParameterSet pinTan, FinTsReadParameterSet reads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(pinTan); ArgumentNullException.ThrowIfNull(reads);
        return AcceptInitializationCore(procedures.Source, procedures, permissions, pinTan, reads, cancellationToken);
    }

    private FinTsPinTanReadSessionResult AcceptInitializationCore(
        FinTsParameterSet parameters,
        FinTsTanParameterSet? procedures,
        FinTsPermittedProcedureSet? permissions,
        FinTsPinTanParameterSet? pinTan,
        FinTsReadParameterSet? reads,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive)
                {
                    return new(_state == FinTsPinTanReadSessionState.Ready
                        ? FinTsPinTanReadSessionTransition.WrongState
                        : FinTsPinTanReadSessionTransition.Terminal, _stage);
                }

                if (_state != FinTsPinTanReadSessionState.AwaitingInitializationResponse)
                {
                    return new(FinTsPinTanReadSessionTransition.WrongState, _stage);
                }

                var binding = FinTsPinTanResponseBinding.Evaluate(_pendingInitialization!, parameters.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues |= FinTsPinTanReadSessionIssue.BindingNeedsReview;
                    _bindingIssues = binding.Issues;
                    return new(FinTsPinTanReadSessionTransition.ContextMismatch, _stage);
                }

                var requirementsEvidence = pinTan is null ? null : FinTsPinTanInitializationRequirementsEvidence.EvaluateCore(binding, procedures!, permissions!, pinTan, reads, cancellationToken);
                var procedureEvidence = requirementsEvidence?.Procedures ?? (procedures is null ? null : FinTsPinTanInitializationProcedureEvidence.Evaluate(binding, procedures, permissions!, cancellationToken));
                var evidence = procedureEvidence?.Initialization ?? FinTsPinTanInitializationEvidence.Evaluate(binding, parameters, cancellationToken);

                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                _bindingIssues = binding.Issues;
                _initializationIssues = evidence.Issues;
                _requirementsIssues = requirementsEvidence?.Issues ?? FinTsPinTanInitializationRequirementsIssue.None;
                bool matching = requirementsEvidence?.HasMatchingEvidence ?? procedureEvidence?.HasMatchingEvidence ?? evidence.HasMatchingEvidence;

                if (!matching)
                {
                    _issues |= FinTsPinTanReadSessionIssue.InitializationNeedsReview;
                    Close(FinTsPinTanReadSessionState.NeedsReview);
                    return new(FinTsPinTanReadSessionTransition.RejectedForReview, _stage, requirementsEvidence: requirementsEvidence);
                }

                _issues = FinTsPinTanReadSessionIssue.None;
                _dialogueId = evidence.ReportedDialogueId;
                _lastBankNumber = parameters.Source.Frame.MessageNumber;
                _requirementsEvidence = requirementsEvidence;
                _procedureEvidence = procedureEvidence;
                _initializationEvidence = evidence;
                _pendingInitialization = null;
                _responsesHandled++;
                _state = FinTsPinTanReadSessionState.DialogueOpen;
                _stage = FinTsPinTanReadSessionStage.Operation;

                return new(FinTsPinTanReadSessionTransition.DialogueOpen, _stage, requirementsEvidence: requirementsEvidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.Cancelled); }
                throw;
            }
        }
    }

    /// <summary>Records an assembled all-account discovery candidate (HKSPA:3:1) for execution in the opened dialogue.</summary>
    public FinTsPinTanReadSessionTransition RecordAllAccountCandidate(FinTsPinTanAllAccountRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (!IsActive) { return FinTsPinTanReadSessionTransition.Terminal; }
            if (_state is not (FinTsPinTanReadSessionState.DialogueOpen or FinTsPinTanReadSessionState.OperationCompleted))
            {
                return FinTsPinTanReadSessionTransition.WrongState;
            }

            if (!candidate.Context.HasMatchingEvidence)
            {
                _allAccountCapabilityIssues = candidate.Context.Issues;
                _issues |= FinTsPinTanReadSessionIssue.OperationNeedsReview;
                Close(FinTsPinTanReadSessionState.NeedsReview);
                return FinTsPinTanReadSessionTransition.RejectedForReview;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                _issues |= FinTsPinTanReadSessionIssue.DialogueIdMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                return FinTsPinTanReadSessionTransition.ReplayRejected;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 || candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                _issues |= FinTsPinTanReadSessionIssue.MessageNumberMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (_requirementsEvidence is null || !ReferenceEquals(candidate.Context.Signature.Initialization, _requirementsEvidence))
            {
                _issues |= FinTsPinTanReadSessionIssue.EvidenceSourceMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            _pendingAllAccount = candidate;
            _requestsRecorded++;
            _state = FinTsPinTanReadSessionState.AwaitingAllAccountResponse;
            _stage = FinTsPinTanReadSessionStage.Operation;

            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadSessionTransition.CandidateRecorded : FinTsPinTanReadSessionTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the currently pending all-account discovery request.</summary>
    public FinTsPinTanReadSessionResult AcceptAllAccountResponse(FinTsReadDataSet response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }
                if (_lastBankNumber > 0 && FinTsReadContextEvidence.Dialog(response.Source.Frame) == _dialogueId && response.Source.Frame.MessageNumber <= _lastBankNumber)
                {
                    _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                    return new(FinTsPinTanReadSessionTransition.ReplayRejected, _stage);
                }

                if (_state != FinTsPinTanReadSessionState.AwaitingAllAccountResponse)
                {
                    return new(FinTsPinTanReadSessionTransition.WrongState, _stage);
                }

                var binding = FinTsPinTanAllAccountResponseBinding.Evaluate(_pendingAllAccount!, response.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues |= FinTsPinTanReadSessionIssue.BindingNeedsReview;
                    _bindingIssues = binding.Issues;
                    return new(FinTsPinTanReadSessionTransition.ContextMismatch, _stage);
                }

                var evidence = FinTsPinTanAllAccountEvidence.Evaluate(binding, response, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                _allAccountIssues = evidence.Issues;
                _bindingIssues = binding.Issues;

                if (!evidence.HasMatchingEvidence)
                {
                    _issues |= FinTsPinTanReadSessionIssue.OperationNeedsReview;
                    Close(FinTsPinTanReadSessionState.NeedsReview);
                    return new(FinTsPinTanReadSessionTransition.RejectedForReview, _stage, allAccountEvidence: evidence);
                }

                _issues = FinTsPinTanReadSessionIssue.None;
                _allAccountEvidence = evidence;
                _accountsDiscovered = evidence.Accounts.Count;
                _lastClientNumber = _pendingAllAccount!.MessageNumber;
                _lastBankNumber = response.Source.Frame.MessageNumber;
                _pendingAllAccount = null;
                _responsesHandled++;
                _state = FinTsPinTanReadSessionState.OperationCompleted;
                _stage = FinTsPinTanReadSessionStage.Closing;

                var transition = evidence.Outcome == FinTsReadOutcomeObservation.ExecutionReported
                    ? FinTsPinTanReadSessionTransition.ExecutionReported
                    : FinTsPinTanReadSessionTransition.UnavailableReported;

                return new(transition, _stage, allAccountEvidence: evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.Cancelled); }
                throw;
            }
        }
    }

    /// <summary>Records the initial balance first-read candidate (HKSAL) for execution in the opened dialogue.</summary>
    public FinTsPinTanReadSessionTransition RecordBalanceFirstReadCandidate(FinTsPinTanReadRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (!IsActive) { return FinTsPinTanReadSessionTransition.Terminal; }
            if (_state is not (FinTsPinTanReadSessionState.DialogueOpen or FinTsPinTanReadSessionState.OperationCompleted))
            {
                return FinTsPinTanReadSessionTransition.WrongState;
            }

            if (!candidate.Context.HasMatchingEvidence)
            {
                _issues |= FinTsPinTanReadSessionIssue.OperationNeedsReview;
                Close(FinTsPinTanReadSessionState.NeedsReview);
                return FinTsPinTanReadSessionTransition.RejectedForReview;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                _issues |= FinTsPinTanReadSessionIssue.DialogueIdMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                return FinTsPinTanReadSessionTransition.ReplayRejected;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 || candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                _issues |= FinTsPinTanReadSessionIssue.MessageNumberMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (_requirementsEvidence is null || !ReferenceEquals(candidate.Context.Signature.Initialization, _requirementsEvidence))
            {
                _issues |= FinTsPinTanReadSessionIssue.EvidenceSourceMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            _pendingBalanceFirstRead = candidate;
            _requestsRecorded++;
            _state = FinTsPinTanReadSessionState.AwaitingBalanceFirstReadResponse;
            _stage = FinTsPinTanReadSessionStage.Operation;

            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadSessionTransition.CandidateRecorded : FinTsPinTanReadSessionTransition.Terminal;
        }
    }

    /// <summary>Records the continuation candidate for subsequent pages in a multi-page balance refresh.</summary>
    public FinTsPinTanReadSessionTransition RecordBalanceContinuationCandidate(FinTsPinTanReadContinuationRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (!IsActive) { return FinTsPinTanReadSessionTransition.Terminal; }
            if (_state != FinTsPinTanReadSessionState.WaitingForBalanceContinuation)
            {
                return FinTsPinTanReadSessionTransition.WrongState;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                _issues |= FinTsPinTanReadSessionIssue.DialogueIdMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                return FinTsPinTanReadSessionTransition.ReplayRejected;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 || candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                _issues |= FinTsPinTanReadSessionIssue.MessageNumberMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            var contContext = candidate.Context.Continuation;
            if (!candidate.Context.HasMatchingEvidence || !contContext.HasMatchingEvidence)
            {
                _continuationIssues = contContext.Issues;
                _readIssues = contContext.RequestIssues;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            // Equal account, token and counter values do not establish session provenance.
            bool originMatch = ReferenceEquals(contContext.Origin, _balanceFirstPageEvidence!.Request.Context);
            bool predecessorMatch = _balancePages == 1
                ? ReferenceEquals(contContext.Previous, _balanceFirstPageEvidence)
                : ReferenceEquals(contContext.PreviousContinuation, _balanceContinuationPages[^1]);
            if (!originMatch || !predecessorMatch)
            {
                _issues |= FinTsPinTanReadSessionIssue.EvidenceSourceMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            string? token = _balancePages == 1
                ? _balanceFirstPageEvidence.ReportedContinuationToken
                : _balanceContinuationPages[^1].ReportedContinuationToken;
            if (contContext.PageNumber != _balancePages + 1 || contContext.Request.Request.ContinuationToken != token)
            {
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            _pendingBalanceContinuation = candidate;
            _requestsRecorded++;
            _state = FinTsPinTanReadSessionState.AwaitingBalanceContinuationResponse;
            _stage = FinTsPinTanReadSessionStage.Operation;

            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadSessionTransition.CandidateRecorded : FinTsPinTanReadSessionTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the currently pending balance first-read or continuation request.</summary>
    public FinTsPinTanReadSessionResult AcceptBalanceResponse(FinTsReadDataSet response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }
                if (_lastBankNumber > 0 && FinTsReadContextEvidence.Dialog(response.Source.Frame) == _dialogueId && response.Source.Frame.MessageNumber <= _lastBankNumber)
                {
                    _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                    return new(FinTsPinTanReadSessionTransition.ReplayRejected, _stage);
                }

                if (_state is not (FinTsPinTanReadSessionState.AwaitingBalanceFirstReadResponse or FinTsPinTanReadSessionState.AwaitingBalanceContinuationResponse))
                {
                    return new(FinTsPinTanReadSessionTransition.WrongState, _stage);
                }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if (_pendingBalanceFirstRead is not null)
                {
                    var binding = FinTsPinTanReadResponseBinding.Evaluate(_pendingBalanceFirstRead, response.Source, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                    if ((binding.Issues & foreignScope) != 0)
                    {
                        _issues |= FinTsPinTanReadSessionIssue.BindingNeedsReview;
                        _bindingIssues = binding.Issues;
                        return new(FinTsPinTanReadSessionTransition.ContextMismatch, _stage);
                    }

                    var evidence = FinTsPinTanReadEvidence.Evaluate(binding, response, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                    _issues = evidence.Issues != FinTsPinTanReadIssue.None ? _issues | FinTsPinTanReadSessionIssue.OperationNeedsReview : _issues;
                    _bindingIssues = binding.Issues;
                    _readIssues = evidence.ReadIssues;

                    if (!evidence.HasMatchingEvidence)
                    {
                        Close(FinTsPinTanReadSessionState.NeedsReview);
                        return new(FinTsPinTanReadSessionTransition.RejectedForReview, _stage, balanceFirstPageEvidence: evidence);
                    }

                    _balancePages++;
                    _lastClientNumber = _pendingBalanceFirstRead.MessageNumber;
                    _lastBankNumber = response.Source.Frame.MessageNumber;
                    _balanceFirstPageEvidence = evidence;
                    _pendingBalanceFirstRead = null;
                    _responsesHandled++;

                    foreach (var balance in response.Balances) { _balances.Add(balance); }
                    _balancesReported = _balances.Count;

                    if (evidence.Outcome == FinTsReadOutcomeObservation.ExecutionReported)
                    {
                        _state = FinTsPinTanReadSessionState.OperationCompleted;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.ExecutionReported, _stage, balanceFirstPageEvidence: evidence, aggregatedBalances: _balances);
                    }
                    if (evidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported)
                    {
                        _state = FinTsPinTanReadSessionState.OperationCompleted;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.UnavailableReported, _stage, balanceFirstPageEvidence: evidence, aggregatedBalances: _balances);
                    }

                    // Partial outcome
                    if (_balancePages >= _maximumBalancePages)
                    {
                        _issues |= FinTsPinTanReadSessionIssue.PageLimitReached;
                        _state = FinTsPinTanReadSessionState.PageLimitReached;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.ExecutionReported, _stage, balanceFirstPageEvidence: evidence, aggregatedBalances: _balances);
                    }

                    _state = FinTsPinTanReadSessionState.WaitingForBalanceContinuation;
                    return new(FinTsPinTanReadSessionTransition.PartialAccepted, _stage, balanceFirstPageEvidence: evidence, aggregatedBalances: _balances);
                }
                else if (_pendingBalanceContinuation is not null)
                {
                    var binding = FinTsPinTanReadContinuationResponseBinding.Evaluate(_pendingBalanceContinuation, response.Source, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                    if ((binding.Issues & foreignScope) != 0)
                    {
                        _issues |= FinTsPinTanReadSessionIssue.BindingNeedsReview;
                        _bindingIssues = binding.Issues;
                        return new(FinTsPinTanReadSessionTransition.ContextMismatch, _stage);
                    }

                    var evidence = FinTsPinTanReadContinuationEvidence.Evaluate(binding, response, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                    _issues = evidence.Issues != FinTsPinTanReadIssue.None ? _issues | FinTsPinTanReadSessionIssue.OperationNeedsReview : _issues;
                    _bindingIssues = binding.Issues;
                    _readIssues = evidence.ReadIssues;
                    _continuationIssues = evidence.Request.Context.Continuation.Issues;

                    if (!evidence.HasMatchingEvidence)
                    {
                        Close(FinTsPinTanReadSessionState.NeedsReview);
                        return new(FinTsPinTanReadSessionTransition.RejectedForReview, _stage, balanceContinuationEvidence: evidence);
                    }

                    _balancePages++;
                    _lastClientNumber = _pendingBalanceContinuation.MessageNumber;
                    _lastBankNumber = response.Source.Frame.MessageNumber;
                    _balanceContinuationPages.Add(evidence);
                    _pendingBalanceContinuation = null;
                    _responsesHandled++;

                    foreach (var balance in response.Balances) { _balances.Add(balance); }
                    _balancesReported = _balances.Count;

                    if (evidence.Outcome == FinTsReadOutcomeObservation.ExecutionReported)
                    {
                        _state = FinTsPinTanReadSessionState.OperationCompleted;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.ExecutionReported, _stage, balanceContinuationEvidence: evidence, aggregatedBalances: _balances);
                    }
                    if (evidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported)
                    {
                        _state = FinTsPinTanReadSessionState.OperationCompleted;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.UnavailableReported, _stage, balanceContinuationEvidence: evidence, aggregatedBalances: _balances);
                    }

                    // Partial outcome
                    if (_balancePages >= _maximumBalancePages)
                    {
                        _issues |= FinTsPinTanReadSessionIssue.PageLimitReached;
                        _state = FinTsPinTanReadSessionState.PageLimitReached;
                        _stage = FinTsPinTanReadSessionStage.Closing;
                        return new(FinTsPinTanReadSessionTransition.ExecutionReported, _stage, balanceContinuationEvidence: evidence, aggregatedBalances: _balances);
                    }

                    _state = FinTsPinTanReadSessionState.WaitingForBalanceContinuation;
                    return new(FinTsPinTanReadSessionTransition.PartialAccepted, _stage, balanceContinuationEvidence: evidence, aggregatedBalances: _balances);
                }
                else
                {
                    return new(FinTsPinTanReadSessionTransition.WrongState, _stage);
                }
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.Cancelled); }
                throw;
            }
        }
    }

    /// <summary>Records the dialogue termination candidate (HKEND) with sequential client and expected bank counters.</summary>
    public FinTsPinTanReadSessionTransition RecordClosingCandidate(FinTsPinTanDialogueEndRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (!IsActive) { return FinTsPinTanReadSessionTransition.Terminal; }
            if (_state is not (FinTsPinTanReadSessionState.DialogueOpen or
                              FinTsPinTanReadSessionState.OperationCompleted or
                              FinTsPinTanReadSessionState.PageLimitReached or
                              FinTsPinTanReadSessionState.WaitingForBalanceContinuation))
            {
                return FinTsPinTanReadSessionTransition.WrongState;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                _issues |= FinTsPinTanReadSessionIssue.DialogueIdMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                return FinTsPinTanReadSessionTransition.ReplayRejected;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 || candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                _issues |= FinTsPinTanReadSessionIssue.MessageNumberMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            if (_initializationEvidence is null || !ReferenceEquals(candidate.Context.Initialization, _initializationEvidence))
            {
                _issues |= FinTsPinTanReadSessionIssue.EvidenceSourceMismatch;
                return FinTsPinTanReadSessionTransition.ContextMismatch;
            }

            _pendingClosing = candidate;
            _requestsRecorded++;
            _state = FinTsPinTanReadSessionState.AwaitingClosingResponse;
            _stage = FinTsPinTanReadSessionStage.Closing;

            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadSessionTransition.CandidateRecorded : FinTsPinTanReadSessionTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the currently pending closing request.</summary>
    public FinTsPinTanReadSessionResult AcceptClosingResponse(FinTsResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }
                if (_lastBankNumber > 0 && FinTsReadContextEvidence.Dialog(response.Frame) == _dialogueId && response.Frame.MessageNumber <= _lastBankNumber)
                {
                    _issues |= FinTsPinTanReadSessionIssue.ReplayDetected;
                    return new(FinTsPinTanReadSessionTransition.ReplayRejected, _stage);
                }

                if (_state != FinTsPinTanReadSessionState.AwaitingClosingResponse)
                {
                    return new(FinTsPinTanReadSessionTransition.WrongState, _stage);
                }

                var binding = FinTsPinTanDialogueEndResponseBinding.Evaluate(_pendingClosing!, response, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues |= FinTsPinTanReadSessionIssue.BindingNeedsReview;
                    _bindingIssues = binding.Issues;
                    return new(FinTsPinTanReadSessionTransition.ContextMismatch, _stage);
                }

                var evidence = FinTsPinTanDialogueEndEvidence.Evaluate(binding, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsPinTanReadSessionTransition.Terminal, _stage); }

                _closingIssues = evidence.Issues;
                _bindingIssues = binding.Issues;
                _closingEvidence = evidence;
                _lastClientNumber = _pendingClosing!.MessageNumber;
                _lastBankNumber = response.Frame.MessageNumber;
                _pendingClosing = null;

                if (evidence.Outcome == FinTsDialogueEndOutcome.ClosureReported)
                {
                    _issues = FinTsPinTanReadSessionIssue.None;
                    _responsesHandled++;
                    Close(FinTsPinTanReadSessionState.Closed);
                    return new(FinTsPinTanReadSessionTransition.Closed, _stage, closingEvidence: evidence);
                }
                if (evidence.Outcome == FinTsDialogueEndOutcome.AbortReported)
                {
                    _issues = FinTsPinTanReadSessionIssue.None;
                    _responsesHandled++;
                    Close(FinTsPinTanReadSessionState.Aborted);
                    return new(FinTsPinTanReadSessionTransition.AbortReported, _stage, closingEvidence: evidence);
                }

                _issues |= FinTsPinTanReadSessionIssue.ClosingNeedsReview;
                Close(FinTsPinTanReadSessionState.NeedsReview);
                return new(FinTsPinTanReadSessionTransition.RejectedForReview, _stage, closingEvidence: evidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanReadSessionState.Ready)
            {
                Close(FinTsPinTanReadSessionState.Cancelled);
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanReadSessionState.Ready)
            {
                Close(FinTsPinTanReadSessionState.Stopped);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private bool IsActive => _state is
        FinTsPinTanReadSessionState.AwaitingInitializationResponse or
        FinTsPinTanReadSessionState.DialogueOpen or
        FinTsPinTanReadSessionState.AwaitingAllAccountResponse or
        FinTsPinTanReadSessionState.AwaitingBalanceFirstReadResponse or
        FinTsPinTanReadSessionState.WaitingForBalanceContinuation or
        FinTsPinTanReadSessionState.AwaitingBalanceContinuationResponse or
        FinTsPinTanReadSessionState.OperationCompleted or
        FinTsPinTanReadSessionState.PageLimitReached or
        FinTsPinTanReadSessionState.AwaitingClosingResponse;

    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanReadSessionState.Ready) { Close(FinTsPinTanReadSessionState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }

    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsPinTanReadSessionState.ClockInvalid); return; }
            _lastTimestamp = now;
            _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanReadSessionState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanReadSessionState.TimedOut); }
        }
        catch (Exception)
        {
            if (IsActive) { Close(FinTsPinTanReadSessionState.ClockInvalid); }
        }
    }

    private void Close(FinTsPinTanReadSessionState state)
    {
        _state = state;
        _stage = FinTsPinTanReadSessionStage.Terminal;
        _pendingInitialization = null;
        _pendingAllAccount = null;
        _pendingBalanceFirstRead = null;
        _pendingBalanceContinuation = null;
        _pendingClosing = null;
        _requirementsEvidence = null;
        _procedureEvidence = null;
        _initializationEvidence = null;
        _allAccountEvidence = null;
        _balanceFirstPageEvidence = null;
        _balanceContinuationPages.Clear();
        _balances.Clear();
        _closingEvidence = null;
        _dialogueId = null;
    }
}
