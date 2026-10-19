namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Untrusted HIVOPS BPD parameter advertisement indicating whether an institution
/// advertises Verification of Payee (HKVOP) capabilities per Regulation (EU) 2024/886.
/// </summary>
public sealed class FinTsVopAdvertisement
{
    public const string ParameterSegmentCode = "HIVOPS";

    public FinTsSegment Source { get; }
    public int Version => Source.Version;
    public int MaximumOrders { get; }
    public int MinimumSignatures { get; }
    public int SecurityClass { get; }

    internal FinTsVopAdvertisement(FinTsSegment source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Code != ParameterSegmentCode)
        {
            throw new ArgumentException($"Expected segment {ParameterSegmentCode}, found {source.Code}.", nameof(source));
        }

        Source = source;
        MaximumOrders = ParameterFields.Number(source.Fields[0], 3) ?? 1;
        MinimumSignatures = ParameterFields.Number(source.Fields[1], 1) ?? 0;
        SecurityClass = ParameterFields.Number(source.Fields[2], 1) ?? 0;
    }

    /// <summary>
    /// Parses any HIVOPS advertisements found in a parameter set.
    /// Returns empty list if unadvertised.
    /// </summary>
    public static IReadOnlyList<FinTsVopAdvertisement> ParseAll(FinTsParameterSet parameterSet)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);

        List<FinTsVopAdvertisement> ads = [];
        var allSegments = parameterSet.UninterpretedSegments.Concat(parameterSet.Source.BodySegments);
        HashSet<FinTsSegment> seen = [];

        foreach (var segment in allSegments)
        {
            if (segment.Code == ParameterSegmentCode && seen.Add(segment))
            {
                ads.Add(new FinTsVopAdvertisement(segment));
            }
        }

        return ads.AsReadOnly();
    }
}
