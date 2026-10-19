using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions.Sync;

/// <summary>
/// Immutable sync checkpoint tracking statement synchronization range,
/// pagination progress, and completion status for an account binding.
/// </summary>
public sealed class StatementSyncCheckpoint
{
    public StatementSyncCheckpoint(
        AccountId accountId,
        AccountIncarnationId accountIncarnationId,
        AccountBindingId accountBindingId,
        DateOnly windowStart,
        DateOnly windowEnd,
        DateTimeOffset lastSyncTimestamp,
        string? lastContinuationToken = null,
        bool isComplete = false,
        int observedTransactionCount = 0)
    {
        if (accountId.Value == 0UL || accountIncarnationId.Value == 0UL || accountBindingId.Value == 0UL)
        {
            throw new ArgumentException("Identities must be nonzero.");
        }

        if (windowStart > windowEnd)
        {
            throw new ArgumentException("Window start cannot be after window end.", nameof(windowStart));
        }

        if (observedTransactionCount < 0)
        {
            throw new ArgumentException("Observed transaction count cannot be negative.", nameof(observedTransactionCount));
        }

        AccountId = accountId;
        AccountIncarnationId = accountIncarnationId;
        AccountBindingId = accountBindingId;
        WindowStart = windowStart;
        WindowEnd = windowEnd;
        LastSyncTimestamp = lastSyncTimestamp;
        LastContinuationToken = string.IsNullOrWhiteSpace(lastContinuationToken) ? null : lastContinuationToken.Trim();
        IsComplete = isComplete;
        ObservedTransactionCount = observedTransactionCount;
    }

    public AccountId AccountId { get; }
    public AccountIncarnationId AccountIncarnationId { get; }
    public AccountBindingId AccountBindingId { get; }
    public DateOnly WindowStart { get; }
    public DateOnly WindowEnd { get; }
    public DateTimeOffset LastSyncTimestamp { get; }
    public string? LastContinuationToken { get; }
    public bool IsComplete { get; }
    public int ObservedTransactionCount { get; }

    public StatementSyncCheckpoint WithContinuation(string continuationToken, int additionalCount, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(continuationToken);
        return new StatementSyncCheckpoint(
            AccountId,
            AccountIncarnationId,
            AccountBindingId,
            WindowStart,
            WindowEnd,
            timestamp,
            continuationToken,
            isComplete: false,
            ObservedTransactionCount + additionalCount);
    }

    public StatementSyncCheckpoint WithCompletion(DateTimeOffset timestamp, int finalTotalCount)
    {
        return new StatementSyncCheckpoint(
            AccountId,
            AccountIncarnationId,
            AccountBindingId,
            WindowStart,
            WindowEnd,
            timestamp,
            lastContinuationToken: null,
            isComplete: true,
            finalTotalCount);
    }
}
