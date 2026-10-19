using System.Globalization;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// A saved beneficiary template / address book entry stored encrypted within the local profile archive.
/// </summary>
public sealed record BeneficiaryTemplate(
    ulong Id,
    string Label,
    string BeneficiaryName,
    string BeneficiaryIban,
    string? BeneficiaryBic,
    string? DefaultRemittanceUnstructured,
    string? DefaultRemittanceCreditorReference,
    Money? DefaultAmount,
    PaymentExecutionMode? DefaultExecutionMode,
    bool IsSoftDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int MaxLabelLength = 70;
    public const int MaxBeneficiaryNameLength = 70;
    public const int MaxRemittanceLength = 140;

    /// <summary>
    /// Applies this template to a payment draft revision by pre-filling the beneficiary, remittance,
    /// and optional amount / execution mode.
    /// Invariant: Applying a template strictly populates draft fields, but never bypasses current
    /// SEPA validation, Verification of Payee (VoP), review, or bank SCA.
    /// </summary>
    public PaymentDraftRevision PopulateDraft(PaymentDraftRevision targetDraft)
    {
        ArgumentNullException.ThrowIfNull(targetDraft);

        PaymentBeneficiary newBeneficiary = new(BeneficiaryName, BeneficiaryIban, BeneficiaryBic);
        PaymentRemittance? newRemittance = targetDraft.Remittance;

        if (!string.IsNullOrWhiteSpace(DefaultRemittanceUnstructured) || !string.IsNullOrWhiteSpace(DefaultRemittanceCreditorReference))
        {
            newRemittance = new PaymentRemittance(DefaultRemittanceUnstructured, DefaultRemittanceCreditorReference);
        }

        Money newAmount = DefaultAmount ?? targetDraft.Amount;
        PaymentExecutionMode newMode = DefaultExecutionMode ?? targetDraft.ExecutionMode;

        return targetDraft with
        {
            Beneficiary = newBeneficiary,
            Remittance = newRemittance,
            Amount = newAmount,
            ExecutionMode = newMode,
        };
    }
}

/// <summary>
/// Domain validator for beneficiary templates ensuring data integrity and valid SEPA identifiers.
/// </summary>
public static class BeneficiaryTemplateValidator
{
    public static SepaValidationResult Validate(BeneficiaryTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<SepaValidationError> errors = [];

        // 1. Label validation
        if (string.IsNullOrWhiteSpace(template.Label))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.None,
                nameof(template.Label),
                "Template label is required."));
        }
        else if (template.Label.Length > BeneficiaryTemplate.MaxLabelLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.None,
                nameof(template.Label),
                $"Template label exceeds maximum of {BeneficiaryTemplate.MaxLabelLength} characters."));
        }

        // 2. Beneficiary name validation
        if (string.IsNullOrWhiteSpace(template.BeneficiaryName))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.MissingBeneficiaryName,
                nameof(template.BeneficiaryName),
                "Beneficiary name is required."));
        }
        else if (template.BeneficiaryName.Length > BeneficiaryTemplate.MaxBeneficiaryNameLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.BeneficiaryNameTooLong,
                nameof(template.BeneficiaryName),
                $"Beneficiary name exceeds maximum of {BeneficiaryTemplate.MaxBeneficiaryNameLength} characters."));
        }

        // 3. IBAN validation
        if (!SepaIbanValidator.Validate(template.BeneficiaryIban, out _))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidIban,
                nameof(template.BeneficiaryIban),
                "Beneficiary IBAN is invalid, has an unsupported format, or failed MOD 97-10 checksum."));
        }

        // 4. BIC validation (optional)
        if (!string.IsNullOrWhiteSpace(template.BeneficiaryBic))
        {
            string bic = template.BeneficiaryBic.Trim();
            if (bic.Length is not (8 or 11))
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.None,
                    nameof(template.BeneficiaryBic),
                    "BIC must be 8 or 11 characters."));
            }
        }

        // 5. Remittance length validation
        if (template.DefaultRemittanceUnstructured is not null &&
            template.DefaultRemittanceUnstructured.Length > BeneficiaryTemplate.MaxRemittanceLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.RemittanceTooLong,
                nameof(template.DefaultRemittanceUnstructured),
                $"Unstructured remittance exceeds maximum of {BeneficiaryTemplate.MaxRemittanceLength} characters."));
        }

        // 6. Default amount validation (optional)
        if (template.DefaultAmount is Money amt)
        {
            if (amt.Currency != "EUR")
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.InvalidCurrency,
                    nameof(template.DefaultAmount),
                    "Template default amount must be denominated in EUR."));
            }

            if (amt.Amount <= 0m)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.InvalidAmount,
                    nameof(template.DefaultAmount),
                    "Template default amount must be strictly greater than 0.00 EUR."));
            }

            if (decimal.Round(amt.Amount, 2) != amt.Amount)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.AmountExceedsScale,
                    nameof(template.DefaultAmount),
                    "Template default amount cannot specify fractions of a cent (maximum 2 decimal places)."));
            }
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }
}
