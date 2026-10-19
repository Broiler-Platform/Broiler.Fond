using System.Globalization;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Standard SEPA direct-debit return reason codes.
/// </summary>
public enum DirectDebitReturnReason
{
    /// <summary>
    /// MS02: Refusal by the Debtor (unconditional 8-week consumer return right under § 675x Abs. 2 BGB / PSD2 Art. 76).
    /// </summary>
    DebtorRequestUnconditional = 1,

    /// <summary>
    /// MS03: Reason not specified / debtor cancellation.
    /// </summary>
    DebtorRequestGeneral = 2,

    /// <summary>
    /// MD01: No valid mandate / unauthorized transaction (13-month refund right under § 676b BGB / PSD2 Art. 71).
    /// </summary>
    NoValidMandate = 3,

    /// <summary>
    /// MD06: Disputed authorized transaction / refund requested by customer.
    /// </summary>
    DisputedTransaction = 4,
}

public static class DirectDebitReturnReasonExtensions
{
    public const string CodeMs02 = "MS02";
    public const string CodeMs03 = "MS03";
    public const string CodeMd01 = "MD01";
    public const string CodeMd06 = "MD06";

    public static string ToReasonCode(this DirectDebitReturnReason reason) => reason switch
    {
        DirectDebitReturnReason.DebtorRequestUnconditional => CodeMs02,
        DirectDebitReturnReason.DebtorRequestGeneral => CodeMs03,
        DirectDebitReturnReason.NoValidMandate => CodeMd01,
        DirectDebitReturnReason.DisputedTransaction => CodeMd06,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown direct-debit return reason."),
    };

    public static DirectDebitReturnReason FromReasonCode(string code) => code?.Trim().ToUpperInvariant() switch
    {
        CodeMs02 => DirectDebitReturnReason.DebtorRequestUnconditional,
        CodeMs03 => DirectDebitReturnReason.DebtorRequestGeneral,
        CodeMd01 => DirectDebitReturnReason.NoValidMandate,
        CodeMd06 => DirectDebitReturnReason.DisputedTransaction,
        _ => throw new ArgumentException($"Unsupported direct-debit return reason code: {code}", nameof(code)),
    };

    public static bool IsAuthorizedReturn(this DirectDebitReturnReason reason) =>
        reason != DirectDebitReturnReason.NoValidMandate;
}

/// <summary>
/// Execution and reconciliation status of a direct-debit return request.
/// </summary>
public enum DirectDebitReturnStatus
{
    /// <summary>
    /// Request created locally, pending user authorization or network transmission.
    /// </summary>
    PendingSubmission = 1,

    /// <summary>
    /// Bank accepted the return instruction; awaiting statement reversal credit entry.
    /// </summary>
    SubmittedAwaitingStatement = 2,

    /// <summary>
    /// Reconciled with an authoritative bank statement credit entry.
    /// </summary>
    Reconciled = 3,

    /// <summary>
    /// Bank authoritatively rejected the return request.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// Network connection indeterminate after possible transmission; requires statement reconciliation.
    /// </summary>
    Unknown = 5,
}

/// <summary>
/// Represents a direct-debit return instruction initiated by the account holder.
/// </summary>
public sealed record DirectDebitReturnRequest(
    long Id,
    long SourceAccountId,
    long SourceAccountBindingId,
    long SourceAccountIncarnationId,
    string OriginalBankReference,
    string CreditorId,
    string MandateId,
    string CreditorName,
    string CreditorIban,
    Money Amount,
    DateOnly OriginalBookingDate,
    DateOnly OriginalDueDate,
    DirectDebitReturnReason Reason,
    DirectDebitReturnStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt = null)
{
    public bool CanSubmit => Status == DirectDebitReturnStatus.PendingSubmission;
    public bool IsTerminal => Status is DirectDebitReturnStatus.Reconciled or DirectDebitReturnStatus.Rejected;
}
