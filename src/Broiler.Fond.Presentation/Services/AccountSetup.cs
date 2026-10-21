using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Presentation.Services;

public sealed record AccountConnectionOption(ulong ConnectionId, string DisplayName);
public sealed record AccountSetupRequest(IReadOnlyList<AccountConnectionOption> Connections);
public sealed record AccountSetupResult(bool Succeeded, ulong ConnectionId, string DisplayName, string Iban, string Currency, bool Cancelled);

internal sealed record ManualAccountInput(string DisplayName, string Iban, string Currency)
{
    public static ManualAccountInput Validate(string displayName, string iban, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Trim().Length > 200)
            throw new ArgumentException("Account name must be 200 characters or fewer.");
        if (iban is null || iban.Length > 128 || !SepaIbanValidator.Validate(iban, out string normalizedIban))
            throw new ArgumentException("Enter a valid IBAN, including its check digits.");
        string normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!CurrencyCatalog.IsRecognized(normalizedCurrency))
            throw new ArgumentException("Enter a recognized currency code, such as EUR or USD.");
        return new(displayName.Trim(), normalizedIban, normalizedCurrency);
    }
}
