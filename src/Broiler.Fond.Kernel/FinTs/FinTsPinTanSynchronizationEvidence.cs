namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanSynchronizationIssue
{
    None = 0, RequestKindMismatch = 1, ReportSourceMismatch = 2, BindingNeedsReview = 4,
    EnvelopeIdentityMismatch = 8, ResponseNeedsReview = 16, StatusNeedsReview = 32, SynchronizationNeedsReview = 64,
}

/// <summary>Pure assembled PIN/TAN synchronization comparison. No recovery application, sending, authentication or session activation.</summary>
public sealed class FinTsPinTanSynchronizationEvidence
{
    private FinTsPinTanSynchronizationEvidence(FinTsPinTanResponseBinding binding, FinTsSynchronizationDataSet data,
        FinTsSynchronizationRecoveryContext? recovery, FinTsPinTanSynchronizationIssue issues, FinTsSynchronizationIssue synchronizationIssues, FinTsSignedTanResult? approval = null)
    { Binding = binding; Data = data; RecoveryContext = recovery; Issues = issues; SynchronizationIssues = synchronizationIssues; Approval = approval; }
    public FinTsSignedTanResult? Approval { get; }
    public FinTsPinTanResponseBinding Binding { get; }
    public FinTsPinTanRequestBinding Request => Binding.Request;
    public FinTsResponse Response => Binding.Response;
    public FinTsSynchronizationDataSet Data { get; }
    public FinTsSynchronizationRecoveryContext? RecoveryContext { get; }
    public FinTsPinTanSynchronizationIssue Issues { get; }
    /// <summary>Request/mode/report/recovery details, evaluated only for a synchronization candidate and exact response source.</summary>
    public FinTsSynchronizationIssue SynchronizationIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanSynchronizationIssue.None;
    public FinTsSynchronizationNextStep NextStep => HasMatchingEvidence ? FinTsSynchronizationNextStep.CloseAndReinitializeRequired : FinTsSynchronizationNextStep.StopForReview;
    public FinTsSynchronizationReport? MatchingReport => HasMatchingEvidence && Data.Reports.Count == 1 ? Data.Reports[0] : null;
    public string ReportedDialogueId => Binding.ReportedDialogueId;

    public static FinTsPinTanSynchronizationEvidence Evaluate(FinTsPinTanResponseBinding binding, FinTsSynchronizationDataSet data,
        FinTsSynchronizationRecoveryContext? recoveryContext = null, CancellationToken cancellationToken = default)
        => EvaluateCore(binding, data, recoveryContext, cancellationToken, null);
    internal static FinTsPinTanSynchronizationEvidence EvaluateCore(FinTsPinTanResponseBinding binding, FinTsSynchronizationDataSet data,
        FinTsSynchronizationRecoveryContext? recoveryContext, CancellationToken cancellationToken, FinTsSignedTanResult? approval)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(data); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanSynchronizationIssue.None;
        if (!binding.HasMatchingReferences) { issues |= FinTsPinTanSynchronizationIssue.BindingNeedsReview; }
        if (!ReferenceEquals(data.Source, binding.Response)) { issues |= FinTsPinTanSynchronizationIssue.ReportSourceMismatch; }
        var request = binding.Request.SignatureEvidence.Request.Synchronization;
        if (request is null) { issues |= FinTsPinTanSynchronizationIssue.RequestKindMismatch; }
        if ((issues & (FinTsPinTanSynchronizationIssue.ReportSourceMismatch | FinTsPinTanSynchronizationIssue.RequestKindMismatch)) != 0)
        { return new(binding, data, recoveryContext, issues, FinTsSynchronizationIssue.None); }
        var profile = binding.Request.ProfileVersion == 1 ? FinTsSynchronizationProfile.PinTan1 : FinTsSynchronizationProfile.PinTan2;
        var details = FinTsSynchronizationEvidence.RequestIssues(request!, profile, recoveryContext) |
            FinTsSynchronizationEvidence.ReportIssues(request!.Synchronization.Mode, data, profile, recoveryContext, cancellationToken);
        if (approval is not null)
        {
            if (approval.Status != FinTsSignedTanStatus.Confirmed || approval.Request is not { } approved || !approved.IsCurrent ||
                !ReferenceEquals(approved.Origin, binding.Request) || !ReferenceEquals(approved.Challenge.Response.Source, data.Source))
                issues |= FinTsPinTanSynchronizationIssue.BindingNeedsReview;
            else
            {
                var parameters = FinTsParameterSet.Parse(data.Source, cancellationToken);
                var tan = FinTsTanParameterSet.Parse(parameters, cancellationToken);
                var pin = FinTsPinTanParameterSet.Parse(parameters, cancellationToken);
                var read = FinTsReadParameterSet.Parse(parameters, cancellationToken);
                var interpreted = tan.Advertisements.Select(a => a.Source).Concat(pin.Advertisements.Select(a => a.Source))
                    .Concat(read.Advertisements.Select(a => a.Source)).Append(approved.Challenge.Challenge!.Source).ToHashSet();
                var parameterIssues = FinTsInitializationEvidence.ParameterIssues(request.Identification, request.Preparation, parameters,
                    binding.Request.ExpectedUserId, cancellationToken, interpreted.Concat(data.Reports.Select(r => r.Source)).ToHashSet());
                if (parameterIssues != 0) issues |= FinTsPinTanSynchronizationIssue.SynchronizationNeedsReview;
                if (data.UninterpretedSegments.All(s => s.Code is "HIBPA" or "HIUPA" or "HIUPD" || interpreted.Contains(s)))
                    details &= ~FinTsSynchronizationIssue.UninterpretedReports;
            }
        }
        if (recoveryContext?.PreviousDialogueId == binding.ReportedDialogueId) { details |= FinTsSynchronizationIssue.RecoveryContextMismatch; }
        if (details != FinTsSynchronizationIssue.None) { issues |= FinTsPinTanSynchronizationIssue.SynchronizationNeedsReview; }
        var response = binding.Response;
        if (response.PinTanEnvelope is { } envelope)
        {
            var signature = binding.Request.SignatureEvidence.Header;
            var key = envelope.SecurityHeader.Fields[6].Elements;
            if (key[0].HeaderText() != signature.CountryCode || key[1].HeaderText() != signature.InstitutionId || key[2].HeaderText() != binding.Request.ExpectedUserId)
            { issues |= FinTsPinTanSynchronizationIssue.EnvelopeIdentityMismatch; }
            // Assignment responses name the newly reported system, not the outgoing zero placeholder.
            // Ambiguous/malformed assignment reports already withhold matching evidence; never select one for this check.
            string? expectedSystem = request.Synchronization.Mode == FinTsSynchronizationMode.SystemId
                ? data.Reports.Count == 1 && data.Reports[0].Shape == FinTsSynchronizationShape.SystemId && data.Reports[0].SystemId is not ("0" or "unbekannt")
                    ? data.Reports[0].SystemId : null
                : signature.SystemId;
            if (expectedSystem is not null && envelope.SystemId.HeaderText() != expectedSystem)
            { issues |= FinTsPinTanSynchronizationIssue.EnvelopeIdentityMismatch; }
        }
        if (response.HasErrors || response.HasConflictingClasses || response.HasIndeterminateProcessing)
        { issues |= FinTsPinTanSynchronizationIssue.ResponseNeedsReview; }
        bool messageExecution = false, synchronizationExecution = false;
        foreach (var segment in response.ReplySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var role = segment.IsMessageLevel ? (FinTsPinTanRequestSegmentRole?)null :
                binding.References.Single(r => ReferenceEquals(r.ResponseSegment, segment.Source)).Target?.Role;
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool permission = approval is not null && role == FinTsPinTanRequestSegmentRole.Preparation && reply.Code == "3920";
                bool challenge = approval is not null && role == FinTsPinTanRequestSegmentRole.TanOperation && reply.Code == "0030";
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || !permission && reply.Parameters.Any(p => !p.IsEmpty) ||
                    !(permission || challenge || (segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020")))
                { issues |= FinTsPinTanSynchronizationIssue.StatusNeedsReview; }
                if (reply.Code != "0020") { continue; }
                if (segment.IsMessageLevel) { messageExecution = true; }
                else if (role == FinTsPinTanRequestSegmentRole.Synchronization) { synchronizationExecution = true; }
            }
        }
        if (!messageExecution && !synchronizationExecution) { issues |= FinTsPinTanSynchronizationIssue.StatusNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(binding, data, recoveryContext, issues, details, approval);
    }
}
