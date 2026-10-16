namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Defines the processing state of a financial transaction.
/// </summary>
public enum TransactionStatus
{
    Booked = 1,
    Pending = 2,
    Cancelled = 3,
}
