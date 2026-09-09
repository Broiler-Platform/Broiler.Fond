using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>One uncompressed PIN/TAN response envelope. Structural evidence only; no authentication or decryption.</summary>
public sealed class FinTsPinTanEnvelope
{
    private FinTsPinTanEnvelope(FinTsMessageFrame frame, FinTsSyntaxDocument body, int profileVersion)
    {
        Frame = frame;
        Body = body;
        ProfileVersion = profileVersion;
    }

    public FinTsMessageFrame Frame { get; }
    public FinTsSyntaxDocument Body { get; }
    public int ProfileVersion { get; }
    public FinTsSegment SecurityHeader => Frame.Syntax.Segments[1];
    /// <summary>Untrusted reported system ID; no comparison with an authenticated session has occurred.</summary>
    public FinTsDataElement SystemId => SecurityHeader.Fields[3].Elements[2];

    public static FinTsPinTanEnvelope Parse(FinTsMessageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        var outer = frame.Syntax.Segments;
        if (outer.Count != 4 || outer[1].Code != "HNVSK" || outer[2].Code != "HNVSD") { throw Unsupported(); }
        var header = outer[1];
        var data = outer[2];
        if (header.Version != 3 || data.Version != 1) { throw Unsupported(); }
        if (header.Reference is not null || data.Reference is not null || header.Fields.Count is < 8 or > 9 ||
            data.Fields.Count != 1 || data.Fields[0].Elements.Count != 1 || !data.Fields[0].Elements[0].IsBinary) { throw Invalid(); }

        var fields = header.Fields;
        var profile = fields[0].Elements;
        if (profile.Count != 2) { throw Invalid(); }
        if (Text(profile[0], 3) != "PIN") { throw Unsupported(); }
        string profileVersion = Text(profile[1], 3);
        if (profileVersion is not ("1" or "2")) { throw Unsupported(); }
        if (Scalar(fields[1], 3) != "998" || Scalar(fields[7], 3) != "0") { throw Unsupported(); }
        if (Scalar(fields[2], 3) is not ("1" or "4")) { throw Invalid(); }
        if (fields.Count == 9 && !ParameterFields.Empty(fields[8])) { throw Invalid(); }

        var identity = fields[3].Elements;
        if (identity.Count != 3 || Text(identity[0], 3) is not ("1" or "2") ||
            identity[1].IsBinary || !identity[1].IsEmpty) { throw Invalid(); }
        _ = Text(identity[2], 30);

        var timestamp = fields[4].Elements;
        if (timestamp.Count is < 1 or > 3 || Text(timestamp[0], 3) != "1") { throw Invalid(); }
        string date = timestamp.Count > 1 ? Text(timestamp[1], 8, false) : "";
        string time = timestamp.Count > 2 ? Text(timestamp[2], 6, false) : "";
        if (date.Length != 0 && (date.Length != 8 || !DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) ||
            time.Length != 0 && (date.Length == 0 || time.Length != 6 || !TimeOnly.TryParseExact(time, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) { throw Invalid(); }

        var algorithm = fields[5].Elements;
        if (algorithm.Count is < 6 or > 7 || Text(algorithm[0], 3) != "2" || Text(algorithm[1], 3) is not ("2" or "18" or "19") ||
            Text(algorithm[2], 3) is not ("13" or "14") || !algorithm[3].IsBinary || algorithm[3].CopyValueBytes().Length is < 1 or > 512 ||
            Text(algorithm[5], 3) != "1") { throw Invalid(); }
        // PIN/TAN specifies a filler, not a cryptographic key or an algorithm selection.
        string keyIdentifier = Text(algorithm[4], 3);
        if (keyIdentifier.Any(c => c is < '0' or > '9') ||
            algorithm.Count == 7 && (algorithm[6].IsBinary || !algorithm[6].IsEmpty)) { throw Invalid(); }

        var key = fields[6].Elements;
        if (key.Count != 6) { throw Invalid(); }
        string country = Text(key[0], 3);
        if (country.Length != 3 || country.Any(c => c is < '0' or > '9')) { throw Invalid(); }
        _ = Text(key[1], 30, false);
        _ = Text(key[2], 30);
        if (Text(key[3], 1) != "V") { throw Invalid(); }
        Number(key[4]);
        Number(key[5]);

        if (data.Fields[0].Elements[0].IsEmpty) { throw Invalid(); }
        var body = FinTsSyntax.ParseSegments(data.Fields[0].Elements[0].CopyValueBytes(), cancellationToken);
        if (body.Segments.Count == 0 || outer[^1].Number != body.Segments.Count + 2) { throw Invalid(); }
        int elements = frame.Syntax.ElementCount + body.ElementCount;
        if (elements > FinTsSyntax.MaximumElements || outer.Count + body.Segments.Count > FinTsSyntax.MaximumSegments)
        { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        for (int index = 0; index < body.Segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = body.Segments[index];
            if (segment.Number != index + 2) { throw Invalid(); }
            if (segment.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA" || segment.Code.StartsWith("HK", StringComparison.Ordinal)) { throw Unsupported(); }
            if (segment.Code is "HNHBK" or "HNHBS") { throw Invalid(); }
        }
        return new(frame, body, profileVersion == "1" ? 1 : 2);
    }

    private static string Scalar(FinTsField field, int length)
    {
        if (field.Elements.Count != 1) { throw Invalid(); }
        return Text(field.Elements[0], length);
    }
    private static string Text(FinTsDataElement element, int maximum, bool required = true)
    {
        if (element.IsBinary) { throw Invalid(); }
        string value = element.HeaderText();
        if (value.Length > maximum || required && value.Length == 0 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return value;
    }
    private static void Number(FinTsDataElement element)
    {
        string value = Text(element, 3);
        if (value.Any(c => c is < '0' or > '9') || value.Length > 1 && value[0] == '0') { throw Invalid(); }
    }
    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidSecurityEnvelope);
    private static FinTsFormatException Unsupported() => new(FinTsSyntaxError.UnsupportedSecurityWrapper);
}
