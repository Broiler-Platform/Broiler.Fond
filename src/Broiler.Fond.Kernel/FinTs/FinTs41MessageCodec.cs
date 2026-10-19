using System.Globalization;
using System.Text;
using System.Xml;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Thrown when FinTS 4.1 XML syntax, framing, or security constraints are violated.
/// </summary>
public sealed class FinTs41MalformedXmlException : InvalidOperationException
{
    public FinTs41MalformedXmlException(string message) : base(message) { }
    public FinTs41MalformedXmlException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// FinTS 4.1 XML message header (HNHBK v4).
/// </summary>
public sealed record FinTs41Header(
    string DialogueId,
    int MessageNumber,
    string Sender,
    string Recipient,
    DateTimeOffset Timestamp);

/// <summary>
/// Generic XML element within a FinTS 4.1 segment.
/// </summary>
public sealed record FinTs41Element(
    string Name,
    string? Value,
    IReadOnlyList<FinTs41Element> Children)
{
    public static FinTs41Element Leaf(string name, string? value) =>
        new(name, value, Array.Empty<FinTs41Element>());

    public static FinTs41Element Node(string name, params FinTs41Element[] children) =>
        new(name, null, children);
}

/// <summary>
/// FinTS 4.1 business or response segment within an envelope.
/// </summary>
public sealed record FinTs41Segment(
    string Tag,
    int Version,
    int SegmentNumber,
    IReadOnlyList<FinTs41Element> Elements)
{
    public string? GetElementValue(string elementName)
    {
        for (int i = 0; i < Elements.Count; i++)
        {
            if (string.Equals(Elements[i].Name, elementName, StringComparison.OrdinalIgnoreCase))
            {
                return Elements[i].Value;
            }
        }
        return null;
    }
}

/// <summary>
/// FinTS 4.1 message trailer (HNHBS v4).
/// </summary>
public sealed record FinTs41Trailer(int MessageNumber);

/// <summary>
/// Structured bank return code extracted from HIRMG or HIRMS segments.
/// </summary>
public sealed record FinTs41ReturnCode(
    string Code,
    string Text,
    string? ReferenceSegment = null)
{
    public bool IsSuccess => Code.StartsWith("00", StringComparison.Ordinal);
    public bool IsWarning => Code.StartsWith("3", StringComparison.Ordinal);
    public bool IsError => Code.StartsWith("9", StringComparison.Ordinal);
}

/// <summary>
/// Complete FinTS 4.1 XML message envelope.
/// </summary>
public sealed record FinTs41XmlMessage(
    FinTs41Header Header,
    IReadOnlyList<FinTs41Segment> Segments,
    FinTs41Trailer Trailer)
{
    public IReadOnlyList<FinTs41ReturnCode> ExtractReturnCodes()
    {
        var list = new List<FinTs41ReturnCode>();
        foreach (var seg in Segments)
        {
            if (string.Equals(seg.Tag, "HIRMG", StringComparison.OrdinalIgnoreCase))
            {
                var code = seg.GetElementValue("code") ?? "0000";
                var text = seg.GetElementValue("text") ?? string.Empty;
                list.Add(new FinTs41ReturnCode(code, text, null));
            }
            else if (string.Equals(seg.Tag, "HIRMS", StringComparison.OrdinalIgnoreCase))
            {
                var code = seg.GetElementValue("code") ?? "0000";
                var text = seg.GetElementValue("text") ?? string.Empty;
                var refSeg = seg.GetElementValue("referenceSegment");
                list.Add(new FinTs41ReturnCode(code, text, refSeg));
            }
        }
        return list;
    }
}

/// <summary>
/// Safe encoder and decoder for FinTS 4.1 XML message envelopes.
/// </summary>
public sealed class FinTs41MessageCodec
{
    public static readonly FinTs41MessageCodec Instance = new();

    public const string XmlNamespace = "urn:org:fints:spec:xml:2008";
    public const int MaxCharactersInDocument = 1_048_576; // 1 MiB
    public const int MaxNestingDepth = 16;
    public const int MaxSegments = 512;

    private static readonly UTF8Encoding Utf8NoBom = new(false, true);

    public byte[] EncodeMessage(FinTs41XmlMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(message.Header);
        ArgumentNullException.ThrowIfNull(message.Trailer);

        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = Utf8NoBom,
            Indent = false,
            NewLineChars = "\n",
            OmitXmlDeclaration = false
        };

        using (var writer = XmlWriter.Create(ms, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("FinTS", XmlNamespace);

            // Write HNHBK v4
            writer.WriteStartElement("HNHBK");
            writer.WriteAttributeString("version", "4");
            writer.WriteElementString("dialogueId", message.Header.DialogueId);
            writer.WriteElementString("messageNumber", message.Header.MessageNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sender", message.Header.Sender);
            writer.WriteElementString("recipient", message.Header.Recipient);
            writer.WriteElementString("timestamp", message.Header.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndElement(); // HNHBK

            // Write Segments
            if (message.Segments != null)
            {
                foreach (var segment in message.Segments)
                {
                    writer.WriteStartElement(segment.Tag);
                    writer.WriteAttributeString("version", segment.Version.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("segNo", segment.SegmentNumber.ToString(CultureInfo.InvariantCulture));

                    WriteElements(writer, segment.Elements);

                    writer.WriteEndElement(); // segment
                }
            }

            // Write HNHBS v4
            writer.WriteStartElement("HNHBS");
            writer.WriteAttributeString("version", "4");
            writer.WriteElementString("messageNumber", message.Trailer.MessageNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement(); // HNHBS

            writer.WriteEndElement(); // FinTS
            writer.WriteEndDocument();
        }

        return ms.ToArray();
    }

    public FinTs41XmlMessage DecodeMessage(ReadOnlySpan<byte> xmlBytes)
    {
        if (xmlBytes.Length == 0)
        {
            throw new FinTs41MalformedXmlException("FinTS 4.1 XML message payload is empty.");
        }

        if (xmlBytes.Length > MaxCharactersInDocument)
        {
            throw new FinTs41MalformedXmlException($"FinTS 4.1 XML message payload exceeds {MaxCharactersInDocument} byte limit.");
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = true
        };

        using var ms = new MemoryStream(xmlBytes.ToArray(), false);
        using var reader = XmlReader.Create(ms, settings);

        FinTs41Header? header = null;
        FinTs41Trailer? trailer = null;
        var segments = new List<FinTs41Segment>();

        try
        {
            if (!reader.ReadToFollowing("FinTS") && !reader.ReadToFollowing("fintsMessage"))
            {
                throw new FinTs41MalformedXmlException("FinTS 4.1 root element '<FinTS>' or '<fintsMessage>' was not found.");
            }

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.Depth > MaxNestingDepth)
                {
                    throw new FinTs41MalformedXmlException($"XML nesting depth exceeds allowed limit of {MaxNestingDepth} levels.");
                }

                string elementName = reader.LocalName;

                if (string.Equals(elementName, "HNHBK", StringComparison.OrdinalIgnoreCase))
                {
                    header = ReadHeader(reader);
                }
                else if (string.Equals(elementName, "HNHBS", StringComparison.OrdinalIgnoreCase))
                {
                    trailer = ReadTrailer(reader);
                }
                else
                {
                    if (segments.Count >= MaxSegments)
                    {
                        throw new FinTs41MalformedXmlException($"FinTS 4.1 message exceeds maximum segment limit of {MaxSegments}.");
                    }
                    var segment = ReadSegment(reader);
                    segments.Add(segment);
                }
            }
        }
        catch (XmlException ex)
        {
            throw new FinTs41MalformedXmlException($"FinTS 4.1 XML syntax error: {ex.Message}", ex);
        }

        if (header == null)
        {
            throw new FinTs41MalformedXmlException("Missing mandatory FinTS 4.1 header '<HNHBK>'.");
        }

        if (trailer == null)
        {
            throw new FinTs41MalformedXmlException("Missing mandatory FinTS 4.1 trailer '<HNHBS>'.");
        }

        if (header.MessageNumber != trailer.MessageNumber)
        {
            throw new FinTs41MalformedXmlException(
                $"FinTS 4.1 message framing mismatch: header message number ({header.MessageNumber}) != trailer message number ({trailer.MessageNumber}).");
        }

        return new FinTs41XmlMessage(header, segments, trailer);
    }

    private static void WriteElements(XmlWriter writer, IReadOnlyList<FinTs41Element> elements)
    {
        if (elements == null) return;
        foreach (var elem in elements)
        {
            writer.WriteStartElement(elem.Name);
            if (!string.IsNullOrEmpty(elem.Value))
            {
                writer.WriteString(elem.Value);
            }
            if (elem.Children != null && elem.Children.Count > 0)
            {
                WriteElements(writer, elem.Children);
            }
            writer.WriteEndElement();
        }
    }

    private static FinTs41Header ReadHeader(XmlReader reader)
    {
        string dialogueId = string.Empty;
        int messageNumber = 1;
        string sender = string.Empty;
        string recipient = string.Empty;
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        using var subReader = reader.ReadSubtree();
        subReader.Read(); // Move onto <HNHBK>

        while (!subReader.EOF)
        {
            if (subReader.NodeType == XmlNodeType.Element && subReader.LocalName != "HNHBK")
            {
                string name = subReader.LocalName;
                string val = subReader.ReadElementContentAsString();
                switch (name)
                {
                    case "dialogueId":
                        dialogueId = val;
                        break;
                    case "messageNumber":
                        if (int.TryParse(val, CultureInfo.InvariantCulture, out int mn))
                            messageNumber = mn;
                        break;
                    case "sender":
                        sender = val;
                        break;
                    case "recipient":
                        recipient = val;
                        break;
                    case "timestamp":
                        if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                            timestamp = ts;
                        break;
                }
            }
            else
            {
                subReader.Read();
            }
        }

        return new FinTs41Header(dialogueId, messageNumber, sender, recipient, timestamp);
    }

    private static FinTs41Trailer ReadTrailer(XmlReader reader)
    {
        int messageNumber = 1;

        using var subReader = reader.ReadSubtree();
        subReader.Read(); // Move onto <HNHBS>

        while (!subReader.EOF)
        {
            if (subReader.NodeType == XmlNodeType.Element && subReader.LocalName != "HNHBS")
            {
                string name = subReader.LocalName;
                string val = subReader.ReadElementContentAsString();
                if (name == "messageNumber" && int.TryParse(val, CultureInfo.InvariantCulture, out int mn))
                {
                    messageNumber = mn;
                }
            }
            else
            {
                subReader.Read();
            }
        }

        return new FinTs41Trailer(messageNumber);
    }

    private static FinTs41Segment ReadSegment(XmlReader reader)
    {
        string tag = reader.LocalName;
        int version = 1;
        int segNo = 1;

        if (int.TryParse(reader.GetAttribute("version"), CultureInfo.InvariantCulture, out int v))
        {
            version = v;
        }
        if (int.TryParse(reader.GetAttribute("segNo"), CultureInfo.InvariantCulture, out int s))
        {
            segNo = s;
        }

        var elements = new List<FinTs41Element>();
        using var subReader = reader.ReadSubtree();
        subReader.Read(); // Move onto <Tag>

        while (!subReader.EOF)
        {
            if (subReader.NodeType == XmlNodeType.Element && subReader.LocalName != tag)
            {
                string name = subReader.LocalName;
                string val = subReader.ReadElementContentAsString();
                elements.Add(FinTs41Element.Leaf(name, val));
            }
            else
            {
                subReader.Read();
            }
        }

        return new FinTs41Segment(tag, version, segNo, elements);
    }
}
