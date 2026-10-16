namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Immutable party information for the origin or destination of funds.
/// </summary>
public sealed class Counterparty
{
    public const int MaximumNameLength = 140;
    public const int MaximumIbanLength = 34;
    public const int MaximumBicLength = 11;
    public const int MaximumAccountFieldLength = 35;

    public Counterparty(
        string? name,
        string? iban = null,
        string? bic = null,
        string? accountNumber = null,
        string? bankCode = null)
    {
        string? normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        string? normalizedIban = string.IsNullOrWhiteSpace(iban) ? null : iban.Replace(" ", "").ToUpperInvariant();
        string? normalizedBic = string.IsNullOrWhiteSpace(bic) ? null : bic.Replace(" ", "").ToUpperInvariant();
        string? normalizedAccountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim();
        string? normalizedBankCode = string.IsNullOrWhiteSpace(bankCode) ? null : bankCode.Trim();

        if (normalizedName?.Length > MaximumNameLength)
        {
            throw new ArgumentException($"Counterparty name exceeds {MaximumNameLength} characters.", nameof(name));
        }

        if (normalizedIban?.Length > MaximumIbanLength)
        {
            throw new ArgumentException($"Counterparty IBAN exceeds {MaximumIbanLength} characters.", nameof(iban));
        }

        if (normalizedBic?.Length > MaximumBicLength)
        {
            throw new ArgumentException($"Counterparty BIC exceeds {MaximumBicLength} characters.", nameof(bic));
        }

        if (normalizedAccountNumber?.Length > MaximumAccountFieldLength)
        {
            throw new ArgumentException($"Counterparty account number exceeds {MaximumAccountFieldLength} characters.", nameof(accountNumber));
        }

        if (normalizedBankCode?.Length > MaximumAccountFieldLength)
        {
            throw new ArgumentException($"Counterparty bank code exceeds {MaximumAccountFieldLength} characters.", nameof(bankCode));
        }

        Name = normalizedName;
        Iban = normalizedIban;
        Bic = normalizedBic;
        AccountNumber = normalizedAccountNumber;
        BankCode = normalizedBankCode;
    }

    public string? Name { get; }
    public string? Iban { get; }
    public string? Bic { get; }
    public string? AccountNumber { get; }
    public string? BankCode { get; }
}
