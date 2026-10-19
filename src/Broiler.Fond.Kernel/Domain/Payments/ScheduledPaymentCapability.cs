namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Bank-advertised parameter data describing support and advance notice limits for scheduled SEPA Credit Transfers (HKCME).
/// </summary>
public sealed record ScheduledPaymentCapability(
    bool IsSupported,
    int MinimumLeadDays,
    int MaximumLeadDays,
    bool CanModify,
    bool CanCancel,
    string SupportedSegmentCode,
    int SupportedSegmentVersion)
{
    public static readonly ScheduledPaymentCapability Unsupported = new(
        IsSupported: false,
        MinimumLeadDays: 0,
        MaximumLeadDays: 0,
        CanModify: false,
        CanCancel: false,
        SupportedSegmentCode: "HKCME",
        SupportedSegmentVersion: 1);

    public static readonly ScheduledPaymentCapability StandardDefault = new(
        IsSupported: true,
        MinimumLeadDays: 1,
        MaximumLeadDays: 365,
        CanModify: true,
        CanCancel: true,
        SupportedSegmentCode: "HKCME",
        SupportedSegmentVersion: 1);
}
