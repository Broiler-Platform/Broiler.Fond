namespace Broiler.Fond.Kernel.FinTs;

public sealed class FinTsPinTanInitializationAttemptResult
{
    internal FinTsPinTanInitializationAttemptResult(FinTsInitializationTransition transition, FinTsPinTanInitializationEvidence? evidence = null, FinTsPinTanInitializationProcedureEvidence? procedureEvidence = null, FinTsPinTanInitializationRequirementsEvidence? requirementsEvidence = null)
    { Transition = transition; Evidence = evidence; ProcedureEvidence = procedureEvidence; RequirementsEvidence = requirementsEvidence; }
    public FinTsInitializationTransition Transition { get; }
    /// <summary>Caller-owned evidence returned once for a scoped response, including semantic review.</summary>
    public FinTsPinTanInitializationEvidence? Evidence { get; }
    /// <summary>Combined procedure qualification on the explicit procedure-response path; null on legacy or non-handoff results.</summary>
    public FinTsPinTanInitializationProcedureEvidence? ProcedureEvidence { get; }
    /// <summary>Full HIPINS/procedure qualification on the explicit requirements-response path; inspect this combined result.</summary>
    public FinTsPinTanInitializationRequirementsEvidence? RequirementsEvidence { get; }
}

/// <summary>Scalar-only diagnostics; no candidate, response, credential or identity references.</summary>
public sealed class FinTsPinTanInitializationSnapshot
{
    internal FinTsPinTanInitializationSnapshot(FinTsInitializationState state, int candidates, int handled, TimeSpan remaining,
        FinTsPinTanInitializationIssue issues, FinTsPinTanResponseBindingIssue bindingIssues, FinTsInitializationIssue parameterIssues, FinTsPinTanInitializationProcedureIssue procedureIssues, FinTsPinTanInitializationRequirementsIssue requirementsIssues)
    { State = state; CandidatesRecorded = candidates; ResponsesHandled = handled; RemainingTime = remaining; Issues = issues; BindingIssues = bindingIssues; ParameterIssues = parameterIssues; ProcedureIssues = procedureIssues; RequirementsIssues = requirementsIssues; }
    public FinTsInitializationState State { get; }
    public int CandidatesRecorded { get; }
    public int ResponsesHandled { get; }
    public TimeSpan RemainingTime { get; }
    public FinTsPinTanInitializationIssue Issues { get; }
    public FinTsPinTanResponseBindingIssue BindingIssues { get; }
    public FinTsInitializationIssue ParameterIssues { get; }
    public FinTsPinTanInitializationProcedureIssue ProcedureIssues { get; }
    public FinTsPinTanInitializationRequirementsIssue RequirementsIssues { get; }
}

/// <summary>One local assembled initialization candidate and one response handoff. No encoding, sending or authentication.
/// The attempt takes no credentials/output buffers; disposal releases pending metadata, not caller-owned objects.</summary>
public sealed class FinTsPinTanInitializationAttempt : IDisposable
{
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private FinTsInitializationState _state;
    private FinTsPinTanRequestBinding? _pending;
    private long _startedAt, _lastTimestamp;
    private TimeSpan _elapsed;
    private int _candidates, _handled;
    private FinTsPinTanInitializationIssue _issues;
    private FinTsPinTanResponseBindingIssue _bindingIssues;
    private FinTsInitializationIssue _parameterIssues;
    private FinTsPinTanInitializationProcedureIssue _procedureIssues;
    private FinTsPinTanInitializationRequirementsIssue _requirementsIssues;

    public FinTsPinTanInitializationAttempt(TimeSpan lifetime, TimeProvider? clock = null)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
        _clock = clock ?? TimeProvider.System; _lifetime = lifetime;
        bool valid;
        try { valid = _clock.TimestampFrequency > 0; } catch (Exception) { valid = false; }
        if (!valid) { throw new ArgumentException("A working positive-frequency clock is required.", nameof(clock)); }
    }

    public FinTsPinTanInitializationSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_state, _candidates, _handled, IsActive ? _lifetime - _elapsed : TimeSpan.Zero, _issues, _bindingIssues, _parameterIssues, _procedureIssues, _requirementsIssues);
        }
    }

    /// <summary>Records metadata only; it does not prove encoding or transmission. The absolute deadline begins here.</summary>
    public FinTsInitializationTransition Start(FinTsPinTanRequestBinding candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            ObserveClock(); CheckCancellation(cancellationToken);
            if (_state != FinTsInitializationState.Ready) { return IsActive ? FinTsInitializationTransition.WrongState : FinTsInitializationTransition.Terminal; }
            if (candidate.SignatureEvidence.Request.Initialization is null)
            { _issues = FinTsPinTanInitializationIssue.RequestKindMismatch; Close(FinTsInitializationState.NeedsReview); return FinTsInitializationTransition.RejectedForReview; }
            try { _startedAt = _lastTimestamp = _clock.GetTimestamp(); }
            catch (Exception) { if (_state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.ClockInvalid); } return FinTsInitializationTransition.Terminal; }
            CheckCancellation(cancellationToken);
            if (_state != FinTsInitializationState.Ready) { return FinTsInitializationTransition.Terminal; }
            _pending = candidate; _candidates = 1; _state = FinTsInitializationState.AwaitingResponse;
            ObserveClock(); CheckCancellation(cancellationToken);
            return IsActive ? FinTsInitializationTransition.RequestRecorded : FinTsInitializationTransition.Terminal;
        }
    }

    public FinTsPinTanInitializationAttemptResult AcceptResponse(FinTsParameterSet parameters, CancellationToken cancellationToken = default) =>
        AcceptCore(parameters, null, null, null, cancellationToken);

    public FinTsPinTanInitializationAttemptResult AcceptProcedureResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions);
        return AcceptCore(procedures.Source, procedures, permissions, null, cancellationToken);
    }

    public FinTsPinTanInitializationAttemptResult AcceptRequirementsResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        FinTsPinTanParameterSet pinTan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(pinTan);
        return AcceptCore(procedures.Source, procedures, permissions, pinTan, cancellationToken);
    }

    public FinTsPinTanInitializationAttemptResult AcceptReadParametersResponse(FinTsTanParameterSet procedures, FinTsPermittedProcedureSet permissions,
        FinTsPinTanParameterSet pinTan, FinTsReadParameterSet reads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(pinTan); ArgumentNullException.ThrowIfNull(reads);
        return AcceptCore(procedures.Source, procedures, permissions, pinTan, cancellationToken, reads);
    }

    private FinTsPinTanInitializationAttemptResult AcceptCore(FinTsParameterSet parameters, FinTsTanParameterSet? procedures,
        FinTsPermittedProcedureSet? permissions, FinTsPinTanParameterSet? pinTan, CancellationToken cancellationToken, FinTsReadParameterSet? reads = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_gate)
        {
            try
            {
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(_state == FinTsInitializationState.Ready ? FinTsInitializationTransition.WrongState : FinTsInitializationTransition.Terminal); }
                var binding = FinTsPinTanResponseBinding.Evaluate(_pending!, parameters.Source, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsInitializationTransition.Terminal); }
                const FinTsPinTanResponseBindingIssue foreignScope = FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference | FinTsPinTanResponseBindingIssue.UnknownSegmentReference | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;
                if ((binding.Issues & foreignScope) != 0)
                {
                    _issues = FinTsPinTanInitializationIssue.BindingNeedsReview; _bindingIssues = binding.Issues; _parameterIssues = FinTsInitializationIssue.None; _procedureIssues = FinTsPinTanInitializationProcedureIssue.None; _requirementsIssues = FinTsPinTanInitializationRequirementsIssue.None;
                    return new(FinTsInitializationTransition.ContextMismatch);
                }
                var requirementsEvidence = pinTan is null ? null : FinTsPinTanInitializationRequirementsEvidence.EvaluateCore(binding, procedures!, permissions!, pinTan, reads, cancellationToken);
                var procedureEvidence = requirementsEvidence?.Procedures ?? (procedures is null ? null : FinTsPinTanInitializationProcedureEvidence.Evaluate(binding, procedures, permissions!, cancellationToken));
                var evidence = procedureEvidence?.Initialization ?? FinTsPinTanInitializationEvidence.Evaluate(binding, parameters, cancellationToken);
                ObserveClock(); CheckCancellation(cancellationToken);
                if (!IsActive) { return new(FinTsInitializationTransition.Terminal); }
                _issues = evidence.Issues; _bindingIssues = binding.Issues; _parameterIssues = evidence.ParameterIssues; _handled = 1;
                _procedureIssues = procedureEvidence?.Issues ?? FinTsPinTanInitializationProcedureIssue.None;
                _requirementsIssues = requirementsEvidence?.Issues ?? FinTsPinTanInitializationRequirementsIssue.None;
                bool matching = requirementsEvidence?.HasMatchingEvidence ?? procedureEvidence?.HasMatchingEvidence ?? evidence.HasMatchingEvidence;
                if (_requirementsIssues != FinTsPinTanInitializationRequirementsIssue.None) { _issues |= FinTsPinTanInitializationIssue.RequirementsNeedReview; }
                if (_procedureIssues != FinTsPinTanInitializationProcedureIssue.None) { _issues |= FinTsPinTanInitializationIssue.ProceduresNeedReview; }
                Close(matching ? FinTsInitializationState.ExecutionReported : FinTsInitializationState.NeedsReview);
                return new(matching ? FinTsInitializationTransition.ExecutionReported : FinTsInitializationTransition.RejectedForReview, evidence, procedureEvidence, requirementsEvidence);
            }
            catch (OperationCanceledException)
            {
                if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Cancelled); }
                throw;
            }
        }
    }

    public void Cancel() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Cancelled); } } }
    /// <summary>Abandon after parsing/transport failure or disconnect. No automatic retry or credential ownership.</summary>
    public void Stop() { lock (_gate) { ObserveClock(); if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Stopped); } } }
    public void Dispose() { Stop(); GC.SuppressFinalize(this); }
    private bool IsActive => _state == FinTsInitializationState.AwaitingResponse;
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (IsActive || _state == FinTsInitializationState.Ready) { Close(FinTsInitializationState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (!IsActive) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (!IsActive) { return; }
            if (now < _lastTimestamp) { Close(FinTsInitializationState.ClockInvalid); return; }
            _lastTimestamp = now; _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (!IsActive) { return; }
            if (_elapsed < TimeSpan.Zero) { Close(FinTsInitializationState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsInitializationState.TimedOut); }
        }
        catch (Exception) { if (IsActive) { Close(FinTsInitializationState.ClockInvalid); } }
    }
    private void Close(FinTsInitializationState state) { _state = state; _pending = null; }
}
