namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Defines the classification of a financial transaction.
/// </summary>
public enum TransactionKind
{
    Transfer = 1,
    Debit = 2,
    Credit = 3,
    ReturnDebit = 4,
    Fee = 5,
    Interest = 6,
    CardPayment = 7,
    Other = 8,
}
