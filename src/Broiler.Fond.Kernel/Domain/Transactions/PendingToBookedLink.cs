using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Defines an immutable correlation record between a pending authorization
/// and a subsequent booked transaction, preserving provenance without double-counting.
/// </summary>
public sealed class PendingToBookedLink
{
    public const int MaximumCriterionLength = 128;

    public PendingToBookedLink(
        TransactionId pendingTransactionId,
        TransactionId bookedTransactionId,
        DateTimeOffset linkedAt,
        string matchCriterion)
    {
        if (pendingTransactionId.Value == 0UL || bookedTransactionId.Value == 0UL)
        {
            throw new ArgumentException("Transaction identities must be nonzero.");
        }

        if (pendingTransactionId == bookedTransactionId)
        {
            throw new ArgumentException("A pending transaction cannot link to itself.", nameof(bookedTransactionId));
        }

        if (string.IsNullOrWhiteSpace(matchCriterion) || matchCriterion.Length > MaximumCriterionLength)
        {
            throw new ArgumentException($"Match criterion must be non-empty and at most {MaximumCriterionLength} characters.", nameof(matchCriterion));
        }

        PendingTransactionId = pendingTransactionId;
        BookedTransactionId = bookedTransactionId;
        LinkedAt = linkedAt;
        MatchCriterion = matchCriterion.Trim();
    }

    public TransactionId PendingTransactionId { get; }
    public TransactionId BookedTransactionId { get; }
    public DateTimeOffset LinkedAt { get; }
    public string MatchCriterion { get; }
}
