using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Credential-free metadata for the restricted closing envelope. No encoding, transmission or authentication evidence.</summary>
public sealed class FinTsPinTanDialogueEndRequestBinding
{
    private FinTsPinTanDialogueEndRequestBinding(FinTsPinTanDialogueEndContext context)
    {
        Context = context;
        Segments = new List<FinTsPinTanRequestSegmentBinding> {
            new("HNHBK", 1, 3, FinTsPinTanRequestSegmentRole.MessageHeader),
            new("HNVSK", 998, 3, FinTsPinTanRequestSegmentRole.EnvelopeHeader),
            new("HNVSD", 999, 1, FinTsPinTanRequestSegmentRole.EnvelopeData),
            new("HNSHK", 2, 4, FinTsPinTanRequestSegmentRole.SignatureHeader),
            new("HKEND", 3, 1, FinTsPinTanRequestSegmentRole.DialogueEnd),
            new("HNSHA", 4, 2, FinTsPinTanRequestSegmentRole.SignatureTrailer),
            new("HNHBS", 5, 1, FinTsPinTanRequestSegmentRole.MessageTrailer),
        }.AsReadOnly();
    }
    public FinTsPinTanDialogueEndContext Context { get; }
    public ReadOnlyCollection<FinTsPinTanRequestSegmentBinding> Segments { get; }
    public string DialogueId => Context.Request.Request.DialogueId;
    public int MessageNumber => Context.Request.Frame.MessageNumber;
    public int ExpectedBankMessageNumber => Context.Request.ExpectedBankMessageNumber;
    public int ProfileVersion => Context.Header.ProfileVersion;
    public int DialogueEndNumber => 3;

    public static FinTsPinTanDialogueEndRequestBinding ForEnvelopeCandidate(FinTsPinTanDialogueEndContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context); cancellationToken.ThrowIfCancellationRequested();
        if (!context.HasMatchingEvidence) { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext); }
        return new(context);
    }
}

/// <summary>Pure closing message/profile/reference comparison. Mapped security replies are not closing success.</summary>
public sealed class FinTsPinTanDialogueEndResponseBinding
{
    private FinTsPinTanDialogueEndResponseBinding(FinTsPinTanDialogueEndRequestBinding request, FinTsResponse response,
        List<FinTsPinTanResponseSegmentBinding> references, FinTsPinTanResponseBindingIssue issues)
    { Request = request; Response = response; References = references.AsReadOnly(); Issues = issues; }
    public FinTsPinTanDialogueEndRequestBinding Request { get; }
    public FinTsResponse Response { get; }
    public ReadOnlyCollection<FinTsPinTanResponseSegmentBinding> References { get; }
    public FinTsPinTanResponseBindingIssue Issues { get; }
    public bool HasMatchingReferences => Issues == FinTsPinTanResponseBindingIssue.None;

    public static FinTsPinTanDialogueEndResponseBinding Evaluate(FinTsPinTanDialogueEndRequestBinding request, FinTsResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanResponseBindingIssue.None;
        if (response.PinTanEnvelope is not { } envelope) { issues |= FinTsPinTanResponseBindingIssue.MissingEnvelope; }
        else if (envelope.ProfileVersion != request.ProfileVersion) { issues |= FinTsPinTanResponseBindingIssue.ProfileMismatch; }
        var header = response.Frame.Syntax.Segments[0].Fields;
        if (response.Frame.MessageNumber != request.ExpectedBankMessageNumber || header[2].Elements[0].HeaderText() != request.DialogueId ||
            header.Count != 5 || header[4].Elements.Count != 2 || header[4].Elements[0].HeaderText() != request.DialogueId ||
            FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.MessageNumber)
        { issues |= FinTsPinTanResponseBindingIssue.MessageMismatch; }
        List<FinTsPinTanResponseSegmentBinding> references = [];
        foreach (var segment in response.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code == "HIRMG") { continue; }
            var target = request.Segments.SingleOrDefault(s => s.Number == segment.Reference);
            references.Add(new(segment, target));
            if (segment.Reference is null) { issues |= FinTsPinTanResponseBindingIssue.MissingSegmentReference; }
            else if (target is null) { issues |= FinTsPinTanResponseBindingIssue.UnknownSegmentReference; }
            if (segment.Code != "HIRMS") { issues |= FinTsPinTanResponseBindingIssue.UninterpretedData; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, response, references, issues);
    }
}
