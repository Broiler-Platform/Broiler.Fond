namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Domain validator for SEPA standing orders (Daueraufträge) covering setup, modification, and deletion.
/// </summary>
public static class StandingOrderValidator
{
    public const int MaxBeneficiaryNameLength = 70;
    public const int MaxRemittanceLength = 140;

    /// <summary>
    /// Validates parameters for setting up a new SEPA standing order.
    /// </summary>
    public static SepaValidationResult ValidateSetup(
        Money amount,
        PaymentBeneficiary beneficiary,
        PaymentRemittance remittance,
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
        StandingOrderCapability capability,
        DateOnly? evaluationDate = null)
    {
        ArgumentNullException.ThrowIfNull(beneficiary);
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];
        DateOnly today = evaluationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // 1. Bank capability checks
        if (!capability.IsSupported || !capability.CanCreate)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderCapabilityNotSupported,
                nameof(capability.CanCreate),
                "Creation of standing orders is not supported by the debit institution."));
        }

        if (!capability.SupportedFrequencies.Contains(frequency))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderFrequencyNotSupported,
                nameof(frequency),
                $"Recurrence frequency '{frequency}' is not supported by the bank."));
        }

        // 2. Execution day checks (1..31)
        if (executionDay is < 1 or > 31)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderInvalidExecutionDay,
                nameof(executionDay),
                $"Execution day must be between 1 and 31; found {executionDay}."));
        }

        // 3. Execution date lead time checks
        DateOnly minStartDate = today.AddDays(capability.MinimumLeadDays);
        if (firstExecutionDate < minStartDate)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderFirstExecutionDateInPast,
                nameof(firstExecutionDate),
                $"First execution date '{firstExecutionDate:yyyy-MM-dd}' must be at least {capability.MinimumLeadDays} day(s) in advance (on or after {minStartDate:yyyy-MM-dd})."));
        }

        // 4. Last execution date checks
        if (lastExecutionDate.HasValue && lastExecutionDate.Value <= firstExecutionDate)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderLastExecutionDateBeforeFirst,
                nameof(lastExecutionDate),
                $"Last execution date '{lastExecutionDate.Value:yyyy-MM-dd}' must be strictly after first execution date '{firstExecutionDate:yyyy-MM-dd}'."));
        }

        // 5. Amount & Currency checks
        if (!string.Equals(amount.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidCurrency,
                nameof(amount.Currency),
                $"Standing orders must be denominated in EUR; found '{amount.Currency}'."));
        }

        if (amount.Amount <= 0m)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidAmount,
                nameof(amount.Amount),
                "Standing order amount must be strictly greater than 0.00 EUR."));
        }
        else if (decimal.Round(amount.Amount, 2) != amount.Amount)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.AmountExceedsScale,
                nameof(amount.Amount),
                "Standing order amount cannot specify fractions of a cent (maximum 2 decimal places)."));
        }

        // 6. Beneficiary checks
        if (string.IsNullOrWhiteSpace(beneficiary.Name))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.MissingBeneficiaryName,
                nameof(beneficiary.Name),
                "Beneficiary name is required."));
        }
        else if (beneficiary.Name.Length > MaxBeneficiaryNameLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.BeneficiaryNameTooLong,
                nameof(beneficiary.Name),
                $"Beneficiary name exceeds maximum length of {MaxBeneficiaryNameLength} characters."));
        }

        if (!SepaIbanValidator.Validate(beneficiary.Iban, out _))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidIban,
                nameof(beneficiary.Iban),
                "Beneficiary IBAN is invalid, has an unsupported format, or failed MOD 97-10 checksum."));
        }

        // 7. Remittance checks
        if (remittance is not null && remittance.UnstructuredText is not null &&
            remittance.UnstructuredText.Length > MaxRemittanceLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.RemittanceTooLong,
                nameof(remittance.UnstructuredText),
                $"Remittance text exceeds maximum allowed length of {MaxRemittanceLength} characters."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }

    /// <summary>
    /// Validates parameters for modifying an existing standing order.
    /// </summary>
    public static SepaValidationResult ValidateModification(
        StandingOrder order,
        Money newAmount,
        StandingOrderFrequency newFrequency,
        int newExecutionDay,
        DateOnly? newLastExecutionDate,
        StandingOrderCapability capability,
        DateOnly? evaluationDate = null)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];

        if (!capability.IsSupported || !capability.CanModify)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderCapabilityNotSupported,
                nameof(capability.CanModify),
                "Modification of standing orders is not supported by the debit institution."));
        }

        if (!order.CanModify)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderActionNotPermitted,
                nameof(order.Status),
                $"Standing order cannot be modified because its status is '{order.Status}' (only Active orders can be modified)."));
        }

        if (!capability.SupportedFrequencies.Contains(newFrequency))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderFrequencyNotSupported,
                nameof(newFrequency),
                $"Recurrence frequency '{newFrequency}' is not supported by the bank."));
        }

        if (newExecutionDay is < 1 or > 31)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderInvalidExecutionDay,
                nameof(newExecutionDay),
                $"Execution day must be between 1 and 31; found {newExecutionDay}."));
        }

        if (newLastExecutionDate.HasValue && newLastExecutionDate.Value <= order.FirstExecutionDate)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderLastExecutionDateBeforeFirst,
                nameof(newLastExecutionDate),
                $"Last execution date '{newLastExecutionDate.Value:yyyy-MM-dd}' must be after first execution date '{order.FirstExecutionDate:yyyy-MM-dd}'."));
        }

        if (newAmount.Amount <= 0m || decimal.Round(newAmount.Amount, 2) != newAmount.Amount)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidAmount,
                nameof(newAmount.Amount),
                "New standing order amount must be positive with maximum 2 decimal places."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }

    /// <summary>
    /// Validates whether a standing order can be deleted or cancelled.
    /// </summary>
    public static SepaValidationResult ValidateDeletion(
        StandingOrder order,
        StandingOrderCapability capability)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];

        if (!capability.IsSupported || !capability.CanDelete)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderCapabilityNotSupported,
                nameof(capability.CanDelete),
                "Deletion of standing orders is not supported by the debit institution."));
        }

        if (!order.CanCancel)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.StandingOrderActionNotPermitted,
                nameof(order.Status),
                $"Standing order cannot be deleted because its status is '{order.Status}' (only Active orders can be deleted)."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }
}
