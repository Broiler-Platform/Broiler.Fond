using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Untrusted HIPBS BPD parameter advertisement indicating whether an institution
/// advertises SEPA Instant Credit Transfer (HKIPB) capabilities.
/// </summary>
public sealed class FinTsInstantPaymentAdvertisement
{
    public const string ParameterSegmentCode = "HIPBS";

    public FinTsSegment Source { get; }
    public int Version => Source.Version;
    public int MaximumOrders { get; }
    public int MinimumSignatures { get; }
    public int SecurityClass { get; }
    public decimal? MaximumAmount { get; }

    internal FinTsInstantPaymentAdvertisement(FinTsSegment source)
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

        // Parse optional parameter fields if present
        if (source.Fields.Count > 3 && source.Fields[3].Elements.Count > 0 && !source.Fields[3].Elements[0].IsEmpty)
        {
            string amountText = source.Fields[3].Elements[0].HeaderText().Replace(',', '.');
            if (decimal.TryParse(amountText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal maxAmt) && maxAmt > 0)
            {
                MaximumAmount = maxAmt;
            }
        }
    }

    /// <summary>
    /// Converts this advertisement into a validated <see cref="InstantPaymentCapability"/> domain record.
    /// </summary>
    public InstantPaymentCapability ToCapability() =>
        new(
            IsSupported: true,
            MaximumAmount: MaximumAmount ?? 100000.00m,
            EstimatedExecutionSeconds: 10,
            FeeNotice: null,
            SupportedSegmentCode: "HKIPB",
            SupportedSegmentVersion: Version);

    /// <summary>
    /// Parses any HIPBS advertisements found in a parameter set.
    /// Returns empty list if unadvertised.
    /// </summary>
    public static IReadOnlyList<FinTsInstantPaymentAdvertisement> ParseAll(FinTsParameterSet parameterSet)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);

        List<FinTsInstantPaymentAdvertisement> ads = [];
        var allSegments = parameterSet.UninterpretedSegments.Concat(parameterSet.Source.BodySegments);
        HashSet<FinTsSegment> seen = [];

        foreach (var segment in allSegments)
        {
            if (segment.Code == ParameterSegmentCode && seen.Add(segment))
            {
                ads.Add(new FinTsInstantPaymentAdvertisement(segment));
            }
        }

        return ads.AsReadOnly();
    }
}
