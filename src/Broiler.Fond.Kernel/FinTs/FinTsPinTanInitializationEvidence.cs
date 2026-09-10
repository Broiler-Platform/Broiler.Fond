namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanInitializationIssue
{
    None = 0, RequestKindMismatch = 1, ParameterSourceMismatch = 2, BindingNeedsReview = 4,
    EnvelopeIdentityMismatch = 8, ResponseNeedsReview = 16, StatusNeedsReview = 32, ParametersNeedReview = 64, ProceduresNeedReview = 128, RequirementsNeedReview = 256,
}

/// <summary>Pure semantic comparison for one assembled initialization candidate and one bound response.
/// ExecutionReported is an untrusted report, never authentication, session activation or permission to send.</summary>
public sealed class FinTsPinTanInitializationEvidence
{
    private FinTsPinTanInitializationEvidence(FinTsPinTanResponseBinding binding, FinTsParameterSet parameters,
        FinTsPinTanInitializationIssue issues, FinTsInitializationIssue parameterIssues)
    { Binding = binding; Parameters = parameters; Issues = issues; ParameterIssues = parameterIssues; }
    public FinTsPinTanResponseBinding Binding { get; }
    public FinTsPinTanRequestBinding Request => Binding.Request;
    public FinTsResponse Response => Binding.Response;
    public FinTsParameterSet Parameters { get; }
    public FinTsPinTanInitializationIssue Issues { get; }
    /// <summary>Detailed bank/user/customer/protocol/language issues; evaluated only for a matching source and initialization kind.</summary>
    public FinTsInitializationIssue ParameterIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanInitializationIssue.None;
    public FinTsInitializationOutcome Outcome => HasMatchingEvidence ? FinTsInitializationOutcome.ExecutionReported : FinTsInitializationOutcome.NeedsReview;
    public string ReportedDialogueId => Binding.ReportedDialogueId;
    public bool? BankVersionChanged => CanCompareParameters && Parameters.Bank is { } bank ? bank.Version != Request.SignatureEvidence.Request.Initialization!.Preparation.BankParameterVersion : null;
    public bool? UserVersionChanged => CanCompareParameters && Parameters.User is { } user ? user.Version != Request.SignatureEvidence.Request.Initialization!.Preparation.UserParameterVersion : null;
    private bool CanCompareParameters => Request.SignatureEvidence.Request.Initialization is not null && ReferenceEquals(Parameters.Source, Response);

    public static FinTsPinTanInitializationEvidence Evaluate(FinTsPinTanResponseBinding binding, FinTsParameterSet parameters,
        CancellationToken cancellationToken = default) => EvaluateCore(binding, parameters, null, null, cancellationToken);

    internal static FinTsPinTanInitializationEvidence EvaluateCore(FinTsPinTanResponseBinding binding, FinTsParameterSet parameters,
        FinTsTanParameterSet? procedures, FinTsPermittedProcedureSet? permissions, CancellationToken cancellationToken,
        FinTsPinTanParameterSet? pinTan = null, FinTsReadParameterSet? reads = null)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(parameters); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanInitializationIssue.None;
        var request = binding.Request;
        if (!binding.HasMatchingReferences) { issues |= FinTsPinTanInitializationIssue.BindingNeedsReview; }
        if (!ReferenceEquals(binding.Response, parameters.Source)) { issues |= FinTsPinTanInitializationIssue.ParameterSourceMismatch; }
        var initialization = request.SignatureEvidence.Request.Initialization;
        if (initialization is null) { issues |= FinTsPinTanInitializationIssue.RequestKindMismatch; }
        // Never combine parameter trees from another response or interpret synchronization as initialization.
        if ((issues & (FinTsPinTanInitializationIssue.ParameterSourceMismatch | FinTsPinTanInitializationIssue.RequestKindMismatch)) != 0)
        { return new(binding, parameters, issues, FinTsInitializationIssue.None); }

        var response = binding.Response;
        bool procedureSource = ReferenceEquals(procedures?.Source, parameters) && ReferenceEquals(permissions?.Source, response);
        if (response.PinTanEnvelope is { } envelope)
        {
            var key = envelope.SecurityHeader.Fields[6].Elements;
            var signature = request.SignatureEvidence.Header;
            if (envelope.SystemId.HeaderText() != signature.SystemId || key[0].HeaderText() != signature.CountryCode ||
                key[1].HeaderText() != signature.InstitutionId || key[2].HeaderText() != request.ExpectedUserId)
            { issues |= FinTsPinTanInitializationIssue.EnvelopeIdentityMismatch; }
        }
        if (response.HasErrors || response.HasConflictingClasses || response.HasIndeterminateProcessing)
        { issues |= FinTsPinTanInitializationIssue.ResponseNeedsReview; }
        bool messageExecution = false, identityExecution = false, preparationExecution = false;
        foreach (var segment in response.ReplySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var role = segment.IsMessageLevel ? (FinTsPinTanRequestSegmentRole?)null :
                binding.References.Single(r => ReferenceEquals(r.ResponseSegment, segment.Source)).Target?.Role;
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool permission = procedureSource && role == FinTsPinTanRequestSegmentRole.Preparation &&
                    permissions!.Reports.Any(r => ReferenceEquals(r.Reply, reply));
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || !permission && reply.Parameters.Any(p => !p.IsEmpty))
                { issues |= FinTsPinTanInitializationIssue.StatusNeedsReview; }
                bool supported = permission || (segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020");
                if (role == FinTsPinTanRequestSegmentRole.Preparation && reply.Code == "3050" && (parameters.Bank is not null || parameters.User is not null))
                { supported = true; }
                if (!supported) { issues |= FinTsPinTanInitializationIssue.StatusNeedsReview; }
                if (reply.Code != "0020") { continue; }
                if (segment.IsMessageLevel) { messageExecution = true; }
                else if (role == FinTsPinTanRequestSegmentRole.Identification) { identityExecution = true; }
                else if (role == FinTsPinTanRequestSegmentRole.Preparation) { preparationExecution = true; }
            }
        }
        if (!messageExecution && !(identityExecution && preparationExecution)) { issues |= FinTsPinTanInitializationIssue.StatusNeedsReview; }
        var interpreted = procedureSource ? procedures!.Advertisements.Select(a => a.Source).ToList() : [];
        if (procedureSource && ReferenceEquals(pinTan?.Source, parameters)) { interpreted.AddRange(pinTan!.Advertisements.Select(a => a.Source)); }
        if (procedureSource && ReferenceEquals(reads?.Source, parameters)) { interpreted.AddRange(reads!.Advertisements.Select(a => a.Source)); }
        var parameterIssues = FinTsInitializationEvidence.ParameterIssues(initialization!.Identification, initialization.Preparation, parameters,
            request.ExpectedUserId, cancellationToken, interpreted);
        if (parameterIssues != FinTsInitializationIssue.None) { issues |= FinTsPinTanInitializationIssue.ParametersNeedReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(binding, parameters, issues, parameterIssues);
    }
}
