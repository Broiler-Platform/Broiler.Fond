namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsTanContextIssue
{
    None = 0, MissingPermissionReport = 1, AmbiguousPermissions = 2, ProcedureNotListed = 4,
    AmbiguousProcedure = 8, ParameterScopeMismatch = 16, ParameterResponseNeedsReview = 32,
    MessageMismatch = 64, UnknownRequestReference = 128, MissingChallenge = 256, AmbiguousChallenge = 512,
    VersionMismatch = 1024, ProcessMismatch = 2048, HashMismatch = 4096, OrderReferenceMismatch = 8192,
    MissingMedium = 16384, MediumMismatch = 32768, RequestOptionsNeedReview = 65536,
    ResponseNeedsReview = 131072, StatusNeedsReview = 262144, DummyMismatch = 524288,
    ExpiryNeedsReview = 1048576, ProfileMismatch = 2097152,
}

public enum FinTsTanOutcomeObservation { NeedsReview, ChallengeReported, DecoupledPendingReported, ExemptionReported, ExecutionReported }

/// <summary>Pure request/evidence comparison. No authentication, replay consumption, continuation or permission to send.</summary>
public sealed class FinTsTanChallengeEvidence
{
    private FinTsTanChallengeEvidence(FinTsTanRequestContext request, FinTsTanParameterSet parameters, FinTsTanProcedure procedure,
        FinTsPermittedProcedureSet permissions, FinTsTanChallengeSet response, FinTsTanChallenge? challenge,
        FinTsTanContextIssue issues, FinTsTanOutcomeObservation observation)
    {
        Request = request; Parameters = parameters; Procedure = procedure; Permissions = permissions;
        Response = response; Challenge = challenge; Issues = issues; Observation = observation;
    }
    public FinTsTanRequestContext Request { get; }
    public FinTsTanParameterSet Parameters { get; }
    public FinTsTanProcedure Procedure { get; }
    public FinTsPermittedProcedureSet Permissions { get; }
    public FinTsTanChallengeSet Response { get; }
    public FinTsTanChallenge? Challenge { get; }
    public FinTsTanContextIssue Issues { get; }
    public FinTsTanOutcomeObservation Observation { get; }

    public static FinTsTanChallengeEvidence Evaluate(FinTsTanRequestContext request, FinTsTanParameterSet parameters,
        FinTsTanProcedure procedure, FinTsPermittedProcedureSet permissions, FinTsTanChallengeSet response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(procedure); ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();
        var advertisement = parameters.Advertisements.SingleOrDefault(a => a.Procedures.Contains(procedure));
        if (advertisement is null) { throw new FinTsFormatException(FinTsSyntaxError.InvalidTanContext); }
        var issues = FinTsTanContextIssue.None;
        if (permissions.Reports.Count == 0) { issues |= FinTsTanContextIssue.MissingPermissionReport; }
        if (permissions.IsAmbiguous) { issues |= FinTsTanContextIssue.AmbiguousPermissions; }
        if (!permissions.Reports.Any(r => r.SecurityFunctions.Contains(procedure.SecurityFunction))) { issues |= FinTsTanContextIssue.ProcedureNotListed; }
        if (parameters.Advertisements.Count(a => a.Source.Version == procedure.SegmentVersion) != 1 ||
            advertisement.Procedures.Count(p => p.SecurityFunction == procedure.SecurityFunction) != 1) { issues |= FinTsTanContextIssue.AmbiguousProcedure; }
        var parameterResponse = parameters.Source.Source;
        if (!ReferenceEquals(parameterResponse, permissions.Source) || !Same(Dialog(parameterResponse.Frame), Dialog(response.Source.Frame)) ||
            parameterResponse.Frame.MessageNumber > response.Source.Frame.MessageNumber ||
            Dialog(request.Frame).HeaderText() == "0" && !ReferenceEquals(parameterResponse, response.Source)) { issues |= FinTsTanContextIssue.ParameterScopeMismatch; }
        if (NeedsReview(parameterResponse)) { issues |= FinTsTanContextIssue.ParameterResponseNeedsReview; }
        if (NeedsReview(response.Source)) { issues |= FinTsTanContextIssue.ResponseNeedsReview; }
        if (!MatchesMessage(request, response.Source)) { issues |= FinTsTanContextIssue.MessageMismatch; }
        var requestNumbers = request.Frame.Syntax.Segments.Select(s => s.Number).ToHashSet();
        if (response.Source.BodySegments.Any(s => s.Reference is int reference && !requestNumbers.Contains(reference))) { issues |= FinTsTanContextIssue.UnknownRequestReference; }
        if (response.Source.PinTanEnvelope is { ProfileVersion: not 2 }) { issues |= FinTsTanContextIssue.ProfileMismatch; }
        if (request.Segment.Version != procedure.SegmentVersion) { issues |= FinTsTanContextIssue.VersionMismatch; }
        if (request.Process == "1" && procedure.ProcessVariant != "1" || request.Process is "4" or "S" && procedure.ProcessVariant != "2" ||
            request.Process == "S" && procedure.Method.HeaderText() != "Decoupled") { issues |= FinTsTanContextIssue.ProcessMismatch; }
        if (procedure.SmsAccountRequirement == "2" || request.Process == "1" && (procedure.OrderingAccountRequirement == "2" || procedure.ChallengeClassRequired) ||
            request.Process is "1" or "4" && procedure.ActiveMediaCount > 1 && procedure.MediumNameRequirement == "2" && !Present(request.MediumName))
        { issues |= FinTsTanContextIssue.RequestOptionsNeedReview; }

        var all = response.Source.UninterpretedSegments.Where(s => s.Code == "HITAN").ToArray();
        var candidates = response.Challenges.Where(c => c.RequestSegmentNumber == request.Segment.Number).ToArray();
        if (candidates.Length == 0) { issues |= FinTsTanContextIssue.MissingChallenge; }
        if (all.Length > 1) { issues |= FinTsTanContextIssue.AmbiguousChallenge; }
        FinTsTanChallenge? challenge = candidates.Length == 1 ? candidates[0] : null;
        var observation = FinTsTanOutcomeObservation.NeedsReview;
        if (challenge is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (challenge.Source.Version != procedure.SegmentVersion) { issues |= FinTsTanContextIssue.VersionMismatch; }
            if (challenge.Process != request.Process) { issues |= FinTsTanContextIssue.ProcessMismatch; }
            bool hashRequired = request.Process == "1" && advertisement.OrderHashAlgorithm != "0";
            if (hashRequired != Present(request.OrderHash) || hashRequired != Present(challenge.OrderHash) ||
                hashRequired && !Same(request.OrderHash, challenge.OrderHash)) { issues |= FinTsTanContextIssue.HashMismatch; }
            if (Present(request.OrderReference) && !Same(request.OrderReference, challenge.OrderReference)) { issues |= FinTsTanContextIssue.OrderReferenceMismatch; }

            var related = response.Source.ReplySegments.Where(s => s.RequestSegmentNumber == request.Segment.Number).SelectMany(s => s.Replies).ToArray();
            if (response.Source.ReplySegments.Where(s => s.RequestSegmentNumber != request.Segment.Number)
                .SelectMany(s => s.Replies).Any(r => r.Code is "0030" or "3076" or "3956")) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
            if (related.Any(r => r.Code is "0030" or "3076" or "3956" && !r.ElementReference.IsEmpty)) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
            if (request.Process == "S" && related.Any(r => r.Code == "0020" && !r.ElementReference.IsEmpty)) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
            int pending = related.Count(r => r.Code == "0030"), exempt = related.Count(r => r.Code == "3076"), waiting = related.Count(r => r.Code == "3956");
            bool noReference = challenge.HasNoReferencePlaceholder;
            bool noChallenge = challenge.Text is { IsBinary: false } text && text.HeaderText() == "nochallenge";
            bool dummy = noReference || exempt != 0;
            if (dummy)
            {
                if (!noReference || !noChallenge || request.Process is not ("1" or "4") || Present(challenge.HhdData)) { issues |= FinTsTanContextIssue.DummyMismatch; }
                if (exempt != 1 || pending != 0 || waiting != 0) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
                observation = FinTsTanOutcomeObservation.ExemptionReported;
            }
            else
            {
                if (request.Process == "S")
                {
                    int executed = related.Count(r => r.Code == "0020");
                    if (executed == 1 && waiting == 0 && pending == 0 && exempt == 0)
                    {
                        observation = FinTsTanOutcomeObservation.ExecutionReported;
                    }
                    else
                    {
                        if (waiting != 1 || pending != 0 || exempt != 0 || executed != 0) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
                        observation = FinTsTanOutcomeObservation.DecoupledPendingReported;
                    }
                }
                else
                {
                    if (pending != 1 || waiting != 0 || exempt != 0 || noChallenge) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
                    observation = FinTsTanOutcomeObservation.ChallengeReported;
                }
                if (request.Process != "S" && related.Any(r => r.Code == "0020")) { issues |= FinTsTanContextIssue.StatusNeedsReview; }
                if (request.Process is "1" or "4" && procedure.ActiveMediaCount is null && !Present(challenge.MediumName)) { issues |= FinTsTanContextIssue.MissingMedium; }
                if (Present(request.MediumName) && !Same(request.MediumName, challenge.MediumName)) { issues |= FinTsTanContextIssue.MediumMismatch; }
            }
            if (challenge.ValidUntil is not null && !ParameterFields.Empty(challenge.ValidUntil)) { issues |= FinTsTanContextIssue.ExpiryNeedsReview; }
        }
        return new(request, parameters, procedure, permissions, response, challenge, issues,
            issues == FinTsTanContextIssue.None ? observation : FinTsTanOutcomeObservation.NeedsReview);
    }

    private static bool NeedsReview(FinTsResponse response) => response.HasErrors || response.HasConflictingClasses ||
        response.ReplySegments.SelectMany(s => s.Replies).Any(r => r.Code is not ("0010" or "0020" or "0030" or "3076" or "3920" or "3956"));
    private static bool MatchesMessage(FinTsTanRequestContext request, FinTsResponse response)
    {
        var fields = response.Frame.Syntax.Segments[0].Fields;
        if (response.Frame.MessageNumber != request.ExpectedBankMessageNumber || fields.Count != 5 || fields[4].Elements.Count != 2 ||
            FinTsSyntax.Number(fields[4].Elements[1], 4, false) != request.Frame.MessageNumber || !Same(Dialog(response.Frame), fields[4].Elements[0])) { return false; }
        string assigned = Dialog(response.Frame).HeaderText();
        if (assigned is "0" or "unbekannt") { return false; }
        if (Dialog(request.Frame).HeaderText() == "0" && (request.Frame.MessageNumber != 1 || request.ExpectedBankMessageNumber != 1)) { return false; }
        return Dialog(request.Frame).HeaderText() == "0" || Same(Dialog(request.Frame), Dialog(response.Frame));
    }
    private static FinTsDataElement Dialog(FinTsMessageFrame frame) => frame.Syntax.Segments[0].Fields[2].Elements[0];
    private static bool Present(FinTsDataElement? value) => value is not null && !value.IsEmpty;
    private static bool Same(FinTsDataElement? left, FinTsDataElement? right) => left is not null && right is not null &&
        left.IsBinary == right.IsBinary && left.CopyValueBytes().AsSpan().SequenceEqual(right.CopyValueBytes());
}
