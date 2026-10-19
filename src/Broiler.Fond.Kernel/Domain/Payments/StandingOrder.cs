namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Recurrence frequency for a SEPA standing order (Turnus).
/// </summary>
public enum StandingOrderFrequency
{
    /// <summary>
    /// Monthly (monatlich) execution.
    /// </summary>
    Monthly = 1,

    /// <summary>
    /// Every two months (zweimonatlich).
    /// </summary>
    TwoMonthly = 2,

    /// <summary>
    /// Quarterly (vierteljährlich).
    /// </summary>
    Quarterly = 3,

    /// <summary>
    /// Semi-annually (halbjährlich).
    /// </summary>
    SemiAnnually = 6,

    /// <summary>
    /// Annually (jährlich).
    /// </summary>
    Annually = 12,
}

/// <summary>
/// Lifecycle status of a standing order at the bank.
/// </summary>
public enum StandingOrderStatus
{
    /// <summary>
    /// Active and executing on scheduled recurrence dates.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Modification request submitted, awaiting bank confirmation.
    /// </summary>
    ModificationPending = 2,

    /// <summary>
    /// Cancellation/deletion submitted, awaiting bank confirmation.
    /// </summary>
    CancellationPending = 3,

    /// <summary>
    /// Confirmed cancelled/deleted at the bank.
    /// </summary>
    Cancelled = 4,
}

/// <summary>
/// Domain entity representing a SEPA standing order (Dauerauftrag) maintained at a bank.
/// </summary>
public sealed record StandingOrder(
    ulong Id,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    string BankOrderReference,
    Money Amount,
    PaymentBeneficiary Beneficiary,
    PaymentRemittance Remittance,
    StandingOrderFrequency Frequency,
    int ExecutionDay,
    DateOnly FirstExecutionDate,
    DateOnly? LastExecutionDate,
    StandingOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null)
{
    public bool CanModify => Status == StandingOrderStatus.Active;
    public bool CanCancel => Status == StandingOrderStatus.Active;
}
