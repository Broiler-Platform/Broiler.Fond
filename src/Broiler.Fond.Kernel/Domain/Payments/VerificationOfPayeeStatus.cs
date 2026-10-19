namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Domain states for pre-authorization Verification of Payee (VoP) per Regulation (EU) 2024/886.
/// </summary>
public enum VerificationOfPayeeStatus
{
    /// <summary>
    /// Verification has been requested and is awaiting authoritative bank response.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// Beneficiary name matches the registered account holder for the provided IBAN.
    /// </summary>
    Match = 2,

    /// <summary>
    /// Close match: minor differences detected (e.g. typos, ordering); bank may supply a suggested corrected name.
    /// </summary>
    CloseMatch = 3,

    /// <summary>
    /// Mismatch: supplied name does not match the account holder for this IBAN. A prominent warning is required.
    /// </summary>
    NoMatch = 4,

    /// <summary>
    /// VoP check is not applicable (e.g. out of scope, own-account transfer, or unadvertised by institution).
    /// </summary>
    NotApplicable = 5,

    /// <summary>
    /// Verification service or directory is temporarily unavailable. Statutory warning must be retained.
    /// </summary>
    Unavailable = 6,
}
