namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Domain validator for SEPA direct-debit return requests and mandate revocations.
/// </summary>
public static class DirectDebitReturnValidator
{
    public const int MaxCreditorIdLength = 35;
    public const int MaxMandateIdLength = 35;

    /// <summary>
    /// Validates a SEPA direct-debit return request against bank capability, statutory deadlines,
    /// and domain invariants.
    /// </summary>
    public static SepaValidationResult Validate(
        DirectDebitReturnRequest request,
        DirectDebitReturnCapability capability,
        DateOnly evaluationDate,
        bool isSafeModeActive = false)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];

        // 1. Safe mode guard
        if (isSafeModeActive)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitReturnSafeModeActive,
                nameof(isSafeModeActive),
                "Read-only safe mode is active; payment writes and direct-debit returns are disabled."));
            return SepaValidationResult.Failed(errors);
        }

        // 2. Capability check
        if (!capability.IsSupported)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitReturnCapabilityNotSupported,
                nameof(capability),
                "The selected institution does not support SEPA direct-debit returns via FinTS (HKDRW)."));
            return SepaValidationResult.Failed(errors);
        }

        // 3. Return reason check
        if (!capability.IsReasonSupported(request.Reason))
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitReturnReasonNotSupported,
                nameof(request.Reason),
                $"The return reason '{request.Reason}' ({request.Reason.ToReasonCode()}) is not advertised by the bank."));
        }

        // 4. Statutory deadline check
        if (!DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(request.Reason, request.OriginalBookingDate, evaluationDate))
        {
            int allowedDays = DirectDebitStatutoryDeadline.GetAllowedDays(request.Reason);
            DateOnly deadline = DirectDebitStatutoryDeadline.GetDeadlineDate(request.Reason, request.OriginalBookingDate);
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitStatutoryDeadlineExceeded,
                nameof(request.OriginalBookingDate),
                $"The statutory deadline for return reason '{request.Reason}' has expired (allowed: {allowedDays} days, expired on {deadline:yyyy-MM-dd}, evaluated as of {evaluationDate:yyyy-MM-dd})."));
        }

        // 5. Creditor identifier check
        if (string.IsNullOrWhiteSpace(request.CreditorId) ||
            request.CreditorId.Length < 5 ||
            request.CreditorId.Length > MaxCreditorIdLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitInvalidCreditorId,
                nameof(request.CreditorId),
                $"Creditor identifier must be between 5 and {MaxCreditorIdLength} characters."));
        }

        // 6. Mandate reference check
        if (string.IsNullOrWhiteSpace(request.MandateId) ||
            request.MandateId.Length > MaxMandateIdLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitMissingMandateId,
                nameof(request.MandateId),
                $"Mandate reference is required and must not exceed {MaxMandateIdLength} characters."));
        }

        // 7. Amount checks
        if (request.Amount.Currency != "EUR")
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidCurrency,
                nameof(request.Amount.Currency),
                "Direct-debit returns must be in EUR currency."));
        }

        if (request.Amount.Amount <= 0m)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.InvalidAmount,
                nameof(request.Amount.Amount),
                "Direct-debit return amount must be strictly positive."));
        }

        if (decimal.Round(request.Amount.Amount, 2) != request.Amount.Amount)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.AmountExceedsScale,
                nameof(request.Amount.Amount),
                "Amount cannot have more than 2 decimal places."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }

    /// <summary>
    /// Validates a mandate revocation notice against bank capability and safe mode.
    /// </summary>
    public static SepaValidationResult ValidateMandateRevocation(
        string creditorId,
        string mandateId,
        DirectDebitReturnCapability capability,
        bool isSafeModeActive = false)
    {
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];

        if (isSafeModeActive)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitReturnSafeModeActive,
                nameof(isSafeModeActive),
                "Read-only safe mode is active; mandate actions are disabled."));
            return SepaValidationResult.Failed(errors);
        }

        if (!capability.SupportsMandateRevocation)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitMandateRevocationNotSupported,
                nameof(capability),
                "The selected institution does not advertise SEPA mandate management/revocation (HKDMB)."));
            return SepaValidationResult.Failed(errors);
        }

        if (string.IsNullOrWhiteSpace(creditorId) || creditorId.Length < 5 || creditorId.Length > MaxCreditorIdLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitInvalidCreditorId,
                nameof(creditorId),
                $"Creditor identifier must be between 5 and {MaxCreditorIdLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(mandateId) || mandateId.Length > MaxMandateIdLength)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.DirectDebitMissingMandateId,
                nameof(mandateId),
                $"Mandate reference is required and must not exceed {MaxMandateIdLength} characters."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }
}
