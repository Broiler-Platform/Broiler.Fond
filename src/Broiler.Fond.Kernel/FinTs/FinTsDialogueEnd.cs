using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>One HKEND-1 observation. Parsing does not close a dialogue or authorize a request.</summary>
public sealed class FinTsDialogueEndRequest
{
    private FinTsDialogueEndRequest(FinTsSegment source, string dialogueId) { Source = source; DialogueId = dialogueId; }
    public FinTsSegment Source { get; }
    public string DialogueId { get; }
    public static FinTsDialogueEndRequest Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Code != "HKEND" || source.Reference is not null) { throw DialogueEndFields.Invalid(); }
        if (source.Version != 1) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedDialogueEndVersion); }
        if (source.Fields.Count != 1 || source.Fields[0].Elements.Count != 1 || source.Fields[0].Elements[0].IsBinary) { throw DialogueEndFields.Invalid(); }
        string dialogue = source.Fields[0].Elements[0].HeaderText();
        DialogueEndFields.ValidateDialogue(dialogue);
        return new(source, dialogue);
    }
}

/// <summary>Unsigned three-segment close request with explicit independent bank-response counter.</summary>
public sealed class FinTsUnsignedDialogueEndRequest
{
    private FinTsUnsignedDialogueEndRequest(FinTsMessageFrame frame, FinTsDialogueEndRequest request, int expectedBankMessageNumber)
    { Frame = frame; Request = request; ExpectedBankMessageNumber = expectedBankMessageNumber; }
    public FinTsMessageFrame Frame { get; }
    public FinTsDialogueEndRequest Request { get; }
    public int ExpectedBankMessageNumber { get; }
    public static FinTsUnsignedDialogueEndRequest Parse(FinTsMessageFrame frame, int expectedBankMessageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame); cancellationToken.ThrowIfCancellationRequested();
        var segments = frame.Syntax.Segments;
        if (segments.Any(s => s.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA")) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
        if (segments.Count != 3 || frame.MessageNumber < 2 || expectedBankMessageNumber is < 2 or > 9999 ||
            segments[0].Fields.Count == 5 && !ParameterFields.Empty(segments[0].Fields[4])) { throw DialogueEndFields.Invalid(); }
        var request = FinTsDialogueEndRequest.Parse(segments[1], cancellationToken);
        if (segments[0].Fields[2].Elements[0].HeaderText() != request.DialogueId) { throw DialogueEndFields.Invalid(); }
        return new(frame, request, expectedBankMessageNumber);
    }
}

public static class FinTsUnsignedDialogueEndWriter
{
    /// <summary>Local unsigned encoding only; no sending, signing, counter allocation or automatic close.</summary>
    public static byte[] Encode(string dialogueId, int messageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dialogueId); cancellationToken.ThrowIfCancellationRequested();
        DialogueEndFields.ValidateDialogue(dialogueId);
        if (messageNumber is < 2 or > 9999) { throw DialogueEndFields.Invalid(); }
        string escaped = FinTsUnsignedWireEncoding.Text(dialogueId, 30, FinTsSyntaxError.InvalidDialogueEnd);
        byte[] wire = FinTsUnsignedWireEncoding.Frame(escaped, messageNumber, new StringBuilder("HKEND:2:1+").Append(escaped).Append('\''), 3);
        _ = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(wire, cancellationToken), 2, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return wire;
    }
}

[Flags]
public enum FinTsDialogueEndIssue
{
    None = 0, MessageMismatch = 1, ReferenceMismatch = 2, UnexpectedData = 4, EnvelopeNeedsReview = 8,
    StatusNeedsReview = 16, ConflictingTermination = 32, MissingTermination = 64, ResponseNeedsReview = 128,
}
public enum FinTsDialogueEndOutcome { NeedsReview, ClosureReported, AbortReported }

/// <summary>Pure request-bound standard-reply observations; no session mutation, transport teardown or replay consumption.</summary>
public sealed class FinTsDialogueEndEvidence
{
    private FinTsDialogueEndEvidence(FinTsUnsignedDialogueEndRequest request, FinTsResponse response, FinTsDialogueEndIssue issues, bool close, bool abort)
    { Request = request; Response = response; Issues = issues; CloseReported = close; AbortReported = abort; }
    public FinTsUnsignedDialogueEndRequest Request { get; }
    public FinTsResponse Response { get; }
    public FinTsDialogueEndIssue Issues { get; }
    /// <summary>Raw code observation, including on unresolved or mismatched responses.</summary>
    public bool CloseReported { get; }
    /// <summary>Raw code observation, including on unresolved or mismatched responses.</summary>
    public bool AbortReported { get; }
    public bool HasMatchingEvidence => Issues == FinTsDialogueEndIssue.None;
    public FinTsDialogueEndOutcome Outcome => !HasMatchingEvidence ? FinTsDialogueEndOutcome.NeedsReview : CloseReported ? FinTsDialogueEndOutcome.ClosureReported : FinTsDialogueEndOutcome.AbortReported;
    public static FinTsDialogueEndEvidence Evaluate(FinTsUnsignedDialogueEndRequest request, FinTsResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsDialogueEndIssue.None;
        var header = response.Frame.Syntax.Segments[0].Fields;
        if (response.Frame.MessageNumber != request.ExpectedBankMessageNumber || header[2].Elements[0].HeaderText() != request.Request.DialogueId ||
            header.Count != 5 || header[4].Elements.Count != 2 || header[4].Elements[0].HeaderText() != request.Request.DialogueId ||
            FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.Frame.MessageNumber) { issues |= FinTsDialogueEndIssue.MessageMismatch; }
        if (response.PinTanEnvelope is not null) { issues |= FinTsDialogueEndIssue.EnvelopeNeedsReview; }
        if (response.UninterpretedSegments.Count != 0) { issues |= FinTsDialogueEndIssue.UnexpectedData; }
        if (response.HasConflictingClasses || response.HasIndeterminateProcessing) { issues |= FinTsDialogueEndIssue.ResponseNeedsReview; }
        int close = 0, abort = 0;
        foreach (var segment in response.ReplySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!segment.IsMessageLevel && segment.RequestSegmentNumber != request.Request.Source.Number) { issues |= FinTsDialogueEndIssue.ReferenceMismatch; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reply.Code == "0100") { close++; }
                if (reply.Code == "9800") { abort++; }
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || reply.Parameters.Any(p => !p.IsEmpty) ||
                    !(segment.IsMessageLevel ? reply.Code is "0010" or "0020" or "0100" or "9800" : reply.Code is "0020" or "0100")) { issues |= FinTsDialogueEndIssue.StatusNeedsReview; }
                if (reply.Class == FinTsReplyClass.Error && reply.Code != "9800") { issues |= FinTsDialogueEndIssue.ResponseNeedsReview; }
            }
        }
        if (close == 0 && abort == 0) { issues |= FinTsDialogueEndIssue.MissingTermination; }
        if (close > 0 && abort > 0) { issues |= FinTsDialogueEndIssue.ConflictingTermination; }
        if (close > 1 || abort > 1) { issues |= FinTsDialogueEndIssue.StatusNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, response, issues, close > 0, abort > 0);
    }
}

internal static class DialogueEndFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidDialogueEnd);
    internal static void ValidateDialogue(string dialogue)
    {
        if (dialogue.Length is < 1 or > 30 || dialogue is "0" or "unbekannt" || dialogue[0] == ' ' || dialogue[^1] == ' ' ||
            dialogue.Any(c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255)) { throw Invalid(); }
    }
}
