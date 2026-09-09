using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsReplyClass { Success, Warning, Error, Unknown }
public enum FinTsReplyMeaning { Uninterpreted, ReceiptReported, ExecutionReported, AuthorizationPending, MoreInformationAvailable, ProcessingIndeterminate }

/// <summary>Typed reply evidence. Text/parameters remain raw, untrusted, and excluded from ToString.</summary>
public sealed class FinTsReply
{
    internal FinTsReply(string code, FinTsField source)
    {
        Code = code;
        Source = source;
        Class = code[0] switch { '0' => FinTsReplyClass.Success, '3' => FinTsReplyClass.Warning, '9' => FinTsReplyClass.Error, _ => FinTsReplyClass.Unknown };
        Meaning = code switch
        {
            "0010" => FinTsReplyMeaning.ReceiptReported,
            "0020" => FinTsReplyMeaning.ExecutionReported,
            "0030" => FinTsReplyMeaning.AuthorizationPending,
            "3040" => FinTsReplyMeaning.MoreInformationAvailable,
            "9000" => FinTsReplyMeaning.ProcessingIndeterminate,
            _ => FinTsReplyMeaning.Uninterpreted,
        };
        Parameters = source.Elements.Skip(3).ToList().AsReadOnly();
    }

    public string Code { get; }
    public FinTsReplyClass Class { get; }
    /// <summary>A deliberately small code vocabulary; this is no operation-completion decision.</summary>
    public FinTsReplyMeaning Meaning { get; }
    public FinTsField Source { get; }
    public FinTsDataElement ElementReference => Source.Elements[1];
    public FinTsDataElement Text => Source.Elements[2];
    public ReadOnlyCollection<FinTsDataElement> Parameters { get; }
}

public sealed class FinTsReplySegment
{
    internal FinTsReplySegment(FinTsSegment source, List<FinTsReply> replies)
    {
        Source = source;
        Replies = replies.AsReadOnly();
    }

    public FinTsSegment Source { get; }
    public bool IsMessageLevel => Source.Code == "HIRMG";
    public int? RequestSegmentNumber => Source.Reference;
    public ReadOnlyCollection<FinTsReply> Replies { get; }
    public bool HasConflictingClasses => Replies.Count(r => r.Class == FinTsReplyClass.Success) > 1 ||
        Replies.Any(r => r.Class == FinTsReplyClass.Success) && Replies.Any(r => r.Class == FinTsReplyClass.Error);
}

/// <summary>
/// HIRMG/HIRMS version 2 schema evidence for a plain frame or explicitly parsed PIN/TAN envelope. No signature,
/// SCA, charset negotiation, body-schema interpretation or domain ingestion.
/// </summary>
public sealed class FinTsResponse
{
    public const int MaximumRepliesPerSegment = 99;
    public const int MaximumReplies = 4096;

    private FinTsResponse(FinTsMessageFrame frame, FinTsPinTanEnvelope? envelope, List<FinTsSegment> body, List<FinTsReplySegment> replies, List<FinTsSegment> uninterpreted)
    {
        Frame = frame;
        PinTanEnvelope = envelope;
        BodySegments = body.AsReadOnly();
        ReplySegments = replies.AsReadOnly();
        UninterpretedSegments = uninterpreted.AsReadOnly();
        HasConflictingClasses = replies.Any(r => r.HasConflictingClasses) ||
            replies.Single(r => r.IsMessageLevel).Replies.Any(r => r.Class == FinTsReplyClass.Success) &&
            replies.Where(r => !r.IsMessageLevel).SelectMany(r => r.Replies).Any(r => r.Class == FinTsReplyClass.Error);
    }

    public FinTsMessageFrame Frame { get; }
    public FinTsPinTanEnvelope? PinTanEnvelope { get; }
    public ReadOnlyCollection<FinTsSegment> BodySegments { get; }
    public ReadOnlyCollection<FinTsReplySegment> ReplySegments { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }
    public bool HasConflictingClasses { get; }
    public bool HasErrors => ReplySegments.SelectMany(r => r.Replies).Any(r => r.Class == FinTsReplyClass.Error);
    public bool HasUninterpretedCodes => ReplySegments.SelectMany(r => r.Replies).Any(r => r.Meaning == FinTsReplyMeaning.Uninterpreted);
    public bool HasIndeterminateProcessing => ReplySegments.SelectMany(r => r.Replies).Any(r => r.Meaning == FinTsReplyMeaning.ProcessingIndeterminate);

    public static FinTsResponse Parse(FinTsMessageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return ParseBody(frame, null, frame.Syntax.Segments.Skip(1).SkipLast(1).ToList(), cancellationToken);
    }

    public static FinTsResponse ParsePinTan(FinTsPinTanEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return ParseBody(envelope.Frame, envelope, envelope.Body.Segments.ToList(), cancellationToken);
    }

    private static FinTsResponse ParseBody(FinTsMessageFrame frame, FinTsPinTanEnvelope? envelope, List<FinTsSegment> body, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsReplySegment> replies = [];
        List<FinTsSegment> uninterpreted = [];
        HashSet<int> requestReferences = [];
        bool foundMessageReply = false;
        int total = 0;
        foreach (FinTsSegment segment in body)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code is "HNVSK" or "HNVSD") { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
            if (segment.Code is not ("HIRMG" or "HIRMS")) { uninterpreted.Add(segment); continue; }
            if (segment.Version != 2) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedResponseVersion); }
            bool messageLevel = segment.Code == "HIRMG";
            if (messageLevel)
            {
                if (foundMessageReply || segment.Reference is not null) { throw Invalid(); }
                foundMessageReply = true;
            }
            else if (segment.Reference is not int reference || !requestReferences.Add(reference)) { throw Invalid(); }

            if (segment.Fields.Count is < 1 or > MaximumRepliesPerSegment) { throw Invalid(); }
            List<FinTsReply> entries = [];
            foreach (FinTsField field in segment.Fields)
            {
                if (++total > MaximumReplies) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
                var elements = field.Elements;
                if (elements.Count is < 3 or > 13) { throw Invalid(); }
                byte[] code = TextBytes(elements[0], 4, required: true);
                if (code.Length != 4 || code.Any(c => c is < (byte)'0' or > (byte)'9')) { throw Invalid(); }
                byte[] elementReference = TextBytes(elements[1], 7, required: false);
                if (messageLevel && elementReference.Length != 0) { throw Invalid(); }
                _ = TextBytes(elements[2], 80, required: true);
                foreach (FinTsDataElement parameter in elements.Skip(3)) { _ = TextBytes(parameter, 35, required: false); }
                entries.Add(new(System.Text.Encoding.ASCII.GetString(code), field));
            }

            replies.Add(new(segment, entries));
        }

        if (!foundMessageReply) { throw Invalid(); }
        return new(frame, envelope, body, replies, uninterpreted);
    }

    private static byte[] TextBytes(FinTsDataElement element, int maximumBytes, bool required)
    {
        if (element.IsBinary) { throw Invalid(); }
        byte[] bytes = element.CopyValueBytes();
        if (bytes.Length > maximumBytes || required && bytes.Length == 0 || bytes.Any(c => c < 32 || c is >= 127 and <= 160)) { throw Invalid(); }
        return bytes;
    }

    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidResponse);
}
