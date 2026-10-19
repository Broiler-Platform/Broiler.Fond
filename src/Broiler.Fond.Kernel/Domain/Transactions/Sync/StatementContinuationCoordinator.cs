using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions.Sync;

/// <summary>
/// Status of a multi-page statement synchronization dialogue.
/// </summary>
public enum StatementPaginationStatus
{
    Active = 1,
    Completed = 2,
    PageLimitReached = 3,
    DeadlineExpired = 4,
    Interrupted = 5,
    Failed = 6,
}

/// <summary>
/// Coordinates multi-page continuation tokens, enforces page budgets (<= 128 pages),
/// monitors monotonic deadlines, and preserves incomplete checkpoints for resume.
/// </summary>
public sealed class StatementContinuationCoordinator
{
    public const int MaximumPages = 128;
    public const int DefaultTimeoutSeconds = 120;

    private readonly List<string> _consumedPageTokens = [];
    private int _pageCount;

    public StatementContinuationCoordinator(
        AccountId accountId,
        AccountBindingId accountBindingId,
        DateTimeOffset deadline,
        string? initialContinuationToken = null)
    {
        if (accountId.Value == 0UL || accountBindingId.Value == 0UL)
        {
            throw new ArgumentException("Identities must be nonzero.");
        }

        AccountId = accountId;
        AccountBindingId = accountBindingId;
        Deadline = deadline;
        CurrentContinuationToken = string.IsNullOrWhiteSpace(initialContinuationToken) ? null : initialContinuationToken.Trim();
        Status = StatementPaginationStatus.Active;
    }

    public AccountId AccountId { get; }
    public AccountBindingId AccountBindingId { get; }
    public DateTimeOffset Deadline { get; }
    public string? CurrentContinuationToken { get; private set; }
    public int PageCount => _pageCount;
    public StatementPaginationStatus Status { get; private set; }
    public bool CanContinue => Status == StatementPaginationStatus.Active && CurrentContinuationToken != null;
    public ReadOnlyCollection<string> ConsumedPageTokens => _consumedPageTokens.AsReadOnly();

    /// <summary>
    /// Records page retrieval and evaluates continuation tokens or completion indicators.
    /// </summary>
    public void RecordPageResponse(
        bool hasMoreEntries,
        string? nextContinuationToken,
        DateTimeOffset currentTimestamp,
        bool hasTerminalError = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Status != StatementPaginationStatus.Active)
        {
            throw new InvalidOperationException($"Cannot record page response in terminal state: {Status}");
        }

        _pageCount++;
        if (CurrentContinuationToken != null)
        {
            _consumedPageTokens.Add(CurrentContinuationToken);
        }

        if (hasTerminalError)
        {
            Status = StatementPaginationStatus.Failed;
            CurrentContinuationToken = null;
            return;
        }

        if (currentTimestamp >= Deadline)
        {
            Status = StatementPaginationStatus.DeadlineExpired;
            // Retain the next continuation token so caller can resume later
            CurrentContinuationToken = nextContinuationToken;
            return;
        }

        if (!hasMoreEntries || string.IsNullOrWhiteSpace(nextContinuationToken))
        {
            Status = StatementPaginationStatus.Completed;
            CurrentContinuationToken = null;
            return;
        }

        if (_pageCount >= MaximumPages)
        {
            Status = StatementPaginationStatus.PageLimitReached;
            CurrentContinuationToken = nextContinuationToken.Trim();
            return;
        }

        CurrentContinuationToken = nextContinuationToken.Trim();
    }

    /// <summary>
    /// Marks the session interrupted (e.g. user cancellation, network loss).
    /// Preserves the current continuation token for later resumption.
    /// </summary>
    public void MarkInterrupted()
    {
        if (Status == StatementPaginationStatus.Active)
        {
            Status = StatementPaginationStatus.Interrupted;
        }
    }
}
