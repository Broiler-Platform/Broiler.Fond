namespace Broiler.Fond.Kernel.FinTs;

/// <summary>A bounded observation of an unsigned synthetic HKTAN request, not a request builder or send authorization.</summary>
public sealed class FinTsTanRequestContext
{
    private FinTsTanRequestContext(FinTsMessageFrame frame, FinTsSegment segment, int expectedBankMessageNumber)
    {
        Frame = frame;
        Segment = segment;
        ExpectedBankMessageNumber = expectedBankMessageNumber;
    }
    public FinTsMessageFrame Frame { get; }
    public FinTsSegment Segment { get; }
    public int ExpectedBankMessageNumber { get; }
    public string Process => Segment.Fields[0].Elements[0].HeaderText();
    public FinTsDataElement Operation => Segment.Fields[1].Elements[0];
    public FinTsDataElement? OrderHash => Element(3);
    public FinTsDataElement? OrderReference => Element(4);
    public FinTsDataElement? MediumName => Element(10);
    private FinTsDataElement? Element(int index) => Segment.Fields.Count > index ? Segment.Fields[index].Elements[0] : null;

    public static FinTsTanRequestContext Parse(FinTsMessageFrame request, int requestSegmentNumber, int expectedBankMessageNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedBankMessageNumber is < 1 or > 9999) { throw Invalid(); }
        var segments = request.Syntax.Segments;
        if (segments.Any(s => s.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA")) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
        var header = segments[0].Fields;
        if (header.Count == 5 && !ParameterFields.Empty(header[4])) { throw Invalid(); }
        var candidates = segments.Where(s => s.Code == "HKTAN").ToArray();
        if (candidates.Length != 1 || candidates[0].Number != requestSegmentNumber) { throw Invalid(); }
        var segment = candidates[0];
        var fields = segment.Fields;
        if (segment.Version is not (6 or 7) || segment.Reference is not null || fields.Count is < 2 or > 12) { throw Invalid(); }
        for (int index = 0; index < fields.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index is 2 or 6 or 7 or 8 or 9 or 11)
            {
                // Account, cancellation, challenge-class and HHD-response options need a later request codec.
                if (!ParameterFields.Empty(fields[index])) { throw Invalid(); }
            }
            else if (fields[index].Elements.Count != 1) { throw Invalid(); }
        }
        string process = Text(fields[0].Elements[0], 1, true);
        if (process is not ("1" or "4") && !(segment.Version == 7 && process == "S")) { throw Invalid(); }
        string operation = Text(fields[1].Elements[0], 6, process != "S");
        if (operation.Length != 0 && !ParameterFields.IsOperation(operation)) { throw Invalid(); }
        if (fields.Count > 3)
        {
            var hash = fields[3].Elements[0];
            if (hash.IsBinary)
            {
                if (process != "1" || hash.CopyValueBytes().Length is < 1 or > 256) { throw Invalid(); }
            }
            else if (!hash.IsEmpty) { throw Invalid(); }
        }
        if (process == "S" && fields.Count < 6 || process == "1" && fields.Count < 6) { throw Invalid(); }
        if (fields.Count > 4) { _ = Text(fields[4].Elements[0], 35, process == "S"); }
        if (fields.Count > 5)
        {
            string further = Text(fields[5].Elements[0], 1, process is "1" or "S");
            if (further != (process == "4" ? "" : "N")) { throw Invalid(); }
        }
        if (fields.Count > 10) { _ = Text(fields[10].Elements[0], 32, false); }
        return new(request, segment, expectedBankMessageNumber);
    }
    private static string Text(FinTsDataElement element, int maximum, bool required)
    {
        if (element.IsBinary) { throw Invalid(); }
        string value = element.HeaderText();
        if (value.Length > maximum || required && value.Length == 0 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return value;
    }
    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidTanContext);
}
