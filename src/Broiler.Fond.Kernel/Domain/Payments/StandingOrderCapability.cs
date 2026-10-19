namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Bank-advertised parameter data describing support and rules for SEPA standing orders (Daueraufträge).
/// </summary>
public sealed record StandingOrderCapability(
    bool IsSupported,
    bool CanCreate,
    bool CanModify,
    bool CanDelete,
    IReadOnlyList<StandingOrderFrequency> SupportedFrequencies,
    int MinimumLeadDays,
    string SupportedSegmentCode,
    int SupportedSegmentVersion)
{
    public static readonly StandingOrderCapability Unsupported = new(
        IsSupported: false,
        CanCreate: false,
        CanModify: false,
        CanDelete: false,
        SupportedFrequencies: Array.Empty<StandingOrderFrequency>(),
        MinimumLeadDays: 0,
        SupportedSegmentCode: "HKCDB",
        SupportedSegmentVersion: 1);

    public static readonly StandingOrderCapability StandardDefault = new(
        IsSupported: true,
        CanCreate: true,
        CanModify: true,
        CanDelete: true,
        SupportedFrequencies:
        [
            StandingOrderFrequency.Monthly,
            StandingOrderFrequency.TwoMonthly,
            StandingOrderFrequency.Quarterly,
            StandingOrderFrequency.SemiAnnually,
            StandingOrderFrequency.Annually
        ],
        MinimumLeadDays: 1,
        SupportedSegmentCode: "HKCDB",
        SupportedSegmentVersion: 1);
}
