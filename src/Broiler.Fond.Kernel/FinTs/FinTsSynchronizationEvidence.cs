namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Explicit caller-selected profile hypothesis. No negotiation, key possession or authentication evidence.</summary>
public enum FinTsSynchronizationProfile { Unspecified, PinTan1, PinTan2, Rah7, Rah9, Rah10 }
public enum FinTsSynchronizationNextStep { StopForReview, CloseAndReinitializeRequired }

[Flags]
public enum FinTsSynchronizationIssue
{
    None = 0, ProfileNeedsReview = 1, ModeNotPermitted = 2, RequestSystemMismatch = 4,
    MessageMismatch = 8, InvalidAssignedDialogue = 16, ReferenceMismatch = 32,
    ResponseNeedsReview = 64, StatusNeedsReview = 128, MissingReport = 256,
    DuplicateReport = 512, UninterpretedReports = 1024, ModeShapeMismatch = 2048,
    InvalidSystemId = 4096, SignatureLayoutMismatch = 8192, ReservedSecurityReference = 16384,
    RecoveryContextMissing = 32768, RecoveryContextMismatch = 65536, EnvelopeNeedsReview = 131072,
}

/// <summary>Caller-owned prior-dialogue observations only. Does not prove which messages a bank received.</summary>
public sealed class FinTsSynchronizationRecoveryContext
{
    public FinTsSynchronizationRecoveryContext(string previousDialogueId, int lastSubmittedMessageNumber)
    {
        ArgumentNullException.ThrowIfNull(previousDialogueId);
        if (previousDialogueId.Length is < 1 or > 30 || previousDialogueId is "0" or "unbekannt" || previousDialogueId[0] == ' ' || previousDialogueId[^1] == ' ' ||
            previousDialogueId.Any(c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255) || lastSubmittedMessageNumber is < 1 or > 9999)
        { throw SynchronizationFields.Invalid(); }
        PreviousDialogueId = previousDialogueId; LastSubmittedMessageNumber = lastSubmittedMessageNumber;
    }
    public string PreviousDialogueId { get; }
    public int LastSubmittedMessageNumber { get; }
}

/// <summary>Pure synchronization comparison. No recovery application, response consumption, dialogue closing or permission to send.</summary>
public sealed class FinTsSynchronizationEvidence
{
    private FinTsSynchronizationEvidence(FinTsUnsignedSynchronizationRequest request, FinTsSynchronizationDataSet response,
        FinTsSynchronizationProfile profile, FinTsSynchronizationRecoveryContext? recovery, string dialogue, FinTsSynchronizationIssue issues)
    { Request = request; Response = response; Profile = profile; RecoveryContext = recovery; ReportedDialogueId = dialogue; Issues = issues; }
    public FinTsUnsignedSynchronizationRequest Request { get; }
    public FinTsSynchronizationDataSet Response { get; }
    public FinTsSynchronizationProfile Profile { get; }
    public FinTsSynchronizationRecoveryContext? RecoveryContext { get; }
    public string ReportedDialogueId { get; }
    public FinTsSynchronizationIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsSynchronizationIssue.None;
    /// <summary>A matching observation still requires closing this dialogue and a new initialization before any business request.</summary>
    public FinTsSynchronizationNextStep NextStep => HasMatchingEvidence ? FinTsSynchronizationNextStep.CloseAndReinitializeRequired : FinTsSynchronizationNextStep.StopForReview;
    /// <summary>Only a unique matching report is selected; every candidate remains in Response.</summary>
    public FinTsSynchronizationReport? MatchingReport => HasMatchingEvidence && Response.Reports.Count == 1 ? Response.Reports[0] : null;

    public static FinTsSynchronizationEvidence Evaluate(FinTsUnsignedSynchronizationRequest request, FinTsSynchronizationDataSet response,
        FinTsSynchronizationProfile profile, FinTsSynchronizationRecoveryContext? recoveryContext = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); cancellationToken.ThrowIfCancellationRequested();
        var issues = RequestIssues(request, profile, recoveryContext);
        var identity = request.Identification;

        var source = response.Source; var header = source.Frame.Syntax.Segments[0].Fields;
        string dialogue = header[2].Elements[0].HeaderText();
        if (source.Frame.MessageNumber != 1 || header.Count != 5 || header[4].Elements.Count != 2 || header[4].Elements[0].HeaderText() != dialogue ||
            FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.Frame.MessageNumber) { issues |= FinTsSynchronizationIssue.MessageMismatch; }
        if (dialogue is "0" or "unbekannt" || dialogue[0] == ' ' || dialogue[^1] == ' ') { issues |= FinTsSynchronizationIssue.InvalidAssignedDialogue; }
        if (recoveryContext?.PreviousDialogueId == dialogue) { issues |= FinTsSynchronizationIssue.RecoveryContextMismatch; }
        if (source.PinTanEnvelope is not null) { issues |= FinTsSynchronizationIssue.EnvelopeNeedsReview; }
        if (source.HasErrors || source.HasConflictingClasses || source.HasIndeterminateProcessing) { issues |= FinTsSynchronizationIssue.ResponseNeedsReview; }
        foreach (var segment in source.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code == "HIRMG") { continue; }
            bool correct = segment.Code == "HIRMS" ? segment.Reference == identity.Source.Number || segment.Reference == request.Preparation.Source.Number || segment.Reference == request.Synchronization.Source.Number
                : segment.Code == "HISYN" ? segment.Reference == request.Synchronization.Source.Number : segment.Reference == request.Preparation.Source.Number;
            if (!correct) { issues |= FinTsSynchronizationIssue.ReferenceMismatch; }
        }
        bool messageExecution = false, syncExecution = false;
        foreach (var segment in source.ReplySegments)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || reply.Parameters.Any(p => !p.IsEmpty) ||
                    !(segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020")) { issues |= FinTsSynchronizationIssue.StatusNeedsReview; }
                if (reply.Code == "0020")
                {
                    if (segment.IsMessageLevel) { messageExecution = true; }
                    else if (segment.RequestSegmentNumber == request.Synchronization.Source.Number) { syncExecution = true; }
                }
            }
        }
        if (!messageExecution && !syncExecution) { issues |= FinTsSynchronizationIssue.StatusNeedsReview; }
        issues |= ReportIssues(request.Synchronization.Mode, response, profile, recoveryContext, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, response, profile, recoveryContext, dialogue, issues);
    }

    internal static FinTsSynchronizationIssue ReportIssues(FinTsSynchronizationMode mode, FinTsSynchronizationDataSet response,
        FinTsSynchronizationProfile profile, FinTsSynchronizationRecoveryContext? recoveryContext, CancellationToken cancellationToken)
    {
        var issues = FinTsSynchronizationIssue.None;
        bool software = profile == FinTsSynchronizationProfile.Rah10;
        if (response.Reports.Count == 0) { issues |= FinTsSynchronizationIssue.MissingReport; }
        if (response.Reports.Count > 1) { issues |= FinTsSynchronizationIssue.DuplicateReport; }
        if (response.UninterpretedSegments.Count != 0) { issues |= FinTsSynchronizationIssue.UninterpretedReports; }
        foreach (var report in response.Reports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedShape = mode switch
            {
                FinTsSynchronizationMode.SystemId => FinTsSynchronizationShape.SystemId,
                FinTsSynchronizationMode.LastMessageNumber => FinTsSynchronizationShape.LastMessageNumber,
                _ => FinTsSynchronizationShape.SignatureReferences,
            };
            if (report.Shape != expectedShape) { issues |= FinTsSynchronizationIssue.ModeShapeMismatch; }
            if (mode == FinTsSynchronizationMode.SystemId && report.SystemId is "0" or "unbekannt") { issues |= FinTsSynchronizationIssue.InvalidSystemId; }
            if (mode == FinTsSynchronizationMode.LastMessageNumber && recoveryContext is not null && report.LastMessageNumber > recoveryContext.LastSubmittedMessageNumber)
            { issues |= FinTsSynchronizationIssue.RecoveryContextMismatch; }
            if (mode == FinTsSynchronizationMode.SignatureReferences)
            {
                if (profile == FinTsSynchronizationProfile.Rah7 && !report.DigitalSignatureSecurityReference.HasValue ||
                    software && report.DigitalSignatureSecurityReference.HasValue) { issues |= FinTsSynchronizationIssue.SignatureLayoutMismatch; }
                if (report.SigningKeySecurityReference == 9999999999999999UL || report.DigitalSignatureSecurityReference == 9999999999999999UL)
                { issues |= FinTsSynchronizationIssue.ReservedSecurityReference; }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return issues;
    }
    internal static FinTsSynchronizationIssue RequestIssues(FinTsUnsignedSynchronizationRequest request,
        FinTsSynchronizationProfile profile, FinTsSynchronizationRecoveryContext? recoveryContext)
    {
        var issues = FinTsSynchronizationIssue.None;
        var mode = request.Synchronization.Mode;
        bool pin = profile is FinTsSynchronizationProfile.PinTan1 or FinTsSynchronizationProfile.PinTan2;
        bool card = profile is FinTsSynchronizationProfile.Rah7 or FinTsSynchronizationProfile.Rah9;
        bool software = profile == FinTsSynchronizationProfile.Rah10;
        if (!pin && !card && !software) { issues |= FinTsSynchronizationIssue.ProfileNeedsReview; }
        if (mode == FinTsSynchronizationMode.SignatureReferences && pin || mode == FinTsSynchronizationMode.SystemId && card) { issues |= FinTsSynchronizationIssue.ModeNotPermitted; }
        // Formals' two-counter condition names RAH-7, but predates the current RAH-9 profile. Do not infer its layout.
        if (mode == FinTsSynchronizationMode.SignatureReferences && profile == FinTsSynchronizationProfile.Rah9) { issues |= FinTsSynchronizationIssue.ProfileNeedsReview; }
        var identity = request.Identification;
        bool unsetSystem = identity.SystemId is "0" or "unbekannt";
        if ((pin || software) && (identity.SystemStatus != FinTsCustomerSystemStatus.Required || mode != FinTsSynchronizationMode.SystemId && unsetSystem) ||
            card && (identity.SystemStatus != FinTsCustomerSystemStatus.NotRequired || unsetSystem)) { issues |= FinTsSynchronizationIssue.RequestSystemMismatch; }
        if (mode == FinTsSynchronizationMode.LastMessageNumber && recoveryContext is null) { issues |= FinTsSynchronizationIssue.RecoveryContextMissing; }
        if (mode != FinTsSynchronizationMode.LastMessageNumber && recoveryContext is not null) { issues |= FinTsSynchronizationIssue.RecoveryContextMismatch; }

        return issues;
    }

}
