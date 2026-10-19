namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable value object representing a payment recipient/beneficiary in SEPA credit transfers.
/// </summary>
public sealed record PaymentBeneficiary
{
    public string Name { get; }
    public string Iban { get; }
    public string? Bic { get; }

    public PaymentBeneficiary(string? name, string? iban, string? bic = null)
    {
        Name = (name ?? string.Empty).Trim();
        Iban = (iban ?? string.Empty).Trim().ToUpperInvariant();
        Bic = string.IsNullOrWhiteSpace(bic) ? null : bic.Trim().ToUpperInvariant();
    }
}
