namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Specific error codes for SEPA payment validation failures.
/// </summary>
public enum SepaValidationErrorCode
{
    None = 0,
    InvalidCurrency = 1,
    InvalidAmount = 2,
    AmountExceedsScale = 3,
    MissingBeneficiaryName = 4,
    BeneficiaryNameTooLong = 5,
    InvalidIban = 6,
    RemittanceTooLong = 7,
    ExecutionDateInPast = 8,
    InstantAmountExceedsLimit = 9,
    InstantExecutionDateNotToday = 10,
    InstantCapabilityNotSupported = 11,
    OwnAccountSourceEqualsDestination = 12,
    ScheduledCapabilityNotSupported = 13,
    ScheduledDateInPastOrToday = 14,
    ScheduledDateOutsideLeadWindow = 15,
    ScheduledCancellationNotPermitted = 16,
    StandingOrderCapabilityNotSupported = 17,
    StandingOrderFrequencyNotSupported = 18,
    StandingOrderInvalidExecutionDay = 19,
    StandingOrderFirstExecutionDateInPast = 20,
    StandingOrderLastExecutionDateBeforeFirst = 21,
    StandingOrderActionNotPermitted = 22,
    DirectDebitReturnCapabilityNotSupported = 23,
    DirectDebitReturnReasonNotSupported = 24,
    DirectDebitStatutoryDeadlineExceeded = 25,
    DirectDebitInvalidCreditorId = 26,
    DirectDebitMissingMandateId = 27,
    DirectDebitMandateRevocationNotSupported = 28,
    DirectDebitReturnSafeModeActive = 29,
}

/// <summary>
/// Structured validation error describing a failed validation check.
/// </summary>
public readonly record struct SepaValidationError(
    SepaValidationErrorCode Code,
    string PropertyName,
    string Message);

/// <summary>
/// Immutable result of validating a SEPA payment draft revision.
/// </summary>
public sealed record SepaValidationResult(
    bool IsValid,
    IReadOnlyList<SepaValidationError> Errors)
{
    public static readonly SepaValidationResult Success = new(true, Array.Empty<SepaValidationError>());

    public static SepaValidationResult Failed(List<SepaValidationError> errors) =>
        new(false, errors.AsReadOnly());
}

/// <summary>
/// Strict domain validator ensuring that a SEPA credit transfer draft revision complies with
/// EPC SEPA Credit Transfer Scheme rules and Broiler Fond safety invariants.
/// </summary>
public static class SepaPaymentDraftValidator
{
    public const int MaxBeneficiaryNameLength = 70;
    public const int MaxRemittanceLength = 140;

    public static SepaValidationResult Validate(
        PaymentDraftRevision revision,
        DateOnly? evaluationDate = null) =>
        Validate(revision, instantCapability: null, scheduledCapability: null, evaluationDate: evaluationDate);

    public static SepaValidationResult Validate(
        PaymentDraftRevision revision,
        InstantPaymentCapability? instantCapability,
        DateOnly? evaluationDate = null) =>
        Validate(revision, instantCapability, scheduledCapability: null, evaluationDate: evaluationDate);

    public static SepaValidationResult Validate(
        PaymentDraftRevision revision,
        InstantPaymentCapability? instantCapability,
        ScheduledPaymentCapability? scheduledCapability,
        DateOnly? evaluationDate = null)
    {
        ArgumentNullException.ThrowIfNull(revision);

        List<SepaValidationError> errors = [];

        // 1. Currency validation
        if (!string.Equals(revision.Amount.Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidCurrency,
                nameof(PaymentDraftRevision.Amount),
                $"SEPA payments must be denominated in EUR; found '{revision.Amount.Currency}'."));
        }

        // 2. Amount validation
        if (revision.Amount.Amount <= 0m)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidAmount,
                nameof(PaymentDraftRevision.Amount),
                "Payment amount must be strictly greater than 0.00 EUR."));
        }
        else if (decimal.Round(revision.Amount.Amount, 2) != revision.Amount.Amount)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.AmountExceedsScale,
                nameof(PaymentDraftRevision.Amount),
                "Payment amount cannot specify fractions of a cent (maximum 2 decimal places)."));
        }

        // 3. Beneficiary validation
        if (string.IsNullOrWhiteSpace(revision.Beneficiary.Name))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.MissingBeneficiaryName,
                $"{nameof(PaymentDraftRevision.Beneficiary)}.{nameof(PaymentBeneficiary.Name)}",
                "Beneficiary name is required."));
        }
        else if (revision.Beneficiary.Name.Length > MaxBeneficiaryNameLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.BeneficiaryNameTooLong,
                $"{nameof(PaymentDraftRevision.Beneficiary)}.{nameof(PaymentBeneficiary.Name)}",
                $"Beneficiary name exceeds the maximum length of {MaxBeneficiaryNameLength} characters."));
        }

        // 4. IBAN validation
        if (!SepaIbanValidator.Validate(revision.Beneficiary.Iban, out _))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidIban,
                $"{nameof(PaymentDraftRevision.Beneficiary)}.{nameof(PaymentBeneficiary.Iban)}",
                "Beneficiary IBAN is invalid, has an unsupported format, or failed MOD 97-10 checksum."));
        }

        // 5. Remittance validation
        if (revision.Remittance.UnstructuredText is not null &&
            revision.Remittance.UnstructuredText.Length > MaxRemittanceLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.RemittanceTooLong,
                $"{nameof(PaymentDraftRevision.Remittance)}.{nameof(PaymentRemittance.UnstructuredText)}",
                $"Remittance text exceeds the maximum allowed length of {MaxRemittanceLength} characters."));
        }

        // 6. Execution Date validation
        DateOnly today = evaluationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (revision.ExecutionDate < today)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.ExecutionDateInPast,
                nameof(PaymentDraftRevision.ExecutionDate),
                $"Execution date '{revision.ExecutionDate:yyyy-MM-dd}' cannot be in the past (reference date '{today:yyyy-MM-dd}')."));
        }

        // 7. Instant payment specific validation
        if (revision.ExecutionMode == PaymentExecutionMode.InstantCreditTransfer)
        {
            if (instantCapability is null || !instantCapability.IsSupported)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.InstantCapabilityNotSupported,
                    nameof(PaymentDraftRevision.ExecutionMode),
                    "SEPA Instant Credit Transfer (SCT Inst) is not supported by the debit institution."));
            }
            else if (revision.Amount.Amount > instantCapability.MaximumAmount)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.InstantAmountExceedsLimit,
                    nameof(PaymentDraftRevision.Amount),
                    $"Instant payment amount {revision.Amount.Amount:N2} EUR exceeds the bank limit of {instantCapability.MaximumAmount:N2} EUR."));
            }

            if (revision.ExecutionDate != today)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.InstantExecutionDateNotToday,
                    nameof(PaymentDraftRevision.ExecutionDate),
                    $"SEPA Instant Credit Transfers execute in real time and must have execution date '{today:yyyy-MM-dd}' (found '{revision.ExecutionDate:yyyy-MM-dd}')."));
            }
        }

        // 8. Scheduled payment specific validation
        if (revision.ExecutionMode == PaymentExecutionMode.ScheduledCreditTransfer)
        {
            if (scheduledCapability is null || !scheduledCapability.IsSupported)
            {
                errors.Add(new SepaValidationError(
                    SepaValidationErrorCode.ScheduledCapabilityNotSupported,
                    nameof(PaymentDraftRevision.ExecutionMode),
                    "Scheduled SEPA Credit Transfer (Terminüberweisung) is not supported by the debit institution."));
            }
            else
            {
                if (revision.ExecutionDate <= today)
                {
                    errors.Add(new SepaValidationError(
                        SepaValidationErrorCode.ScheduledDateInPastOrToday,
                        nameof(PaymentDraftRevision.ExecutionDate),
                        $"Scheduled execution date '{revision.ExecutionDate:yyyy-MM-dd}' must be strictly in the future (reference date '{today:yyyy-MM-dd}')."));
                }
                else
                {
                    DateOnly minDate = today.AddDays(scheduledCapability.MinimumLeadDays);
                    DateOnly maxDate = today.AddDays(scheduledCapability.MaximumLeadDays);

                    if (revision.ExecutionDate < minDate || revision.ExecutionDate > maxDate)
                    {
                        errors.Add(new SepaValidationError(
                            SepaValidationErrorCode.ScheduledDateOutsideLeadWindow,
                            nameof(PaymentDraftRevision.ExecutionDate),
                            $"Scheduled execution date '{revision.ExecutionDate:yyyy-MM-dd}' is outside the bank's allowed advance window (between {minDate:yyyy-MM-dd} and {maxDate:yyyy-MM-dd})."));
                    }
                }
            }
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }
}
