using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Immutable snapshot of backoff status for one bank connection.
/// </summary>
public sealed record ConnectionBackoffState(
    ConnectionId ConnectionId,
    int ConsecutiveFailures,
    DateTimeOffset? BackoffUntilUtc,
    string? LastFailureReason,
    DateTimeOffset? LastFailureAtUtc)
{
    public bool IsInBackoff(DateTimeOffset now)
    {
        return BackoffUntilUtc.HasValue && now < BackoffUntilUtc.Value;
    }

    public TimeSpan RemainingBackoff(DateTimeOffset now)
    {
        if (!IsInBackoff(now))
        {
            return TimeSpan.Zero;
        }

        return BackoffUntilUtc!.Value - now;
    }
}

/// <summary>
/// Thread-safe tracker recording connection failures and managing monotonic backoff windows.
/// Prevents hammering bank servers during maintenance, rate limits, or transient network outages.
/// </summary>
public sealed class ConnectionBackoffTracker
{
    private readonly ConcurrentDictionary<ulong, ConnectionBackoffState> _states = new();
    private readonly ConnectionRetryPolicy _policy;

    public ConnectionBackoffTracker(ConnectionRetryPolicy? policy = null)
    {
        _policy = policy ?? ConnectionRetryPolicy.Default;
    }

    /// <summary>
    /// Checks whether backoff is currently active for the specified connection.
    /// </summary>
    public bool IsBackoffActive(ConnectionId connectionId, DateTimeOffset now, out TimeSpan remaining)
    {
        if (_states.TryGetValue(connectionId.Value, out var state) && state.IsInBackoff(now))
        {
            remaining = state.RemainingBackoff(now);
            return true;
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Records a transient failure for the connection and advances the backoff window.
    /// </summary>
    public ConnectionBackoffState RecordTransientFailure(
        ConnectionId connectionId,
        string reason,
        DateTimeOffset now,
        TimeSpan? explicitDelay = null)
    {
        return _states.AddOrUpdate(
            connectionId.Value,
            id =>
            {
                TimeSpan delay = _policy.CalculateBackoff(1, explicitDelay);
                return new ConnectionBackoffState(
                    connectionId,
                    ConsecutiveFailures: 1,
                    BackoffUntilUtc: now.Add(delay),
                    LastFailureReason: reason,
                    LastFailureAtUtc: now);
            },
            (id, existing) =>
            {
                int nextFailures = existing.ConsecutiveFailures + 1;
                TimeSpan delay = _policy.CalculateBackoff(nextFailures, explicitDelay);
                return new ConnectionBackoffState(
                    connectionId,
                    ConsecutiveFailures: nextFailures,
                    BackoffUntilUtc: now.Add(delay),
                    LastFailureReason: reason,
                    LastFailureAtUtc: now);
            });
    }

    /// <summary>
    /// Resets the failure counter and clears active backoff for the connection upon successful contact.
    /// </summary>
    public void Reset(ConnectionId connectionId)
    {
        _states.TryRemove(connectionId.Value, out _);
    }

    /// <summary>
    /// Clears all recorded backoff states across all connections.
    /// </summary>
    public void Clear()
    {
        _states.Clear();
    }

    /// <summary>
    /// Gets the current backoff state for the specified connection, or null if no failures recorded.
    /// </summary>
    public ConnectionBackoffState? GetState(ConnectionId connectionId)
    {
        return _states.TryGetValue(connectionId.Value, out var state) ? state : null;
    }

    /// <summary>
    /// Returns a list of all active or recorded backoff states.
    /// </summary>
    public IReadOnlyList<ConnectionBackoffState> GetAllStates()
    {
        return _states.Values.ToList().AsReadOnly();
    }
}
