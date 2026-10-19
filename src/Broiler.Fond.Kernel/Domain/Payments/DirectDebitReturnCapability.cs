namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Advertised capability of an institution or account to execute SEPA direct-debit returns (HKDRW)
/// and mandate management/revocation (HKDMB).
/// </summary>
public sealed record DirectDebitReturnCapability(
    bool IsSupported,
    IReadOnlySet<DirectDebitReturnReason> SupportedReasons,
    bool SupportsMandateRevocation,
    int MaxAdvanceNoticeDays = 0)
{
    private static readonly HashSet<DirectDebitReturnReason> AllStandardReasons =
    [
        DirectDebitReturnReason.DebtorRequestUnconditional,
        DirectDebitReturnReason.DebtorRequestGeneral,
        DirectDebitReturnReason.NoValidMandate,
        DirectDebitReturnReason.DisputedTransaction,
    ];

    /// <summary>
    /// Standard full capability matching standard FinTS 3.0 HKDRW and HKDMB support.
    /// </summary>
    public static DirectDebitReturnCapability StandardDefault => new(
        IsSupported: true,
        SupportedReasons: AllStandardReasons,
        SupportsMandateRevocation: true,
        MaxAdvanceNoticeDays: 30);

    /// <summary>
    /// Unsupported capability when neither HIDRWS nor HIDMBS is advertised.
    /// </summary>
    public static DirectDebitReturnCapability Unsupported => new(
        IsSupported: false,
        SupportedReasons: new HashSet<DirectDebitReturnReason>(),
        SupportsMandateRevocation: false,
        MaxAdvanceNoticeDays: 0);

    /// <summary>
    /// Checks if a specific return reason is supported by this capability.
    /// </summary>
    public bool IsReasonSupported(DirectDebitReturnReason reason) =>
        IsSupported && SupportedReasons.Contains(reason);
}
