using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Defines an immutable bank-reported revision of a financial transaction.
/// Captures verbatim bank evidence without allowing in-place mutations.
/// </summary>
public sealed class TransactionRevision
{
    public const int MaximumReturnReasonCodeLength = 16;
    public const int MaximumRawBankCodeLength = 32;

    public TransactionRevision(
        TransactionRevisionId id,
        TransactionId transactionId,
        ObservationId observationId,
        AccountId accountId,
        AccountIncarnationId accountIncarnationId,
        AccountBindingId accountBindingId,
        TransactionStatus status,
        TransactionKind kind,
        Money amount,
        bool isCredit,
        DateOnly bookingDate,
        DateOnly? valueDate,
        DateTimeOffset bankTimestamp,
        DateTimeOffset retrievedAt,
        Counterparty? counterparty = null,
        RemittanceInformation? remittance = null,
        TransactionRevisionId? supersedesId = null,
        string? returnReasonCode = null,
        string? rawBankCode = null)
    {
        ulong[] requiredIds = [id.Value, transactionId.Value, observationId.Value, accountId.Value, accountIncarnationId.Value, accountBindingId.Value];
        if (requiredIds.Contains(0UL))
        {
            throw new ArgumentException("Required transaction identities must be nonzero.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentException("A valid transaction status is required.", nameof(status));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("A valid transaction kind is required.", nameof(kind));
        }

        ArgumentNullException.ThrowIfNull(amount);

        if (amount.Amount < 0m)
        {
            throw new ArgumentException("Transaction magnitude must be non-negative; use isCredit to denote flow direction.", nameof(amount));
        }

        if (supersedesId.HasValue && supersedesId.Value.Value == 0UL)
        {
            throw new ArgumentException("Supersedes ID must be nonzero when specified.", nameof(supersedesId));
        }

        if (supersedesId.HasValue && supersedesId.Value == id)
        {
            throw new ArgumentException("A transaction revision cannot supersede itself.", nameof(supersedesId));
        }

        if (returnReasonCode?.Length > MaximumReturnReasonCodeLength)
        {
            throw new ArgumentException($"Return reason code exceeds {MaximumReturnReasonCodeLength} characters.", nameof(returnReasonCode));
        }

        if (rawBankCode?.Length > MaximumRawBankCodeLength)
        {
            throw new ArgumentException($"Raw bank code exceeds {MaximumRawBankCodeLength} characters.", nameof(rawBankCode));
        }

        Id = id;
        TransactionId = transactionId;
        ObservationId = observationId;
        AccountId = accountId;
        AccountIncarnationId = accountIncarnationId;
        AccountBindingId = accountBindingId;
        Status = status;
        Kind = kind;
        Amount = amount;
        IsCredit = isCredit;
        BookingDate = bookingDate;
        ValueDate = valueDate ?? bookingDate;
        BankTimestamp = bankTimestamp;
        RetrievedAt = retrievedAt;
        Counterparty = counterparty;
        Remittance = remittance;
        SupersedesId = supersedesId;
        ReturnReasonCode = string.IsNullOrWhiteSpace(returnReasonCode) ? null : returnReasonCode.Trim().ToUpperInvariant();
        RawBankCode = string.IsNullOrWhiteSpace(rawBankCode) ? null : rawBankCode.Trim();
    }

    public TransactionRevisionId Id { get; }
    public TransactionId TransactionId { get; }
    public ObservationId ObservationId { get; }
    public AccountId AccountId { get; }
    public AccountIncarnationId AccountIncarnationId { get; }
    public AccountBindingId AccountBindingId { get; }
    public TransactionRevisionId? SupersedesId { get; }
    public TransactionStatus Status { get; }
    public TransactionKind Kind { get; }
    public Money Amount { get; }
    public bool IsCredit { get; }
    public DateOnly BookingDate { get; }
    public DateOnly ValueDate { get; }
    public DateTimeOffset BankTimestamp { get; }
    public DateTimeOffset RetrievedAt { get; }
    public Counterparty? Counterparty { get; }
    public RemittanceInformation? Remittance { get; }
    public string? ReturnReasonCode { get; }
    public string? RawBankCode { get; }
}
