namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Result of a duplicate payment guard evaluation.
/// </summary>
public sealed record PaymentDuplicateCheckResult(
    bool IsDuplicateSuspected,
    ulong? MatchedAttemptId,
    ulong? MatchedDraftId,
    PaymentState? MatchedAttemptState,
    string? WarningMessage,
    bool RequiresExplicitOverride)
{
    /// <summary>
    /// Indicates no duplicate risk was detected.
    /// </summary>
    public static PaymentDuplicateCheckResult Clear =>
        new(
            IsDuplicateSuspected: false,
            MatchedAttemptId: null,
            MatchedDraftId: null,
            MatchedAttemptState: null,
            WarningMessage: null,
            RequiresExplicitOverride: false);

    /// <summary>
    /// Constructs a result indicating that a potential duplicate payment instruction was identified.
    /// </summary>
    public static PaymentDuplicateCheckResult DuplicateDetected(
        ulong? matchedAttemptId,
        ulong? matchedDraftId,
        PaymentState? state,
        string warningMessage,
        bool requiresExplicitOverride = true) =>
        new(
            IsDuplicateSuspected: true,
            MatchedAttemptId: matchedAttemptId,
            MatchedDraftId: matchedDraftId,
            MatchedAttemptState: state,
            WarningMessage: warningMessage,
            RequiresExplicitOverride: requiresExplicitOverride);
}
