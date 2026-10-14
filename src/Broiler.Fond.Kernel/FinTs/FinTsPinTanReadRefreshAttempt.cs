using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanReadRefreshState
{
    Ready,
    AwaitingResponse,
    WaitingToContinue,
    ExecutionReported,
    UnavailableReported,
    NeedsReview,
    PageLimitReached,
    CounterExhausted,
    Cancelled,
    TimedOut,
    Stopped,
    ClockInvalid,
}

public enum FinTsPinTanReadRefreshTransition
{
    CandidateRecorded,
    PartialAccepted,
    ExecutionReported,
    UnavailableReported,
    ReplayRejected,
    ContextMismatch,
    RejectedForReview,
    WrongState,
    Terminal,
}

public sealed class FinTsPinTanReadRefreshResult
{
    internal FinTsPinTanReadRefreshResult(
        FinTsPinTanReadRefreshTransition transition,
        FinTsPinTanReadEvidence? firstPageEvidence = null,
        FinTsPinTanReadContinuationEvidence? continuationEvidence = null,
        IReadOnlyList<FinTsBalanceReport>? aggregatedBalances = null)
    {
        Transition = transition;
        FirstPageEvidence = firstPageEvidence;
        ContinuationEvidence = continuationEvidence;
        AggregatedBalances = aggregatedBalances is null
            ? ReadOnlyCollection<FinTsBalanceReport>.Empty
            : new ReadOnlyCollection<FinTsBalanceReport>(aggregatedBalances.ToList());
    }

    public FinTsPinTanReadRefreshTransition Transition { get; }
    public FinTsPinTanReadEvidence? FirstPageEvidence { get; }
    public FinTsPinTanReadContinuationEvidence? ContinuationEvidence { get; }
    public ReadOnlyCollection<FinTsBalanceReport> AggregatedBalances { get; }
    public int PageNumber => ContinuationEvidence?.PageNumber ?? (FirstPageEvidence is not null ? 1 : 0);
    public bool HasMatchingEvidence => (FirstPageEvidence?.HasMatchingEvidence ?? false) || (ContinuationEvidence?.HasMatchingEvidence ?? false);
}

/// <summary>Scalar lifecycle diagnostics only; no account, amount, continuation token or credential references.</summary>
public sealed class FinTsPinTanReadRefreshSnapshot
{
    internal FinTsPinTanReadRefreshSnapshot(
        FinTsPinTanReadRefreshState state,
        int requestsRecorded,
        int pagesAccepted,
        int maximumPages,
        int balancesReported,
        TimeSpan remainingTime,
        FinTsPinTanReadIssue issues,
        FinTsPinTanResponseBindingIssue bindingIssues,
        FinTsReadContextIssue readIssues,
        FinTsPinTanReadContinuationIssue continuationIssues)
    {
        State = state;
        RequestsRecorded = requestsRecorded;
        PagesAccepted = pagesAccepted;
        MaximumPages = maximumPages;
        BalancesReported = balancesReported;
        RemainingTime = remainingTime;
        Issues = issues;
        BindingIssues = bindingIssues;
        ReadIssues = readIssues;
        ContinuationIssues = continuationIssues;
    }

    public FinTsPinTanReadRefreshState State { get; }
    public int RequestsRecorded { get; }
    public int PagesAccepted { get; }
    public int MaximumPages { get; }
    public int BalancesReported { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanReadIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsReadContextIssue ReadIssues { get; }
    public FinTsPinTanReadContinuationIssue ContinuationIssues { get; }
}

/// <summary>
/// Session-level multi-page PIN/TAN balance refresh coordinator across page 1 and continuation pages.
/// Enforces single lifetime deadline, page budget, sequential counters, and collects balance reports
/// without double-counting. Disposal releases pending request metadata without modifying caller-owned evidence.
/// </summary>
public sealed class FinTsPinTanReadRefreshAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumPages;
    private FinTsPinTanReadRefreshState _state;
    private FinTsPinTanReadRequestBinding? _pendingFirstRead;
    private FinTsPinTanReadContinuationRequestBinding? _pendingContinuation;
    private FinTsPinTanReadEvidence? _firstPageEvidence;
    private readonly List<FinTsPinTanReadContinuationEvidence> _continuationPages = [];
    private readonly List<FinTsBalanceReport> _balances = [];
    private string? _dialogueId;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _requests, _pages, _lastClientNumber, _lastBankNumber;
    private FinTsPinTanReadIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsReadContextIssue _readIssues;
    private FinTsPinTanReadContinuationIssue _continuationIssues;

    public FinTsPinTanReadRefreshAttempt(TimeSpan lifetime, int maximumPages = 16, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        if (maximumPages is < 1 or > FinTsReadContextEvidence.MaximumPages) { throw new ArgumentOutOfRangeException(nameof(maximumPages)); }
        _clock = clock ?? TimeProvider.System;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
        _lifetime = lifetime;
        _maximumPages = maximumPages;
    }

    public FinTsPinTanReadRefreshSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(
                _state,
                _requests,
                _pages,
                _maximumPages,
                _balances.Count,
                IsActive ? _lifetime - _elapsed : TimeSpan.Zero,
                _issues,
                _bindingIssues,
                _readIssues,
                _continuationIssues);
        }
    }

    public FinTsPinTanReadEvidence? FirstPageEvidence { get { lock (_gate) return _firstPageEvidence; } }
    public ReadOnlyCollection<FinTsPinTanReadContinuationEvidence> ContinuationPages { get { lock (_gate) return _continuationPages.AsReadOnly(); } }
    public FinTsPinTanReadContinuationEvidence? LastContinuationEvidence { get { lock (_gate) return _continuationPages.Count > 0 ? _continuationPages[^1] : null; } }
    public ReadOnlyCollection<FinTsBalanceReport> Balances { get { lock (_gate) return _balances.AsReadOnly(); } }

    public string? CurrentContinuationToken
    {
        get
        {
            lock (_gate)
            {
                ObserveClock();
                if (_state != FinTsPinTanReadRefreshState.WaitingToContinue) { return null; }
                return _continuationPages.Count > 0
                    ? _continuationPages[^1].ReportedContinuationToken
                    : _firstPageEvidence?.ReportedContinuationToken;
            }
        }
    }

    internal object? PendingCandidate
    {
        get
        {
            lock (_gate)
            {
                return (object?)_pendingFirstRead ?? _pendingContinuation;
            }
        }
    }

    /// <summary>Records the initial first-read candidate. The absolute refresh deadline starts here.</summary>
    public FinTsPinTanReadRefreshTransition Start(FinTsPinTanReadRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadRefreshState.Ready)
            {
                return IsActive ? FinTsPinTanReadRefreshTransition.WrongState : FinTsPinTanReadRefreshTransition.Terminal;
            }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception)
            {
                if (_state == FinTsPinTanReadRefreshState.Ready) { Close(FinTsPinTanReadRefreshState.ClockInvalid); }
                return FinTsPinTanReadRefreshTransition.Terminal;
            }
            CheckCancellation(cancellationToken);
            if (_state != FinTsPinTanReadRefreshState.Ready) { return FinTsPinTanReadRefreshTransition.Terminal; }

            if (!candidate.Context.HasMatchingEvidence)
            {
                _issues = FinTsPinTanReadIssue.BindingNeedsReview;
                _readIssues = FinTsReadContextIssue.None;
                Close(FinTsPinTanReadRefreshState.NeedsReview);
                return FinTsPinTanReadRefreshTransition.RejectedForReview;
            }

            _dialogueId = candidate.DialogueId;
            _pendingFirstRead = candidate;
            _requests = 1;
            _state = FinTsPinTanReadRefreshState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadRefreshTransition.CandidateRecorded : FinTsPinTanReadRefreshTransition.Terminal;
        }
    }

    /// <summary>Records the explicit continuation candidate for the next page. Does not extend the absolute deadline.</summary>
    public FinTsPinTanReadRefreshTransition RecordContinuation(FinTsPinTanReadContinuationRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (!IsActive) { return InactiveTransition; }
            if (_state != FinTsPinTanReadRefreshState.WaitingToContinue)
            {
                return FinTsPinTanReadRefreshTransition.WrongState;
            }

            if (candidate.DialogueId != _dialogueId)
            {
                return FinTsPinTanReadRefreshTransition.ContextMismatch;
            }

            if (candidate.MessageNumber <= _lastClientNumber || candidate.ExpectedBankMessageNumber <= _lastBankNumber)
            {
                return FinTsPinTanReadRefreshTransition.ReplayRejected;
            }

            if (candidate.MessageNumber != _lastClientNumber + 1 || candidate.ExpectedBankMessageNumber != _lastBankNumber + 1)
            {
                return FinTsPinTanReadRefreshTransition.ContextMismatch;
            }

            var contContext = candidate.Context.Continuation;
            if (!candidate.Context.HasMatchingEvidence || !contContext.HasMatchingEvidence)
            {
                _issues = candidate.Context.Issues != FinTsPinTanReadContinuationSignatureIssue.None
                    ? FinTsPinTanReadIssue.BindingNeedsReview
                    : FinTsPinTanReadIssue.None;
                _continuationIssues = contContext.Issues;
                _readIssues = contContext.RequestIssues;
                return FinTsPinTanReadRefreshTransition.ContextMismatch;
            }

            if (_pages == 1)
            {
                if (contContext.Previous is null)
                {
                    return FinTsPinTanReadRefreshTransition.ContextMismatch;
                }
                bool originMatch = ReferenceEquals(contContext.Origin, _firstPageEvidence!.Request.Context) ||
                    (contContext.Origin.Signature.Request.Request.Source.Version == _firstPageEvidence.Request.Context.Signature.Request.Request.Source.Version &&
                     FinTsReadContextEvidence.SameAccount(contContext.Origin.Signature.Request.Request.Accounts[0], _firstPageEvidence.Request.Context.Signature.Request.Request.Accounts[0]));
                bool match = originMatch &&
                    (ReferenceEquals(contContext.Previous, _firstPageEvidence) ||
                     (contContext.Previous.ReportedContinuationToken == _firstPageEvidence.ReportedContinuationToken &&
                      contContext.Previous.Response.Frame.MessageNumber == _firstPageEvidence.Response.Frame.MessageNumber));
                if (!match)
                {
                    return FinTsPinTanReadRefreshTransition.ContextMismatch;
                }
            }
            else
            {
                if (contContext.PreviousContinuation is null)
                {
                    return FinTsPinTanReadRefreshTransition.ContextMismatch;
                }
                var lastCont = _continuationPages[^1];
                bool originMatch = ReferenceEquals(contContext.Origin, _firstPageEvidence!.Request.Context) ||
                    (contContext.Origin.Signature.Request.Request.Source.Version == _firstPageEvidence.Request.Context.Signature.Request.Request.Source.Version &&
                     FinTsReadContextEvidence.SameAccount(contContext.Origin.Signature.Request.Request.Accounts[0], _firstPageEvidence.Request.Context.Signature.Request.Request.Accounts[0]));
                bool match = originMatch &&
                    (ReferenceEquals(contContext.PreviousContinuation, lastCont) ||
                     (contContext.PreviousContinuation.ReportedContinuationToken == lastCont.ReportedContinuationToken &&
                      contContext.PreviousContinuation.Response.Frame.MessageNumber == lastCont.Response.Frame.MessageNumber));
                if (!match)
                {
                    return FinTsPinTanReadRefreshTransition.ContextMismatch;
                }
            }

            if (contContext.PageNumber != _pages + 1)
            {
                return FinTsPinTanReadRefreshTransition.ContextMismatch;
            }

            if (contContext.Request.Request.ContinuationToken != CurrentContinuationToken)
            {
                return FinTsPinTanReadRefreshTransition.ContextMismatch;
            }

            _pendingContinuation = candidate;
            _requests++;
            _state = FinTsPinTanReadRefreshState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsPinTanReadRefreshTransition.CandidateRecorded : FinTsPinTanReadRefreshTransition.Terminal;
        }
    }

    /// <summary>Evaluates bank response for the currently pending first-read or continuation candidate.</summary>
    public FinTsPinTanReadRefreshResult AcceptResponse(FinTsReadDataSet response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive)
                {
                    return new(_state == FinTsPinTanReadRefreshState.Ready
                        ? FinTsPinTanReadRefreshTransition.WrongState
                        : FinTsPinTanReadRefreshTransition.Terminal);
                }

                if (_pages > 0 && FinTsReadContextEvidence.Dialog(response.Source.Frame) == _dialogueId && response.Source.Frame.MessageNumber <= _lastBankNumber)
                {
                    return new(FinTsPinTanReadRefreshTransition.ReplayRejected);
                }

                if (_state != FinTsPinTanReadRefreshState.AwaitingResponse)
                {
                    return new(FinTsPinTanReadRefreshTransition.WrongState);
                }

                const FinTsPinTanResponseBindingIssue foreignScope =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                if (_pendingFirstRead is not null)
                {
                    var binding = FinTsPinTanReadResponseBinding.Evaluate(_pendingFirstRead, response.Source, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadRefreshTransition.Terminal); }
                    if ((binding.Issues & foreignScope) != 0)
                    {
                        _issues = FinTsPinTanReadIssue.BindingNeedsReview;
                        _bindingIssues = binding.Issues;
                        _readIssues = FinTsReadContextIssue.None;
                        return new(FinTsPinTanReadRefreshTransition.ContextMismatch);
                    }

                    var evidence = FinTsPinTanReadEvidence.Evaluate(binding, response, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadRefreshTransition.Terminal); }

                    _issues = evidence.Issues;
                    _bindingIssues = binding.Issues;
                    _readIssues = evidence.ReadIssues;

                    if (!evidence.HasMatchingEvidence)
                    {
                        Close(FinTsPinTanReadRefreshState.NeedsReview);
                        return new(FinTsPinTanReadRefreshTransition.RejectedForReview, firstPageEvidence: evidence);
                    }

                    _pages++;
                    _lastClientNumber = _pendingFirstRead.MessageNumber;
                    _lastBankNumber = response.Source.Frame.MessageNumber;
                    _firstPageEvidence = evidence;
                    _pendingFirstRead = null;

                    foreach (var balance in response.Balances)
                    {
                        _balances.Add(balance);
                    }

                    return HandleOutcome(evidence.Outcome, firstPage: evidence, continuation: null);
                }
                else if (_pendingContinuation is not null)
                {
                    var binding = FinTsPinTanReadContinuationResponseBinding.Evaluate(_pendingContinuation, response.Source, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadRefreshTransition.Terminal); }
                    if ((binding.Issues & foreignScope) != 0)
                    {
                        _issues = FinTsPinTanReadIssue.BindingNeedsReview;
                        _bindingIssues = binding.Issues;
                        _readIssues = FinTsReadContextIssue.None;
                        return new(FinTsPinTanReadRefreshTransition.ContextMismatch);
                    }

                    var evidence = FinTsPinTanReadContinuationEvidence.Evaluate(binding, response, cancellationToken);
                    ObserveClock(); CheckCancellation(cancellationToken);
                    if (!IsActive) { return new(FinTsPinTanReadRefreshTransition.Terminal); }

                    _issues = evidence.Issues;
                    _bindingIssues = binding.Issues;
                    _readIssues = evidence.ReadIssues;
                    _continuationIssues = evidence.Request.Context.Continuation.Issues;

                    if (!evidence.HasMatchingEvidence)
                    {
                        Close(FinTsPinTanReadRefreshState.NeedsReview);
                        return new(FinTsPinTanReadRefreshTransition.RejectedForReview, continuationEvidence: evidence);
                    }

                    _pages++;
                    _lastClientNumber = _pendingContinuation.MessageNumber;
                    _lastBankNumber = response.Source.Frame.MessageNumber;
                    _continuationPages.Add(evidence);
                    _pendingContinuation = null;

                    foreach (var balance in response.Balances)
                    {
                        _balances.Add(balance);
                    }

                    return HandleOutcome(evidence.Outcome, firstPage: null, continuation: evidence);
                }
                else
                {
                    return new(FinTsPinTanReadRefreshTransition.WrongState);
                }
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsPinTanReadRefreshState.Ready)
                {
                    Close(FinTsPinTanReadRefreshState.Cancelled);
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
            if (IsActive || _state == FinTsPinTanReadRefreshState.Ready)
            {
                Close(FinTsPinTanReadRefreshState.Cancelled);
            }
        }
    }

    /// <summary>Abandon after transport failure, timeout or disconnect. No automatic retries.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            ObserveClock();
            if (IsActive || _state == FinTsPinTanReadRefreshState.Ready)
            {
                Close(FinTsPinTanReadRefreshState.Stopped);
            }
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private FinTsPinTanReadRefreshResult HandleOutcome(
        FinTsReadOutcomeObservation outcome,
        FinTsPinTanReadEvidence? firstPage,
        FinTsPinTanReadContinuationEvidence? continuation)
    {
        if (outcome == FinTsReadOutcomeObservation.ExecutionReported)
        {
            Close(FinTsPinTanReadRefreshState.ExecutionReported);
            return new(FinTsPinTanReadRefreshTransition.ExecutionReported, firstPage, continuation, _balances);
        }

        if (outcome == FinTsReadOutcomeObservation.UnavailableReported)
        {
            Close(FinTsPinTanReadRefreshState.UnavailableReported);
            return new(FinTsPinTanReadRefreshTransition.UnavailableReported, firstPage, continuation, _balances);
        }

        if (outcome == FinTsReadOutcomeObservation.PartialReported)
        {
            if (_lastClientNumber == 9999 || _lastBankNumber == 9999)
            {
                Close(FinTsPinTanReadRefreshState.CounterExhausted);
                return new(FinTsPinTanReadRefreshTransition.Terminal, firstPage, continuation, _balances);
            }
            if (_pages >= _maximumPages)
            {
                Close(FinTsPinTanReadRefreshState.PageLimitReached);
                return new(FinTsPinTanReadRefreshTransition.Terminal, firstPage, continuation, _balances);
            }

            _state = FinTsPinTanReadRefreshState.WaitingToContinue;
            return new(FinTsPinTanReadRefreshTransition.PartialAccepted, firstPage, continuation, _balances);
        }

        Close(FinTsPinTanReadRefreshState.NeedsReview);
        return new(FinTsPinTanReadRefreshTransition.RejectedForReview, firstPage, continuation, _balances);
    }

    private bool IsActive => _state is FinTsPinTanReadRefreshState.AwaitingResponse or FinTsPinTanReadRefreshState.WaitingToContinue;
    private FinTsPinTanReadRefreshTransition InactiveTransition => _state == FinTsPinTanReadRefreshState.Ready
        ? FinTsPinTanReadRefreshTransition.WrongState
        : FinTsPinTanReadRefreshTransition.Terminal;

    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsPinTanReadRefreshState.Ready)
        {
            Close(FinTsPinTanReadRefreshState.Cancelled);
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
            if (now < _lastTimestamp) { Close(FinTsPinTanReadRefreshState.ClockInvalid); return; }
            _lastTimestamp = now;
            _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsPinTanReadRefreshState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsPinTanReadRefreshState.TimedOut); }
        }
        catch (Exception)
        {
            if (IsActive) { Close(FinTsPinTanReadRefreshState.ClockInvalid); }
        }
    }

    private void Close(FinTsPinTanReadRefreshState state)
    {
        _state = state;
        _pendingFirstRead = null;
        _pendingContinuation = null;
    }
}
