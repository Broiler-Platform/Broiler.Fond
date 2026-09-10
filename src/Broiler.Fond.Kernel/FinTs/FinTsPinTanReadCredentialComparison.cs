namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanReadCredentialIssue
{
    None = 0, ContextNeedsReview = 1, EmptyValue = 2, LocalLengthExceeded = 4, InvalidText = 8,
    MissingPinBounds = 16, PinTooShort = 32, PinTooLong = 64, MissingTanBound = 128,
    HipinsTanTooLong = 256, ProcedureTanTooLong = 512, TanFormatMismatch = 1024,
    TanInputNotSupported = 2048, ProcedureRequirementsNeedReview = 4096,
}

/// <summary>Compares one supplied credential against reported first-read requirements without retaining or copying its bytes.
/// None means only that this value fits these local checks, not that all credentials, TAN placement or SCA are satisfied.</summary>
public static class FinTsPinTanReadCredentialComparison
{
    /// <summary>Input is exclusively caller-owned, already encoded in the supported single-byte text subset, and unchanged on every exit.
    /// The caller must clear it. This API neither accesses nor consumes credential owners; the result contains only fixed issue flags.</summary>
    public static FinTsPinTanReadCredentialIssue Compare(FinTsPinTanReadCapabilityContext context, FinTsCredentialKind kind,
        ReadOnlySpan<byte> value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (kind is not (FinTsCredentialKind.Pin or FinTsCredentialKind.Tan)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.HasMatchingEvidence) { return FinTsPinTanReadCredentialIssue.ContextNeedsReview; }
        var requirements = context.Signature.MatchingRequirements!;
        var procedure = context.Signature.MatchingProcedure;
        if (kind == FinTsCredentialKind.Tan && procedure is { IsDecoupled: true }) { return FinTsPinTanReadCredentialIssue.TanInputNotSupported; }
        if (value.IsEmpty) { return FinTsPinTanReadCredentialIssue.EmptyValue; }
        if (value.Length > FinTsSessionCredential.MaximumLength) { return FinTsPinTanReadCredentialIssue.LocalLengthExceeded; }
        var issues = FinTsPinTanReadCredentialIssue.None;
        bool numeric = true;
        foreach (byte octet in value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Same unescaped text subset as the existing local HNSHA encoder; never trim or transcode credentials.
            if (octet < 32 || octet is >= 127 and <= 160) { issues |= FinTsPinTanReadCredentialIssue.InvalidText; }
            if (octet is < (byte)'0' or > (byte)'9') { numeric = false; }
        }
        if (kind == FinTsCredentialKind.Pin)
        {
            if (requirements.MinimumPinLength is null || requirements.MaximumPinLength is null) { issues |= FinTsPinTanReadCredentialIssue.MissingPinBounds; }
            if (value.Length < requirements.MinimumPinLength) { issues |= FinTsPinTanReadCredentialIssue.PinTooShort; }
            if (value.Length > requirements.MaximumPinLength) { issues |= FinTsPinTanReadCredentialIssue.PinTooLong; }
        }
        else
        {
            if (requirements.MaximumTanLength is null) { issues |= FinTsPinTanReadCredentialIssue.MissingTanBound; }
            if (value.Length > requirements.MaximumTanLength) { issues |= FinTsPinTanReadCredentialIssue.HipinsTanTooLong; }
            if (context.Signature.Selection.ProfileVersion == 2)
            {
                if (procedure is null || procedure.MaximumTanLength is null or 0 || procedure.InputFormat is not ("1" or "2"))
                { issues |= FinTsPinTanReadCredentialIssue.ProcedureRequirementsNeedReview; }
                else
                {
                    // Enforce each reported maximum independently; do not select a winner or fill an absent HIPINS bound.
                    if (value.Length > procedure.MaximumTanLength) { issues |= FinTsPinTanReadCredentialIssue.ProcedureTanTooLong; }
                    if (procedure.InputFormat == "1" && !numeric) { issues |= FinTsPinTanReadCredentialIssue.TanFormatMismatch; }
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return issues;
    }
}
