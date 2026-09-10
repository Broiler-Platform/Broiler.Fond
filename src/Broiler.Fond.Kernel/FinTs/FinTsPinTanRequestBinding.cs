using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanRequestSegmentRole
{
    MessageHeader, EnvelopeHeader, EnvelopeData, SignatureHeader, Identification, Preparation, Synchronization, SignatureTrailer, MessageTrailer, DialogueEnd,
}

/// <summary>Credential-free description of one actual outgoing segment position, including reserved wrapper numbers.</summary>
public sealed class FinTsPinTanRequestSegmentBinding
{
    internal FinTsPinTanRequestSegmentBinding(string code, int number, int version, FinTsPinTanRequestSegmentRole role)
    { Code = code; Number = number; Version = version; Role = role; }
    public string Code { get; }
    public int Number { get; }
    public int Version { get; }
    public FinTsPinTanRequestSegmentRole Role { get; }
}

/// <summary>Metadata for the restricted envelope candidate, never proof of encoding, transmission or authentication.
/// Construction takes no credential owners or assembled secret bytes.</summary>
public sealed class FinTsPinTanRequestBinding
{
    private FinTsPinTanRequestBinding(FinTsPinTanSignatureEvidence signature)
    {
        SignatureEvidence = signature;
        List<FinTsPinTanRequestSegmentBinding> segments = [
            new("HNHBK", 1, 3, FinTsPinTanRequestSegmentRole.MessageHeader),
            new("HNVSK", 998, 3, FinTsPinTanRequestSegmentRole.EnvelopeHeader),
            new("HNVSD", 999, 1, FinTsPinTanRequestSegmentRole.EnvelopeData),
            new("HNSHK", 2, 4, FinTsPinTanRequestSegmentRole.SignatureHeader),
            new("HKIDN", 3, 2, FinTsPinTanRequestSegmentRole.Identification),
            new("HKVVB", 4, 3, FinTsPinTanRequestSegmentRole.Preparation),
        ];
        bool sync = signature.Request.Synchronization is not null;
        if (sync) { segments.Add(new("HKSYN", 5, 3, FinTsPinTanRequestSegmentRole.Synchronization)); }
        segments.Add(new("HNSHA", sync ? 6 : 5, 2, FinTsPinTanRequestSegmentRole.SignatureTrailer));
        segments.Add(new("HNHBS", sync ? 7 : 6, 1, FinTsPinTanRequestSegmentRole.MessageTrailer));
        Segments = segments.AsReadOnly();
    }
    public FinTsPinTanSignatureEvidence SignatureEvidence { get; }
    /// <summary>Outer framing/wrappers followed by logical inner segments and the outer trailer.</summary>
    public ReadOnlyCollection<FinTsPinTanRequestSegmentBinding> Segments { get; }
    public int MessageNumber => 1;
    public int ProfileVersion => SignatureEvidence.Header.ProfileVersion;
    public string ExpectedUserId => SignatureEvidence.Request.ExpectedUserId;
    public int IdentificationNumber => 3;
    public int PreparationNumber => 4;
    public int? SynchronizationNumber => SignatureEvidence.Request.Synchronization is not null ? 5 : null;

    public static FinTsPinTanRequestBinding ForEnvelopeCandidate(FinTsPinTanSignatureEvidence signature, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature); cancellationToken.ThrowIfCancellationRequested();
        if (!signature.HasMatchingEvidence) { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext); }
        return new(signature);
    }
}

[Flags]
public enum FinTsPinTanResponseBindingIssue
{
    None = 0, MissingEnvelope = 1, ProfileMismatch = 2, MessageMismatch = 4, InvalidAssignedDialogue = 8,
    MissingSegmentReference = 16, UnknownSegmentReference = 32, SegmentRoleMismatch = 64, UninterpretedData = 128,
}

/// <summary>A source-preserving reference observation. Target is null for absent or unknown request references.</summary>
public sealed class FinTsPinTanResponseSegmentBinding
{
    internal FinTsPinTanResponseSegmentBinding(FinTsSegment response, FinTsPinTanRequestSegmentBinding? target)
    { ResponseSegment = response; Target = target; }
    public FinTsSegment ResponseSegment { get; }
    public FinTsPinTanRequestSegmentBinding? Target { get; }
}

/// <summary>Pure message/profile/reference comparison only. Matching references do not imply successful execution,
/// correct parameter identities, trusted envelope identity, authentication, response consumption or replay protection.</summary>
public sealed class FinTsPinTanResponseBinding
{
    private FinTsPinTanResponseBinding(FinTsPinTanRequestBinding request, FinTsResponse response, string dialogue,
        List<FinTsPinTanResponseSegmentBinding> references, FinTsPinTanResponseBindingIssue issues)
    { Request = request; Response = response; ReportedDialogueId = dialogue; References = references.AsReadOnly(); Issues = issues; }
    public FinTsPinTanRequestBinding Request { get; }
    public FinTsResponse Response { get; }
    public string ReportedDialogueId { get; }
    public ReadOnlyCollection<FinTsPinTanResponseSegmentBinding> References { get; }
    public FinTsPinTanResponseBindingIssue Issues { get; }
    public bool HasMatchingReferences => Issues == FinTsPinTanResponseBindingIssue.None;

    public static FinTsPinTanResponseBinding Evaluate(FinTsPinTanRequestBinding request, FinTsResponse response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanResponseBindingIssue.None;
        if (response.PinTanEnvelope is not { } envelope) { issues |= FinTsPinTanResponseBindingIssue.MissingEnvelope; }
        else if (envelope.ProfileVersion != request.ProfileVersion) { issues |= FinTsPinTanResponseBindingIssue.ProfileMismatch; }
        var header = response.Frame.Syntax.Segments[0].Fields;
        string dialogue = header[2].Elements[0].HeaderText();
        if (response.Frame.MessageNumber != 1 || header.Count != 5 || header[4].Elements.Count != 2 ||
            header[4].Elements[0].HeaderText() != dialogue || FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.MessageNumber)
        { issues |= FinTsPinTanResponseBindingIssue.MessageMismatch; }
        // First responses reference the assigned dialogue, not the outgoing zero placeholder.
        if (dialogue is "0" or "unbekannt" || dialogue[0] == ' ' || dialogue[^1] == ' ')
        { issues |= FinTsPinTanResponseBindingIssue.InvalidAssignedDialogue; }
        List<FinTsPinTanResponseSegmentBinding> references = [];
        foreach (var segment in response.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code == "HIRMG") { continue; }
            var target = request.Segments.SingleOrDefault(s => s.Number == segment.Reference);
            references.Add(new(segment, target));
            if (segment.Reference is null) { issues |= FinTsPinTanResponseBindingIssue.MissingSegmentReference; }
            else if (target is null) { issues |= FinTsPinTanResponseBindingIssue.UnknownSegmentReference; }
            // HIRMS may report errors on framing/security segments as well as business segments. Binding is not acceptance.
            if (segment.Code == "HIRMS") { continue; }
            int? expected = segment.Code switch
            {
                "HIBPA" or "HIUPA" or "HIUPD" or "HIPINS" or "HITANS" or "HISALS" or "HISPAS" => request.PreparationNumber,
                "HISYN" => request.SynchronizationNumber,
                _ => null,
            };
            bool supported = (segment.Code, segment.Version) is ("HIBPA", 3) or ("HIUPA", 4) or ("HIUPD", 6) or ("HIPINS", 1) or ("HITANS", 6 or 7) or ("HISYN", 4) or ("HISALS", 6 or 7 or 8) or ("HISPAS", 1 or 2 or 3);
            if (!supported) { issues |= FinTsPinTanResponseBindingIssue.UninterpretedData; }
            if (segment.Code == "HISYN" && expected is null || expected is not null && segment.Reference != expected)
            { issues |= FinTsPinTanResponseBindingIssue.SegmentRoleMismatch; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, response, dialogue, references, issues);
    }
}
