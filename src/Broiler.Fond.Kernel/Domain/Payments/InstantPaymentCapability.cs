namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Bank-advertised parameter data describing support and limits for SEPA Instant Credit Transfers (SCT Inst).
/// </summary>
public sealed record InstantPaymentCapability(
    bool IsSupported,
    decimal MaximumAmount,
    int EstimatedExecutionSeconds,
    string? FeeNotice,
    string SupportedSegmentCode,
    int SupportedSegmentVersion)
{
    public static readonly InstantPaymentCapability Unsupported = new(
        IsSupported: false,
        MaximumAmount: 0m,
        EstimatedExecutionSeconds: 0,
        FeeNotice: null,
        SupportedSegmentCode: "HKIPB",
        SupportedSegmentVersion: 1);

    public static readonly InstantPaymentCapability StandardEpcDefault = new(
        IsSupported: true,
        MaximumAmount: 100000.00m,
        EstimatedExecutionSeconds: 10,
        FeeNotice: null,
        SupportedSegmentCode: "HKIPB",
        SupportedSegmentVersion: 1);
}
