namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable result of a pre-authorization Verification of Payee (VoP) check per Regulation (EU) 2024/886.
/// Retains exact bank-returned match indicators, corrected names, and non-blocking warnings.
/// </summary>
public sealed record VerificationOfPayeeResult
{
    public VerificationOfPayeeStatus Status { get; }
    public string SuppliedName { get; }
    public string? CorrectedName { get; }
    public string? WarningMessage { get; }
    public string? RawBankCode { get; }
    public DateTimeOffset CheckedAt { get; }

    /// <summary>
    /// Gets whether this VoP outcome requires explicit user review and acknowledgment before authorization.
    /// In accordance with Regulation (EU) 2024/886, close matches, mismatches, and unavailable checks require user acknowledgment.
    /// </summary>
    public bool RequiresExplicitAcknowledgment =>
        Status is VerificationOfPayeeStatus.CloseMatch
               or VerificationOfPayeeStatus.NoMatch
               or VerificationOfPayeeStatus.Unavailable;

    /// <summary>
    /// Gets whether this payment can proceed to authorization review after user acknowledgment where required.
    /// (Mismatch alone does not legally forbid a consumer from authorizing, provided warnings were presented).
    /// </summary>
    public bool CanProceed =>
        Status is not VerificationOfPayeeStatus.Pending;

    public VerificationOfPayeeResult(
        VerificationOfPayeeStatus status,
        string suppliedName,
        string? correctedName = null,
        string? warningMessage = null,
        string? rawBankCode = null,
        DateTimeOffset? checkedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suppliedName);

        Status = status;
        SuppliedName = suppliedName.Trim();
        CorrectedName = string.IsNullOrWhiteSpace(correctedName) ? null : correctedName.Trim();
        WarningMessage = string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage.Trim();
        RawBankCode = string.IsNullOrWhiteSpace(rawBankCode) ? null : rawBankCode.Trim();
        CheckedAt = checkedAt ?? DateTimeOffset.UtcNow;
    }

    public static VerificationOfPayeeResult CreateMatch(string suppliedName, DateTimeOffset? checkedAt = null, string? rawBankCode = "MATCH") =>
        new(VerificationOfPayeeStatus.Match, suppliedName, rawBankCode: rawBankCode, checkedAt: checkedAt);

    public static VerificationOfPayeeResult CreateCloseMatch(string suppliedName, string? correctedName, string? warningMessage = null, DateTimeOffset? checkedAt = null, string? rawBankCode = "CLOSE_MATCH") =>
        new(VerificationOfPayeeStatus.CloseMatch, suppliedName, correctedName, warningMessage ?? $"Payee bank suggests corrected name: '{correctedName}'.", rawBankCode, checkedAt);

    public static VerificationOfPayeeResult CreateNoMatch(string suppliedName, string? warningMessage = null, DateTimeOffset? checkedAt = null, string? rawBankCode = "NO_MATCH") =>
        new(VerificationOfPayeeStatus.NoMatch, suppliedName, warningMessage: warningMessage ?? "Payee name does not match the account holder for this IBAN.", rawBankCode: rawBankCode, checkedAt: checkedAt);

    public static VerificationOfPayeeResult CreateUnavailable(string suppliedName, string? warningMessage = null, DateTimeOffset? checkedAt = null, string? rawBankCode = "UNAVAILABLE") =>
        new(VerificationOfPayeeStatus.Unavailable, suppliedName, warningMessage: warningMessage ?? "Payee verification is currently unavailable at the recipient bank. Verify payment details carefully.", rawBankCode: rawBankCode, checkedAt: checkedAt);

    public static VerificationOfPayeeResult CreateNotApplicable(string suppliedName, string? reason = null, DateTimeOffset? checkedAt = null) =>
        new(VerificationOfPayeeStatus.NotApplicable, suppliedName, warningMessage: reason ?? "Verification of payee is not applicable for this transfer.", checkedAt: checkedAt);
}
