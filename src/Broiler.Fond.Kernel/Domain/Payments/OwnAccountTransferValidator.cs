namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Validates own-account internal and cross-institution transfers between accounts
/// verified within the same user profile (Umbuchung).
/// </summary>
public static class OwnAccountTransferValidator
{
    public static SepaValidationResult Validate(
        ulong sourceAccountId,
        ulong destinationAccountId,
        string sourceIban,
        string destinationIban,
        Money amount,
        string? sourceBankCode = null,
        string? destinationBankCode = null)
    {
        List<SepaValidationError> errors = [];

        if (sourceAccountId == destinationAccountId)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.OwnAccountSourceEqualsDestination,
                nameof(destinationAccountId),
                "Source account and destination account must be distinct for an own-account transfer."));
        }

        if (!SepaIbanValidator.Validate(sourceIban, out string normSource))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidIban,
                nameof(sourceIban),
                "Source IBAN is invalid, has an unsupported format, or failed MOD 97-10 checksum."));
        }

        if (!SepaIbanValidator.Validate(destinationIban, out string normDest))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidIban,
                nameof(destinationIban),
                "Destination IBAN is invalid, has an unsupported format, or failed MOD 97-10 checksum."));
        }

        if (!string.IsNullOrEmpty(normSource) && !string.IsNullOrEmpty(normDest) &&
            normSource.Equals(normDest, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.OwnAccountSourceEqualsDestination,
                nameof(destinationIban),
                "Source IBAN and destination IBAN cannot be identical."));
        }

        if (!string.Equals(amount.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidCurrency,
                nameof(amount.Currency),
                $"Own-account transfers must be denominated in EUR; found '{amount.Currency}'."));
        }

        if (amount.Amount <= 0m)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidAmount,
                nameof(amount.Amount),
                "Payment amount must be strictly greater than 0.00 EUR."));
        }
        else if (decimal.Round(amount.Amount, 2) != amount.Amount)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.AmountExceedsScale,
                nameof(amount.Amount),
                "Payment amount cannot specify fractions of a cent (maximum 2 decimal places)."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }

    /// <summary>
    /// Determines whether an own-account transfer is internal (intra-bank within the same institution code)
    /// which can settle without external clearing network routing.
    /// </summary>
    public static bool IsInternalTransfer(string? sourceBankCode, string? destinationBankCode)
    {
        if (string.IsNullOrWhiteSpace(sourceBankCode) || string.IsNullOrWhiteSpace(destinationBankCode))
        {
            return false;
        }

        return string.Equals(sourceBankCode.Trim(), destinationBankCode.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
