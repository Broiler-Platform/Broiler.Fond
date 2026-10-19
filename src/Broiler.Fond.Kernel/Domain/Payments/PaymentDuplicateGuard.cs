using System.Globalization;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Evaluates payment drafts and attempts against existing active and recent payments to detect
/// unintended duplicate transfers before submission.
/// </summary>
public static class PaymentDuplicateGuard
{
    /// <summary>
    /// Default lookback window for reconciled identical payments (48 hours).
    /// </summary>
    public static readonly TimeSpan DefaultReconciledLookbackWindow = TimeSpan.FromHours(48);

    /// <summary>
    /// Checks whether the candidate draft revision matches any active or recently executed payment attempts.
    /// </summary>
    public static PaymentDuplicateCheckResult CheckDuplicate(
        PaymentDraftRevision candidate,
        IEnumerable<PaymentAttempt> existingAttempts,
        IReadOnlyDictionary<ulong, PaymentDraftRevision> revisionsById,
        TimeSpan? reconciledLookbackWindow = null,
        DateTimeOffset? currentTimestamp = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(existingAttempts);
        ArgumentNullException.ThrowIfNull(revisionsById);

        TimeSpan lookback = reconciledLookbackWindow ?? DefaultReconciledLookbackWindow;
        DateTimeOffset now = currentTimestamp ?? DateTimeOffset.UtcNow;

        if (!SepaIbanValidator.TryNormalize(candidate.Beneficiary.Iban, out string candidateIban))
        {
            return PaymentDuplicateCheckResult.Clear;
        }

        foreach (PaymentAttempt attempt in existingAttempts)
        {
            // If the attempt was terminal cancelled or rejected, it did not execute at the bank.
            // Retrying from the draft is normal and safe.
            if (attempt.State is PaymentState.AuthorizationCancelled or PaymentState.Rejected)
            {
                continue;
            }

            if (!revisionsById.TryGetValue(attempt.DraftRevisionId, out PaymentDraftRevision? attemptRev))
            {
                continue;
            }

            if (!IsInstructionMatch(candidate, candidateIban, attemptRev))
            {
                continue;
            }

            // High severity: in-flight submission or durable Unknown
            if (attempt.State is PaymentState.Unknown)
            {
                return PaymentDuplicateCheckResult.DuplicateDetected(
                    matchedAttemptId: attempt.Id,
                    matchedDraftId: attempt.DraftId,
                    state: attempt.State,
                    warningMessage: string.Format(
                        CultureInfo.InvariantCulture,
                        "An unconfirmed payment attempt (Attempt #{0}, EndToEndId: '{1}') is in state 'Unknown'. " +
                        "The bank may have already received and processed this payment. Resubmission is prohibited without explicit reconciliation.",
                        attempt.Id,
                        attempt.EndToEndId),
                    requiresExplicitOverride: true);
            }

            if (attempt.State is PaymentState.Submitting)
            {
                return PaymentDuplicateCheckResult.DuplicateDetected(
                    matchedAttemptId: attempt.Id,
                    matchedDraftId: attempt.DraftId,
                    state: attempt.State,
                    warningMessage: string.Format(
                        CultureInfo.InvariantCulture,
                        "A payment attempt (Attempt #{0}, EndToEndId: '{1}') is currently submitting to the bank. " +
                        "Submitting an identical order risks creating a duplicate debit.",
                        attempt.Id,
                        attempt.EndToEndId),
                    requiresExplicitOverride: true);
            }

            // Medium-High severity: Active attempt in progress
            if (attempt.State is PaymentState.AwaitingAuthorization or PaymentState.BankPending or PaymentState.Accepted)
            {
                return PaymentDuplicateCheckResult.DuplicateDetected(
                    matchedAttemptId: attempt.Id,
                    matchedDraftId: attempt.DraftId,
                    state: attempt.State,
                    warningMessage: string.Format(
                        CultureInfo.InvariantCulture,
                        "An active payment attempt (Attempt #{0}, EndToEndId: '{1}') is currently in state '{2}'. " +
                        "An identical instruction is already in progress.",
                        attempt.Id,
                        attempt.EndToEndId,
                        attempt.State),
                    requiresExplicitOverride: true);
            }

            // Reconciled within lookback window
            if (attempt.State is PaymentState.Reconciled)
            {
                TimeSpan elapsed = now - attempt.UpdatedAt;
                if (elapsed >= TimeSpan.Zero && elapsed <= lookback)
                {
                    return PaymentDuplicateCheckResult.DuplicateDetected(
                        matchedAttemptId: attempt.Id,
                        matchedDraftId: attempt.DraftId,
                        state: attempt.State,
                        warningMessage: string.Format(
                            CultureInfo.InvariantCulture,
                            "An identical payment attempt (Attempt #{0}, EndToEndId: '{1}') was recently reconciled {2:F1} hours ago. " +
                            "Please confirm if you intend to send another identical transfer.",
                            attempt.Id,
                            attempt.EndToEndId,
                            elapsed.TotalHours),
                        requiresExplicitOverride: true);
                }
            }
        }

        return PaymentDuplicateCheckResult.Clear;
    }

    /// <summary>
    /// Checks whether the candidate draft revision matches any existing paired attempts with revisions.
    /// </summary>
    public static PaymentDuplicateCheckResult CheckDuplicate(
        PaymentDraftRevision candidate,
        IEnumerable<(PaymentAttempt Attempt, PaymentDraftRevision Revision)> attemptsWithRevisions,
        TimeSpan? reconciledLookbackWindow = null,
        DateTimeOffset? currentTimestamp = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(attemptsWithRevisions);

        List<PaymentAttempt> attempts = [];
        Dictionary<ulong, PaymentDraftRevision> revDict = [];

        foreach (var (attempt, revision) in attemptsWithRevisions)
        {
            attempts.Add(attempt);
            revDict[attempt.DraftRevisionId] = revision;
        }

        return CheckDuplicate(candidate, attempts, revDict, reconciledLookbackWindow, currentTimestamp);
    }

    /// <summary>
    /// Checks whether the candidate draft revision matches other active, unsubmitted draft revisions.
    /// </summary>
    public static PaymentDuplicateCheckResult CheckDuplicateDrafts(
        PaymentDraftRevision candidate,
        IEnumerable<PaymentDraftRevision> activeDraftRevisions)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(activeDraftRevisions);

        if (!SepaIbanValidator.TryNormalize(candidate.Beneficiary.Iban, out string candidateIban))
        {
            return PaymentDuplicateCheckResult.Clear;
        }

        foreach (PaymentDraftRevision other in activeDraftRevisions)
        {
            if (other.DraftId == candidate.DraftId)
            {
                continue; // Same draft
            }

            if (IsInstructionMatch(candidate, candidateIban, other))
            {
                return PaymentDuplicateCheckResult.DuplicateDetected(
                    matchedAttemptId: null,
                    matchedDraftId: other.DraftId,
                    state: null,
                    warningMessage: string.Format(
                        CultureInfo.InvariantCulture,
                        "Another active draft (Draft #{0}) has identical payment parameters: Source Account #{1}, Beneficiary IBAN '{2}', Amount {3:F2} {4}, Execution Date {5:yyyy-MM-dd}.",
                        other.DraftId,
                        other.SourceAccountId,
                        candidateIban,
                        candidate.Amount.Amount,
                        candidate.Amount.Currency,
                        candidate.ExecutionDate),
                    requiresExplicitOverride: false);
            }
        }

        return PaymentDuplicateCheckResult.Clear;
    }

    private static bool IsInstructionMatch(
        PaymentDraftRevision candidate,
        string candidateIban,
        PaymentDraftRevision other)
    {
        if (candidate.SourceAccountId != other.SourceAccountId)
        {
            return false;
        }

        if (candidate.ExecutionDate != other.ExecutionDate)
        {
            return false;
        }

        if (candidate.Amount.Amount != other.Amount.Amount ||
            !string.Equals(candidate.Amount.Currency, other.Amount.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!SepaIbanValidator.TryNormalize(other.Beneficiary.Iban, out string otherIban))
        {
            return false;
        }

        return string.Equals(candidateIban, otherIban, StringComparison.Ordinal);
    }
}
