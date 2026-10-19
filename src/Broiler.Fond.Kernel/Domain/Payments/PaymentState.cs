namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Canonical lifecycle state of a payment instruction or attempt per the normative roadmap state machine.
/// </summary>
public enum PaymentState
{
    /// <summary>Editable local intent; nothing sent.</summary>
    Draft = 1,

    /// <summary>Local and capability checks passed.</summary>
    Validated = 2,

    /// <summary>The bank has not returned a final Verification of Payee (VoP) result.</summary>
    PayeeCheckPending = 3,

    /// <summary>Supplied name and account match.</summary>
    PayeeMatch = 4,

    /// <summary>Bank supplied a close match and any suggested corrected name.</summary>
    PayeeCloseMatch = 5,

    /// <summary>Bank supplied a mismatch warning.</summary>
    PayeeNoMatch = 6,

    /// <summary>The operation is outside the applicable VoP scope, with the reason recorded.</summary>
    PayeeCheckNotApplicable = 7,

    /// <summary>VoP could not produce a result; required bank warning is retained.</summary>
    PayeeCheckUnavailable = 8,

    /// <summary>Awaiting the bank's Strong Customer Authentication (SCA/TAN) flow.</summary>
    AwaitingAuthorization = 9,

    /// <summary>The write boundary may be crossed; order is in flight.</summary>
    Submitting = 10,

    /// <summary>Bank accepted the order; settlement is not yet inferred.</summary>
    Accepted = 11,

    /// <summary>Bank reports ongoing processing.</summary>
    BankPending = 12,

    /// <summary>
    /// Connection failed after the order may have reached the bank.
    /// Durable and user-visible; never automatically retried.
    /// </summary>
    Unknown = 13,

    /// <summary>Terminal: This attempt ended before submission. Retry creates a new attempt from the draft.</summary>
    AuthorizationCancelled = 14,

    /// <summary>Terminal: Bank authoritatively rejected this attempt. Retry creates a new attempt from the draft.</summary>
    Rejected = 15,

    /// <summary>Terminal: Later authoritative order or booking evidence resolved the attempt.</summary>
    Reconciled = 16
}
