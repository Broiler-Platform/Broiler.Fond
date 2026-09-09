using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>One untrusted HITANS procedure. No procedure is selected or permitted by parsing it.</summary>
public sealed class FinTsTanProcedure
{
    internal FinTsTanProcedure(FinTsSegment source, List<FinTsDataElement> elements)
    {
        Source = source;
        Elements = elements.AsReadOnly();
    }
    public FinTsSegment Source { get; }
    public ReadOnlyCollection<FinTsDataElement> Elements { get; }
    public int SegmentVersion => Source.Version;
    public int SecurityFunction => ParameterFields.Number(Elements[0], 3);
    public string ProcessVariant => Elements[1].HeaderText();
    public FinTsDataElement TechnicalId => Elements[2];
    public FinTsDataElement Method => Elements[3];
    public FinTsDataElement MethodVersion => Elements[4];
    public FinTsDataElement Name => Elements[5];
    public int? MaximumTanLength => TanFields.OptionalNumber(Elements, 6, 2);
    public string? InputFormat => Elements[7].IsEmpty ? null : Elements[7].HeaderText();
    public FinTsDataElement ReturnValueLabel => Elements[8];
    public int MaximumReturnValueLength => ParameterFields.Number(Elements[9], 4);
    public bool MultipleTanAllowed => FinTsReadAdvertisement.Flag(Elements[10]);
    public string TimeAndDialogueScope => Elements[11].HeaderText();
    public bool CancellationAllowed => FinTsReadAdvertisement.Flag(Elements[12]);
    public string SmsAccountRequirement => Elements[13].HeaderText();
    public string OrderingAccountRequirement => Elements[14].HeaderText();
    public bool ChallengeClassRequired => FinTsReadAdvertisement.Flag(Elements[15]);
    public bool StructuredChallenge => FinTsReadAdvertisement.Flag(Elements[16]);
    public string InitializationMode => Elements[17].HeaderText();
    public string MediumNameRequirement => Elements[18].HeaderText();
    public bool HhdResponseRequired => FinTsReadAdvertisement.Flag(Elements[19]);
    public int? ActiveMediaCount => TanFields.OptionalNumber(Elements, 20, 1);
    public bool IsDecoupled => SegmentVersion == 7 && Method.HeaderText() is "Decoupled" or "DecoupledPush";
    public int? MaximumStatusQueries => TanFields.OptionalNumber(Elements, 21, 3);
    public int? FirstQueryDelaySeconds => TanFields.OptionalNumber(Elements, 22, 3);
    public int? SubsequentQueryDelaySeconds => TanFields.OptionalNumber(Elements, 23, 3);
    public bool? ManualConfirmationAllowed => TanFields.OptionalFlag(Elements, 24);
    public bool? AutomatedQueriesAllowed => TanFields.OptionalFlag(Elements, 25);
}

public sealed class FinTsTanAdvertisement
{
    internal FinTsTanAdvertisement(FinTsSegment source, List<FinTsTanProcedure> procedures)
    {
        Source = source;
        Procedures = procedures.AsReadOnly();
    }
    public FinTsSegment Source { get; }
    public ReadOnlyCollection<FinTsTanProcedure> Procedures { get; }
    public int MaximumOrders => ParameterFields.Number(Source.Fields[0], 3)!.Value;
    public int MinimumSignatures => ParameterFields.Number(Source.Fields[1], 1)!.Value;
    /// <summary>Uninterpreted PIN/TAN filler.</summary>
    public int SecurityClass => ParameterFields.Number(Source.Fields[2], 1)!.Value;
    public bool OneStepReportedAllowed => FinTsReadAdvertisement.Flag(Source.Fields[3].Elements[0]);
    public bool MultipleTanOrdersReportedAllowed => FinTsReadAdvertisement.Flag(Source.Fields[3].Elements[1]);
    public string OrderHashAlgorithm => Source.Fields[3].Elements[2].HeaderText();
    public bool HasDuplicateSecurityFunctions => Procedures.GroupBy(p => p.SecurityFunction).Any(g => g.Count() > 1);
}

/// <summary>HITANS versions 6/7, with explicit unknown versions and ordered duplicate evidence.</summary>
public sealed class FinTsTanParameterSet
{
    public const int MaximumAdvertisements = 128;
    public const int MaximumProcedures = 1024;
    private FinTsTanParameterSet(FinTsParameterSet source, List<FinTsTanAdvertisement> advertisements, List<FinTsSegment> unknown)
    {
        Source = source;
        Advertisements = advertisements.AsReadOnly();
        UninterpretedSegments = unknown.AsReadOnly();
    }
    public FinTsParameterSet Source { get; }
    public ReadOnlyCollection<FinTsTanAdvertisement> Advertisements { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }
    public bool HasDuplicateAdvertisementVersions => Advertisements.GroupBy(a => a.Source.Version).Any(g => g.Count() > 1);

    public static FinTsTanParameterSet Parse(FinTsParameterSet source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsTanAdvertisement> advertisements = [];
        List<FinTsSegment> unknown = [];
        int count = 0, total = 0;
        foreach (var segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code != "HITANS") { unknown.Add(segment); continue; }
            if (++count > MaximumAdvertisements) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (segment.Version is not (6 or 7)) { unknown.Add(segment); continue; }
            if (segment.Fields.Count != 4) { throw ParameterFields.Invalid(); }
            _ = ParameterFields.Number(segment.Fields[0], 3);
            if (ParameterFields.Number(segment.Fields[1], 1) > 3 || ParameterFields.Number(segment.Fields[2], 1) > 4) { throw ParameterFields.Invalid(); }
            var options = segment.Fields[3].Elements;
            if (options.Count < 23) { throw ParameterFields.Invalid(); }
            _ = FinTsReadAdvertisement.Flag(options[0]);
            _ = FinTsReadAdvertisement.Flag(options[1]);
            TanFields.Code(options[2], "0", "1", "2");
            int width = segment.Version == 6 ? 21 : 26;
            List<FinTsTanProcedure> procedures = [];
            // Repeated DEGs are flattened. Omitted optional fields before another group need placeholders.
            for (int start = 3; start < options.Count; start += width)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = options.Skip(start).Take(width).ToList();
                if (values.Count < 20) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedParameterLayout); }
                ValidateProcedure(values, segment.Version);
                if (++total > MaximumProcedures) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
                procedures.Add(new(segment, values));
            }
            advertisements.Add(new(segment, procedures));
        }
        return new(source, advertisements, unknown);
    }

    private static void ValidateProcedure(IReadOnlyList<FinTsDataElement> values, int version)
    {
        if (ParameterFields.Number(values[0], 3) is < 900 or > 997) { throw ParameterFields.Invalid(); }
        TanFields.Code(values[1], "1", "2");
        ParameterFields.Text(values[2], 30, true);
        ParameterFields.Text(values[3], 32, false);
        ParameterFields.Text(values[4], 10, false);
        ParameterFields.Text(values[5], 30, true);
        bool decoupled = version == 7 && values[3].HeaderText() is "Decoupled" or "DecoupledPush";
        bool polling = version == 7 && values[3].HeaderText() == "Decoupled";
        if (decoupled)
        {
            TanFields.Empty(values[6]);
            TanFields.Empty(values[7]);
        }
        else
        {
            _ = ParameterFields.Number(values[6], 2);
            TanFields.Code(values[7], "1", "2");
        }
        ParameterFields.Text(values[8], 30, true);
        if (ParameterFields.Number(values[9], 4) is < 1 or > 2048) { throw ParameterFields.Invalid(); }
        foreach (int index in new[] { 10, 12, 15, 16, 19 }) { _ = FinTsReadAdvertisement.Flag(values[index]); }
        TanFields.Code(values[11], "1", "2", "3", "4");
        if (values[1].HeaderText() == "1" && values[11].HeaderText() != "4") { throw ParameterFields.Invalid(); }
        TanFields.Code(values[13], "0", "1", "2");
        TanFields.Code(values[14], "0", "2");
        TanFields.Code(values[17], "00", "01", "02");
        TanFields.Code(values[18], "0", "1", "2");
        _ = TanFields.OptionalNumber(values, 20, 1);
        if (polling)
        {
            if (values.Count < 24) { throw ParameterFields.Invalid(); }
            for (int index = 21; index < 24; index++) { _ = ParameterFields.Number(values[index], 3); }
            _ = TanFields.OptionalFlag(values, 24);
            _ = TanFields.OptionalFlag(values, 25);
        }
        else
        {
            foreach (var value in values.Skip(21)) { TanFields.Empty(value); }
        }
    }
}

internal static class TanFields
{
    internal static void Code(FinTsDataElement element, params string[] allowed)
    {
        ParameterFields.Text(element, 3, true);
        if (!allowed.Contains(element.HeaderText(), StringComparer.Ordinal)) { throw ParameterFields.Invalid(); }
    }
    internal static void Empty(FinTsDataElement element)
    {
        if (element.IsBinary || !element.IsEmpty) { throw ParameterFields.Invalid(); }
    }
    internal static int? OptionalNumber(IReadOnlyList<FinTsDataElement> values, int index, int digits)
    {
        if (index >= values.Count) { return null; }
        ParameterFields.Text(values[index], digits, false);
        return values[index].IsEmpty ? null : ParameterFields.Number(values[index], digits);
    }
    internal static bool? OptionalFlag(IReadOnlyList<FinTsDataElement> values, int index)
    {
        if (index >= values.Count) { return null; }
        ParameterFields.Text(values[index], 1, false);
        return values[index].IsEmpty ? null : FinTsReadAdvertisement.Flag(values[index]);
    }
}
