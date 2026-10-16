namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Immutable remittance and payment references accompanying a transaction.
/// </summary>
public sealed class RemittanceInformation
{
    public const int MaximumReferenceLength = 35;
    public const int MaximumPurposeCodeLength = 4;
    public const int MaximumUnstructuredTextLength = 1024;

    public RemittanceInformation(
        string? unstructuredText,
        string? endToEndId = null,
        string? mandateId = null,
        string? creditorId = null,
        string? customerReference = null,
        string? bankReference = null,
        string? purposeCode = null)
    {
        string? normalizedText = string.IsNullOrWhiteSpace(unstructuredText) ? null : unstructuredText.Trim();
        string? normalizedE2E = string.IsNullOrWhiteSpace(endToEndId) ? null : endToEndId.Trim();
        string? normalizedMandate = string.IsNullOrWhiteSpace(mandateId) ? null : mandateId.Trim();
        string? normalizedCreditor = string.IsNullOrWhiteSpace(creditorId) ? null : creditorId.Trim();
        string? normalizedCustomerRef = string.IsNullOrWhiteSpace(customerReference) ? null : customerReference.Trim();
        string? normalizedBankRef = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
        string? normalizedPurpose = string.IsNullOrWhiteSpace(purposeCode) ? null : purposeCode.Trim().ToUpperInvariant();

        if (normalizedText?.Length > MaximumUnstructuredTextLength)
        {
            throw new ArgumentException($"Unstructured remittance text exceeds {MaximumUnstructuredTextLength} characters.", nameof(unstructuredText));
        }

        if (normalizedE2E?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"End-to-end ID exceeds {MaximumReferenceLength} characters.", nameof(endToEndId));
        }

        if (normalizedMandate?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Mandate ID exceeds {MaximumReferenceLength} characters.", nameof(mandateId));
        }

        if (normalizedCreditor?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Creditor ID exceeds {MaximumReferenceLength} characters.", nameof(creditorId));
        }

        if (normalizedCustomerRef?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Customer reference exceeds {MaximumReferenceLength} characters.", nameof(customerReference));
        }

        if (normalizedBankRef?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Bank reference exceeds {MaximumReferenceLength} characters.", nameof(bankReference));
        }

        if (normalizedPurpose?.Length > MaximumPurposeCodeLength)
        {
            throw new ArgumentException($"Purpose code exceeds {MaximumPurposeCodeLength} characters.", nameof(purposeCode));
        }

        UnstructuredText = normalizedText;
        EndToEndId = normalizedE2E;
        MandateId = normalizedMandate;
        CreditorId = normalizedCreditor;
        CustomerReference = normalizedCustomerRef;
        BankReference = normalizedBankRef;
        PurposeCode = normalizedPurpose;
    }

    public string? UnstructuredText { get; }
    public string? EndToEndId { get; }
    public string? MandateId { get; }
    public string? CreditorId { get; }
    public string? CustomerReference { get; }
    public string? BankReference { get; }
    public string? PurposeCode { get; }
}
