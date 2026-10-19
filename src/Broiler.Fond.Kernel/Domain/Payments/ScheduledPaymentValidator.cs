namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Domain validator for scheduled SEPA credit transfers (Terminüberweisungen) and cancellation lifecycle.
/// </summary>
public static class ScheduledPaymentValidator
{
    public static SepaValidationResult ValidateDraft(
        PaymentDraftRevision revision,
        ScheduledPaymentCapability capability,
        DateOnly? evaluationDate = null) =>
        SepaPaymentDraftValidator.Validate(
            revision,
            instantCapability: null,
            scheduledCapability: capability,
            evaluationDate: evaluationDate);

    public static SepaValidationResult ValidateCancellation(
        ScheduledOrder order,
        ScheduledPaymentCapability capability)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(capability);

        List<SepaValidationError> errors = [];

        if (!capability.IsSupported || !capability.CanCancel)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.ScheduledCancellationNotPermitted,
                nameof(capability.CanCancel),
                "Cancellation of scheduled orders is not supported by the debit institution."));
        }

        if (!order.CanCancel)
        {
            errors.Add(new SepaValidationError(
                SepaValidationErrorCode.ScheduledCancellationNotPermitted,
                nameof(order.Status),
                $"Scheduled order cannot be cancelled because its current status is '{order.Status}' (only Pending orders can be cancelled)."));
        }

        return errors.Count == 0 ? SepaValidationResult.Success : SepaValidationResult.Failed(errors);
    }
}
