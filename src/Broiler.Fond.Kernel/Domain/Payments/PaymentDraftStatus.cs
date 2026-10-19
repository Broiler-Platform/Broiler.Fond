namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Status of a local payment draft.
/// </summary>
public enum PaymentDraftStatus
{
    /// <summary>
    /// Editable local draft; validation not yet performed or failed.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Local validation passed; ready for Verification of Payee or authorization review.
    /// </summary>
    Validated = 2,

    /// <summary>
    /// Superseded or cancelled draft.
    /// </summary>
    Archived = 3,
}
