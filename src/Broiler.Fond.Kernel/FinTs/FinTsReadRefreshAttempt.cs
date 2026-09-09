namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsReadRefreshState
{
    Ready, AwaitingResponse, WaitingToContinue, ExecutionReported, Cancelled, TimedOut, Stopped,
    NeedsReview, PageLimitReached, CounterExhausted, ClockInvalid,
    UnavailableReported,
}

public enum FinTsReadRefreshTransition
{
    RequestRecorded, PartialAccepted, ExecutionReported, ReplayRejected, ContextMismatch,
    RejectedForReview, WrongState, Terminal,
    UnavailableReported,
}

/// <summary>Scalar lifecycle diagnostics only; no account, amount or continuation token.</summary>
public sealed class FinTsReadRefreshSnapshot
{
    internal FinTsReadRefreshSnapshot(FinTsReadRefreshState state, int requests, int pages, int maximum, TimeSpan remaining, FinTsReadContextIssue issues)
    { State = state; RequestsRecorded = requests; PagesAccepted = pages; MaximumPages = maximum; RemainingTime = remaining; LastIssues = issues; }
    public FinTsReadRefreshState State { get; }
    public int RequestsRecorded { get; }
    public int PagesAccepted { get; }
    public int MaximumPages { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsReadContextIssue LastIssues { get; }
}

/// <summary>One synchronized synthetic read attempt. Never sends, authenticates, aggregates or persists bank data.</summary>
public sealed class FinTsReadRefreshAttempt
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly int _maximumPages;
    private FinTsReadRefreshState _state;
    private FinTsReadRequestContext? _pending;
    private FinTsReadCapabilityEvidence? _capability;
    private FinTsReadAdvertisement? _nationalAdvertisement;
    private FinTsReadContextEvidence? _previousPage;
    private string? _dialog;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _requests, _pages, _lastClientNumber, _lastBankNumber;
    private FinTsReadContextIssue _lastIssues;

    public FinTsReadRefreshAttempt(TimeSpan lifetime, int maximumPages = 16, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        if (maximumPages is < 1 or > FinTsReadContextEvidence.MaximumPages) { throw new ArgumentOutOfRangeException(nameof(maximumPages)); }
        _clock = clock ?? TimeProvider.System;
        if (_clock.TimestampFrequency <= 0) { throw new ArgumentException("A positive timestamp frequency is required.", nameof(clock)); }
        _lifetime = lifetime;
        _maximumPages = maximumPages;
    }

    public FinTsReadRefreshSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _requests, _pages, _maximumPages, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _lastIssues);
        }
    }

    /// <summary>Untrusted last partial page, exposed only while waiting for explicit continuation. Terminal states release it.</summary>
    public FinTsReadContextEvidence? ContinuationEvidence
    { get { lock (_gate) { ObserveClock(); return _state == FinTsReadRefreshState.WaitingToContinue ? _previousPage : null; } } }

    public FinTsReadRefreshTransition Start(FinTsReadRequestContext request, FinTsReadCapabilityEvidence capability,
        FinTsReadAdvertisement? nationalAdvertisement = null)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(capability);
        lock (_gate)
        {
            ObserveClock();
            if (_state != FinTsReadRefreshState.Ready) { return IsActive ? FinTsReadRefreshTransition.WrongState : FinTsReadRefreshTransition.Terminal; }
            _lastIssues = FinTsReadContextEvidence.RequestIssues(request, capability, nationalAdvertisement) |
                FinTsReadContextEvidence.ContinuationIssues(request, capability, nationalAdvertisement, null, out _);
            if (_lastIssues != FinTsReadContextIssue.None) { Close(FinTsReadRefreshState.NeedsReview); return FinTsReadRefreshTransition.RejectedForReview; }
            _startedAt = _lastTimestamp = _clock.GetTimestamp();
            _capability = capability;
            _nationalAdvertisement = nationalAdvertisement;
            _dialog = FinTsReadContextEvidence.Dialog(request.Frame);
            _pending = request;
            _requests = 1;
            _state = FinTsReadRefreshState.AwaitingResponse;
            return FinTsReadRefreshTransition.RequestRecorded;
        }
    }

    /// <summary>Records the caller's next synthetic request. No automatic continuation or permission to transmit.</summary>
    public FinTsReadRefreshTransition RecordContinuation(FinTsReadRequestContext request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return InactiveTransition; }
            if (_state != FinTsReadRefreshState.WaitingToContinue) { return FinTsReadRefreshTransition.WrongState; }
            if (FinTsReadContextEvidence.Dialog(request.Frame) == _dialog &&
                (request.Frame.MessageNumber <= _lastClientNumber || request.ExpectedBankMessageNumber <= _lastBankNumber))
            { return FinTsReadRefreshTransition.ReplayRejected; }
            var issues = FinTsReadContextEvidence.RequestIssues(request, _capability!, _nationalAdvertisement) |
                FinTsReadContextEvidence.ContinuationIssues(request, _capability!, _nationalAdvertisement, _previousPage, out _);
            ObserveClock();
            if (!IsActive) { return FinTsReadRefreshTransition.Terminal; }
            if (issues != FinTsReadContextIssue.None) { _lastIssues = issues; return FinTsReadRefreshTransition.ContextMismatch; }
            _lastIssues = FinTsReadContextIssue.None;
            _pending = request;
            _requests++;
            _state = FinTsReadRefreshState.AwaitingResponse;
            return FinTsReadRefreshTransition.RequestRecorded;
        }
    }

    /// <summary>Recomputes evidence against the internally pending request and last accepted page. Consumes a matching page once per instance.</summary>
    public FinTsReadRefreshTransition AcceptResponse(FinTsReadDataSet response)
    {
        ArgumentNullException.ThrowIfNull(response);
        lock (_gate)
        {
            ObserveClock();
            if (!IsActive) { return InactiveTransition; }
            if (_pages > 0 && FinTsReadContextEvidence.Dialog(response.Source.Frame) == _dialog && response.Source.Frame.MessageNumber <= _lastBankNumber)
            { return FinTsReadRefreshTransition.ReplayRejected; }
            if (_state != FinTsReadRefreshState.AwaitingResponse) { return FinTsReadRefreshTransition.WrongState; }
            var evidence = FinTsReadContextEvidence.Evaluate(_pending!, response, _capability!, _nationalAdvertisement, _previousPage);
            // Validation time counts toward the same deadline as waiting time.
            ObserveClock();
            if (!IsActive) { return FinTsReadRefreshTransition.Terminal; }
            _lastIssues = evidence.Issues;
            // An unrelated response does not consume or replace the pending request.
            if ((evidence.Issues & (FinTsReadContextIssue.MessageMismatch | FinTsReadContextIssue.ReferenceMismatch)) != 0)
            { return FinTsReadRefreshTransition.ContextMismatch; }
            if (!evidence.HasMatchingEvidence)
            { Close(FinTsReadRefreshState.NeedsReview); return FinTsReadRefreshTransition.RejectedForReview; }
            _pages++;
            _lastClientNumber = _pending!.Frame.MessageNumber;
            _lastBankNumber = response.Source.Frame.MessageNumber;
            _pending = null;
            if (evidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported)
            { Close(FinTsReadRefreshState.UnavailableReported); return FinTsReadRefreshTransition.UnavailableReported; }
            if (evidence.Outcome == FinTsReadOutcomeObservation.ExecutionReported)
            { Close(FinTsReadRefreshState.ExecutionReported); return FinTsReadRefreshTransition.ExecutionReported; }
            if (_lastClientNumber == 9999 || _lastBankNumber == 9999)
            { Close(FinTsReadRefreshState.CounterExhausted); return FinTsReadRefreshTransition.Terminal; }
            if (_pages >= _maximumPages)
            { Close(FinTsReadRefreshState.PageLimitReached); return FinTsReadRefreshTransition.Terminal; }
            _previousPage = evidence;
            _state = FinTsReadRefreshState.WaitingToContinue;
            return FinTsReadRefreshTransition.PartialAccepted;
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsReadRefreshState.Ready) { Close(FinTsReadRefreshState.Cancelled); } } }
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsReadRefreshState.Ready) { Close(FinTsReadRefreshState.Stopped); } } }

    private bool IsActive => _state is FinTsReadRefreshState.AwaitingResponse or FinTsReadRefreshState.WaitingToContinue;
    private FinTsReadRefreshTransition InactiveTransition => _state == FinTsReadRefreshState.Ready ? FinTsReadRefreshTransition.WrongState : FinTsReadRefreshTransition.Terminal;
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        long now = _clock.GetTimestamp();
        if (now < _lastTimestamp) { Close(FinTsReadRefreshState.ClockInvalid); return; }
        _lastTimestamp = now;
        _elapsed = _clock.GetElapsedTime(_startedAt, now);
        if (_elapsed < TimeSpan.Zero) { Close(FinTsReadRefreshState.ClockInvalid); }
        else if (_elapsed >= _lifetime) { Close(FinTsReadRefreshState.TimedOut); }
    }
    private void Close(FinTsReadRefreshState state)
    {
        _state = state;
        _pending = null; _capability = null; _nationalAdvertisement = null; _previousPage = null; _dialog = null;
    }
}
