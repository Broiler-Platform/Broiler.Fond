namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Status of a scheduled SEPA credit transfer order at the bank.
/// </summary>
public enum ScheduledOrderStatus
{
    /// <summary>
    /// Registered at the bank, awaiting future execution date.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Execution date reached and order executed/booked at the bank.
    /// </summary>
    Executed = 2,

    /// <summary>
    /// Cancellation request submitted, awaiting bank confirmation.
    /// </summary>
    CancellationPending = 3,

    /// <summary>
    /// Bank confirmed cancellation prior to execution.
    /// </summary>
    Cancelled = 4,
}

/// <summary>
/// Domain entity representing a scheduled SEPA credit transfer instruction recorded at a bank.
/// </summary>
public sealed record ScheduledOrder(
    ulong Id,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    string BankOrderReference,
    string EndToEndId,
    Money Amount,
    PaymentBeneficiary Beneficiary,
    PaymentRemittance Remittance,
    DateOnly ScheduledExecutionDate,
    ScheduledOrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null)
{
    public bool CanCancel => Status == ScheduledOrderStatus.Pending;
}
