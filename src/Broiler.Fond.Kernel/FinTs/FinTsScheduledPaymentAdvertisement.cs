using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Untrusted HICMES BPD parameter advertisement indicating whether an institution
/// advertises scheduled SEPA credit transfer (HKCME) capabilities.
/// </summary>
public sealed class FinTsScheduledPaymentAdvertisement
{
    public const string ParameterSegmentCode = "HICMES";

    public FinTsSegment Source { get; }
    public int Version => Source.Version;
    public int MaximumOrders { get; }
    public int MinimumSignatures { get; }
    public int SecurityClass { get; }
    public int MinimumLeadDays { get; }
    public int MaximumLeadDays { get; }
    public bool CanModify { get; }
    public bool CanCancel { get; }

    internal FinTsScheduledPaymentAdvertisement(FinTsSegment source)
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

        int minDays = 1;
        int maxDays = 365;
        bool canMod = true;
        bool canDel = true;

        if (source.Fields.Count > 3 && source.Fields[3].Elements.Count > 0)
        {
            var el = source.Fields[3].Elements;
            if (el.Count > 0 && !el[0].IsEmpty && int.TryParse(el[0].HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pMin))
            {
                minDays = Math.Max(1, pMin);
            }

            if (el.Count > 1 && !el[1].IsEmpty && int.TryParse(el[1].HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pMax))
            {
                maxDays = Math.Max(minDays, pMax);
            }

            if (el.Count > 2 && !el[2].IsEmpty)
            {
                canMod = el[2].HeaderText().Equals("J", StringComparison.OrdinalIgnoreCase);
            }

            if (el.Count > 3 && !el[3].IsEmpty)
            {
                canDel = el[3].HeaderText().Equals("J", StringComparison.OrdinalIgnoreCase);
            }
        }

        MinimumLeadDays = minDays;
        MaximumLeadDays = maxDays;
        CanModify = canMod;
        CanCancel = canDel;
    }

    public ScheduledPaymentCapability ToCapability() =>
        new(
            IsSupported: true,
            MinimumLeadDays: MinimumLeadDays,
            MaximumLeadDays: MaximumLeadDays,
            CanModify: CanModify,
            CanCancel: CanCancel,
            SupportedSegmentCode: "HKCME",
            SupportedSegmentVersion: Version);

    public static IReadOnlyList<FinTsScheduledPaymentAdvertisement> ParseAll(FinTsParameterSet parameterSet)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);

        List<FinTsScheduledPaymentAdvertisement> ads = [];
        var allSegments = parameterSet.UninterpretedSegments.Concat(parameterSet.Source.BodySegments);
        HashSet<FinTsSegment> seen = [];

        foreach (var segment in allSegments)
        {
            if (segment.Code == ParameterSegmentCode && seen.Add(segment))
            {
                ads.Add(new FinTsScheduledPaymentAdvertisement(segment));
            }
        }

        return ads.AsReadOnly();
    }
}
