namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Execution mode for SEPA payment instructions.
/// </summary>
public enum PaymentExecutionMode
{
    /// <summary>
    /// Standard single SEPA credit transfer (SCT).
    /// </summary>
    StandardCreditTransfer = 1,

    /// <summary>
    /// Real-time SEPA Instant Credit Transfer (SCT Inst) executing within seconds.
    /// </summary>
    InstantCreditTransfer = 2,

    /// <summary>
    /// Transfer between accounts owned by the user within the same local profile (Umbuchung).
    /// </summary>
    OwnAccountTransfer = 3,

    /// <summary>
    /// Future-dated scheduled SEPA credit transfer (Terminüberweisung).
    /// </summary>
    ScheduledCreditTransfer = 4,
}
