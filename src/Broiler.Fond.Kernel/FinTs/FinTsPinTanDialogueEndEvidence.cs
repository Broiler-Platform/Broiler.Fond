namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanDialogueEndIssue
{
    None = 0, BindingNeedsReview = 1, EnvelopeIdentityMismatch = 2, StatusNeedsReview = 4,
    ConflictingTermination = 8, MissingTermination = 16, ResponseNeedsReview = 32, UnexpectedData = 64,
}

/// <summary>Pure source-bound termination observations. No socket closure, response consumption, recovery application or session activation.</summary>
public sealed class FinTsPinTanDialogueEndEvidence
{
    private FinTsPinTanDialogueEndEvidence(FinTsPinTanDialogueEndResponseBinding binding, FinTsPinTanDialogueEndIssue issues, bool close, bool abort)
    { Binding = binding; Issues = issues; CloseReported = close; AbortReported = abort; }
    public FinTsPinTanDialogueEndResponseBinding Binding { get; }
    public FinTsPinTanDialogueEndRequestBinding Request => Binding.Request;
    public FinTsResponse Response => Binding.Response;
    public FinTsPinTanDialogueEndIssue Issues { get; }
    /// <summary>Raw observation, including on foreign or unresolved responses; inspect Outcome before using it.</summary>
    public bool CloseReported { get; }
    /// <summary>Raw observation, including on foreign or unresolved responses; inspect Outcome before using it.</summary>
    public bool AbortReported { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanDialogueEndIssue.None;
    public FinTsDialogueEndOutcome Outcome => !HasMatchingEvidence ? FinTsDialogueEndOutcome.NeedsReview : CloseReported ? FinTsDialogueEndOutcome.ClosureReported : FinTsDialogueEndOutcome.AbortReported;

    public static FinTsPinTanDialogueEndEvidence Evaluate(FinTsPinTanDialogueEndResponseBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding); cancellationToken.ThrowIfCancellationRequested();
        var issues = binding.HasMatchingReferences ? FinTsPinTanDialogueEndIssue.None : FinTsPinTanDialogueEndIssue.BindingNeedsReview;
        var response = binding.Response; var expected = binding.Request.Context.Header;
        if (response.PinTanEnvelope is { } envelope)
        {
            var key = envelope.SecurityHeader.Fields[6].Elements;
            if (envelope.SystemId.HeaderText() != expected.SystemId || key[0].HeaderText() != expected.CountryCode ||
                key[1].HeaderText() != expected.InstitutionId || key[2].HeaderText() != expected.UserId)
            { issues |= FinTsPinTanDialogueEndIssue.EnvelopeIdentityMismatch; }
        }
        if (response.UninterpretedSegments.Count != 0) { issues |= FinTsPinTanDialogueEndIssue.UnexpectedData; }
        if (response.HasConflictingClasses || response.HasIndeterminateProcessing) { issues |= FinTsPinTanDialogueEndIssue.ResponseNeedsReview; }
        int close = 0, abort = 0;
        foreach (var segment in response.ReplySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool closingRole = !segment.IsMessageLevel && segment.RequestSegmentNumber == binding.Request.DialogueEndNumber;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reply.Code == "0100") { close++; }
                if (reply.Code == "9800") { abort++; }
                bool supported = segment.IsMessageLevel ? reply.Code is "0010" or "0020" or "0100" or "9800" : closingRole && reply.Code is "0020" or "0100";
                if (!supported || !seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || reply.Parameters.Any(p => !p.IsEmpty))
                { issues |= FinTsPinTanDialogueEndIssue.StatusNeedsReview; }
                if (reply.Class == FinTsReplyClass.Error && reply.Code != "9800") { issues |= FinTsPinTanDialogueEndIssue.ResponseNeedsReview; }
            }
        }
        if (close == 0 && abort == 0) { issues |= FinTsPinTanDialogueEndIssue.MissingTermination; }
        if (close > 0 && abort > 0) { issues |= FinTsPinTanDialogueEndIssue.ConflictingTermination; }
        if (close > 1 || abort > 1) { issues |= FinTsPinTanDialogueEndIssue.StatusNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(binding, issues, close > 0, abort > 0);
    }
}
