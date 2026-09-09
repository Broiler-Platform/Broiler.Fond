namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Validated outer framing only. Body schemas, security wrappers, response
/// correlation, character repertoires and dialogue sequencing remain untrusted.
/// </summary>
public sealed class FinTsMessageFrame
{
    private FinTsMessageFrame(FinTsSyntaxDocument syntax, int messageNumber)
    {
        Syntax = syntax;
        MessageNumber = messageNumber;
    }

    public FinTsSyntaxDocument Syntax { get; }
    public int MessageNumber { get; }

    public static FinTsMessageFrame Parse(ReadOnlySpan<byte> wire, CancellationToken cancellationToken = default)
    {
        FinTsSyntaxDocument syntax = FinTsSyntax.ParseSegments(wire, cancellationToken);
        var segments = syntax.Segments;
        if (segments.Count < 2) { throw Invalid(); }
        FinTsSegment header = segments[0];
        FinTsSegment trailer = segments[^1];
        if (header.Code != "HNHBK" || header.Number != 1 || header.Version != 3 || header.Reference is not null ||
            trailer.Code != "HNHBS" || trailer.Version != 1 || trailer.Reference is not null ||
            header.Fields.Count is < 4 or > 5 || trailer.Fields.Count != 1) { throw Invalid(); }
        string size = Scalar(header.Fields[0]).HeaderText();
        if (size.Length != 12 || size.Any(c => c is < '0' or > '9') ||
            !ulong.TryParse(size, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong declared) ||
            declared != (ulong)syntax.ByteLength) { throw Invalid(); }
        if (Scalar(header.Fields[1]).HeaderText() != "300") { throw Invalid(); }
        // IDs stay in the syntax tree; this does not assert negotiated repertoire or session correlation.
        ValidateDialogId(Scalar(header.Fields[2]));
        int number = FinTsSyntax.Number(Scalar(header.Fields[3]), 4, allowZero: false);
        if (FinTsSyntax.Number(Scalar(trailer.Fields[0]), 4, allowZero: false) != number) { throw Invalid(); }
        if (header.Fields.Count == 5)
        {
            var reference = header.Fields[4].Elements;
            // Accept an omitted optional reference retained as an empty trailing field.
            if (!(reference.Count == 1 && !reference[0].IsBinary && reference[0].IsEmpty))
            {
                if (reference.Count != 2 || reference[0].IsEmpty) { throw Invalid(); }
                ValidateDialogId(reference[0]);
                _ = FinTsSyntax.Number(reference[1], 4, allowZero: false);
            }
        }

        HashSet<int> numbers = [];
        for (int index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FinTsSegment segment = segments[index];
            if (!numbers.Add(segment.Number) || index != 0 && segment.Code == "HNHBK" ||
                index != segments.Count - 1 && segment.Code == "HNHBS") { throw Invalid(); }
        }

        // Wrapped messages use reserved 998/999; their binary body is deliberately opaque.
        bool wrapped = segments.Count == 4 && segments[1].Code == "HNVSK" && segments[1].Number == 998 &&
            segments[2].Code == "HNVSD" && segments[2].Number == 999;
        if (wrapped)
        {
            if (trailer.Number is < 2 or > 997) { throw Invalid(); }
        }
        else
        {
            for (int index = 0; index < segments.Count; index++)
            {
                if (segments[index].Number != index + 1 || segments[index].Code is "HNVSK" or "HNVSD") { throw Invalid(); }
            }
        }

        return new(syntax, number);
    }

    private static FinTsDataElement Scalar(FinTsField field)
    {
        if (field.Elements.Count != 1 || field.Elements[0].IsBinary) { throw Invalid(); }
        return field.Elements[0];
    }

    private static void ValidateDialogId(FinTsDataElement element)
    {
        string value = element.HeaderText();
        if (value.Length is < 1 or > 30 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
    }

    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidFrame);
}
