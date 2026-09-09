using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Untrusted HITAN evidence. Raw text/markup and binary challenge data must not be executed or logged.</summary>
public sealed class FinTsTanChallenge
{
    internal FinTsTanChallenge(FinTsSegment source) => Source = source;
    public FinTsSegment Source { get; }
    public int RequestSegmentNumber => Source.Reference!.Value;
    public string Process => Source.Fields[0].Elements[0].HeaderText();
    public FinTsDataElement? OrderHash => Element(1);
    public FinTsDataElement? OrderReference => Element(2);
    public FinTsDataElement? Text => Element(3);
    public FinTsDataElement? HhdData => Element(4);
    /// <summary>Raw local date/time without a timezone; no trusted expiry decision has been made.</summary>
    public FinTsField? ValidUntil => Source.Fields.Count > 5 ? Source.Fields[5] : null;
    public FinTsDataElement? MediumName => Element(6);
    /// <summary>The literal placeholder alone never proves an SCA exemption or completion.</summary>
    public bool HasNoReferencePlaceholder => OrderReference is { IsBinary: false } reference && reference.HeaderText() == "noref";
    private FinTsDataElement? Element(int index) => Source.Fields.Count > index ? Source.Fields[index].Elements[0] : null;
}

public sealed class FinTsTanChallengeSet
{
    public const int MaximumChallenges = 32;
    public const int MaximumHhdBytes = 65_536;
    private FinTsTanChallengeSet(FinTsResponse source, List<FinTsTanChallenge> challenges, List<FinTsSegment> unknown)
    {
        Source = source;
        Challenges = challenges.AsReadOnly();
        UninterpretedSegments = unknown.AsReadOnly();
    }
    public FinTsResponse Source { get; }
    public ReadOnlyCollection<FinTsTanChallenge> Challenges { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }
    public bool HasDuplicateRequestReferences => Source.UninterpretedSegments.Where(s => s.Code == "HITAN" && s.Reference is not null)
        .GroupBy(s => s.Reference).Any(g => g.Count() > 1);

    public static FinTsTanChallengeSet Parse(FinTsResponse source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsTanChallenge> challenges = [];
        List<FinTsSegment> unknown = [];
        int count = 0;
        foreach (var segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code != "HITAN") { unknown.Add(segment); continue; }
            if (++count > MaximumChallenges) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (segment.Version is not (6 or 7)) { unknown.Add(segment); continue; }
            Validate(segment);
            challenges.Add(new(segment));
        }
        return new(source, challenges, unknown);
    }

    private static void Validate(FinTsSegment segment)
    {
        var fields = segment.Fields;
        if (segment.Reference is null || fields.Count is < 3 or > 7 || fields.Where((_, i) => i != 5).Any(f => f.Elements.Count != 1)) { throw Invalid(); }
        string process = Text(fields[0].Elements[0], 1, true);
        if (process is not ("1" or "2" or "3" or "4") && !(segment.Version == 7 && process == "S")) { throw Invalid(); }
        bool requiresText = process is "1" or "3" or "4";
        Binary(fields[1].Elements[0], 256);
        if (process != "1" && (fields[1].Elements[0].IsBinary || !fields[1].Elements[0].IsEmpty)) { throw Invalid(); }
        _ = Text(fields[2].Elements[0], 35, process != "1");
        if (requiresText && fields.Count < 4) { throw Invalid(); }
        if (fields.Count > 3) { _ = Text(fields[3].Elements[0], 2048, requiresText); }
        if (fields.Count > 4) { Binary(fields[4].Elements[0], MaximumHhdBytes); }
        if (fields.Count > 5)
        {
            var expiry = fields[5].Elements;
            if (!(expiry.Count is 1 or 2 && ParameterFields.Empty(fields[5])))
            {
                if (expiry.Count != 2) { throw Invalid(); }
                string date = Text(expiry[0], 8, true), time = Text(expiry[1], 6, true);
                if (date.Length != 8 || time.Length != 6 ||
                    !DateOnly.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                    !TimeOnly.TryParseExact(time, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { throw Invalid(); }
            }
        }
        if (fields.Count > 6) { _ = Text(fields[6].Elements[0], 32, false); }
        // Hash presence/mirroring and medium-name requirements need a selected procedure and request context.
    }

    private static void Binary(FinTsDataElement element, int maximum)
    {
        if (element.IsBinary)
        {
            int length = element.CopyValueBytes().Length;
            if (length == 0 || length > maximum) { throw Invalid(); }
        }
        else if (!element.IsEmpty) { throw Invalid(); }
    }
    private static string Text(FinTsDataElement element, int maximum, bool required)
    {
        if (element.IsBinary) { throw Invalid(); }
        string value = element.HeaderText();
        if (value.Length > maximum || required && value.Length == 0 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return value;
    }
    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidTanChallenge);
}
