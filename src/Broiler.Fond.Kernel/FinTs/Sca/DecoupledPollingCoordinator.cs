namespace Broiler.Fond.Kernel.FinTs.Sca;

/// <summary>
/// Status of an active decoupled SCA polling operation.
/// </summary>
public enum DecoupledPollingStatus
{
    /// <summary>Polling loop is waiting for user action in their bank app or next scheduled status query.</summary>
    Waiting,

    /// <summary>Status query was confirmed and terminal execution was reported by the bank.</summary>
    Completed,

    /// <summary>The bank rejected the decoupled approval or user cancelled in their bank app.</summary>
    Rejected,

    /// <summary>The polling operation reached its monotonic deadline before approval.</summary>
    TimedOut,

    /// <summary>The maximum query count was reached without resolution.</summary>
    QueryLimitExceeded,

    /// <summary>The operation was cancelled by the local user or caller.</summary>
    Cancelled,
}

/// <summary>
/// Coordinates timing, query spacing, monotonic deadlines, and query bounds
/// for FinTS 3.0 decoupled / app-approval SCA continuation.
/// </summary>
public sealed class DecoupledPollingCoordinator
{
    public static readonly TimeSpan MinimumQueryInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);
    public const int DefaultMaximumQueries = 32;

    private readonly TimeSpan _timeout;
    private readonly int _maxQueries;
    private readonly TimeProvider _clock;

    private int _queriesExecuted;
    private readonly DateTimeOffset _deadline;

    public DecoupledPollingCoordinator(
        TimeSpan? timeout = null,
        int maxQueries = DefaultMaximumQueries,
        DateTimeOffset? bankExpiry = null,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
        _maxQueries = Math.Clamp(maxQueries, 1, 64);

        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset calculatedDeadline = now + _timeout;

        // Clamp to bank-supplied expiry if earlier than client timeout
        if (bankExpiry.HasValue && bankExpiry.Value > now && bankExpiry.Value < calculatedDeadline)
        {
            _deadline = bankExpiry.Value;
        }
        else
        {
            _deadline = calculatedDeadline;
        }
    }

    public DateTimeOffset Deadline => _deadline;
    public int QueriesExecuted => _queriesExecuted;
    public int MaximumQueries => _maxQueries;

    /// <summary>
    /// Checks whether the deadline or query limit has been exceeded.
    /// </summary>
    public DecoupledPollingStatus CheckStatus(DateTimeOffset now, out TimeSpan remainingTime)
    {
        if (now >= _deadline)
        {
            remainingTime = TimeSpan.Zero;
            return DecoupledPollingStatus.TimedOut;
        }

        remainingTime = _deadline - now;

        if (_queriesExecuted >= _maxQueries)
        {
            return DecoupledPollingStatus.QueryLimitExceeded;
        }

        return DecoupledPollingStatus.Waiting;
    }

    /// <summary>
    /// Calculates the delay to wait before sending the next status query,
    /// respecting the minimum interval floor, bank recommendations, and remaining deadline.
    /// </summary>
    public TimeSpan CalculateNextDelay(TimeSpan? bankRecommendedWait, DateTimeOffset now)
    {
        if (now >= _deadline)
        {
            return TimeSpan.Zero;
        }

        TimeSpan delay = MinimumQueryInterval;
        if (bankRecommendedWait.HasValue && bankRecommendedWait.Value > delay)
        {
            delay = bankRecommendedWait.Value;
        }

        TimeSpan remaining = _deadline - now;
        if (delay > remaining)
        {
            delay = remaining;
        }

        return delay;
    }

    /// <summary>
    /// Records a sent status query and checks if the query budget allows continuation.
    /// </summary>
    public bool RecordQuery(out int currentQueryCount)
    {
        _queriesExecuted++;
        currentQueryCount = _queriesExecuted;
        return _queriesExecuted <= _maxQueries;
    }
}
