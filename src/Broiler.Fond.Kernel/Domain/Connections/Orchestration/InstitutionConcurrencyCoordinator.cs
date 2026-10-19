using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Coordinates execution across multiple bank institutions and connections.
/// Invariants:
/// 1. Dialogues to the same connection are strictly serialized (1 at a time).
/// 2. Dialogues to the same institution (or BLZ/endpoint) are strictly serialized (1 at a time).
/// 3. Cross-institution dialogues run concurrently up to <see cref="MaxConcurrentInstitutions"/>.
/// </summary>
public sealed class InstitutionConcurrencyCoordinator : IDisposable
{
    public const int DefaultMaxConcurrentInstitutions = 3;
    public const int MinimumMaxConcurrentInstitutions = 1;
    public const int MaximumMaxConcurrentInstitutions = 8;

    private readonly SemaphoreSlim _globalThrottle;
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _institutionLocks = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _connectionLocks = new();
    private bool _isDisposed;

    public int MaxConcurrentInstitutions { get; }

    public InstitutionConcurrencyCoordinator(int maxConcurrentInstitutions = DefaultMaxConcurrentInstitutions)
    {
        if (maxConcurrentInstitutions is < MinimumMaxConcurrentInstitutions or > MaximumMaxConcurrentInstitutions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxConcurrentInstitutions),
                $"Max concurrent institutions must be between {MinimumMaxConcurrentInstitutions} and {MaximumMaxConcurrentInstitutions}.");
        }

        MaxConcurrentInstitutions = maxConcurrentInstitutions;
        _globalThrottle = new SemaphoreSlim(maxConcurrentInstitutions, maxConcurrentInstitutions);
    }

    /// <summary>
    /// Executes the specified asynchronous action within the scoped concurrency boundaries of the specified institution and connection.
    /// </summary>
    public async Task<T> ExecuteScopedAsync<T>(
        InstitutionId institutionId,
        ConnectionId connectionId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfDisposed();

        SemaphoreSlim connectionLock = _connectionLocks.GetOrAdd(connectionId.Value, static _ => new SemaphoreSlim(1, 1));
        SemaphoreSlim institutionLock = _institutionLocks.GetOrAdd(institutionId.Value, static _ => new SemaphoreSlim(1, 1));

        bool acquiredConnection = false;
        bool acquiredInstitution = false;
        bool acquiredGlobal = false;

        try
        {
            await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquiredConnection = true;

            await institutionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquiredInstitution = true;

            await _globalThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquiredGlobal = true;

            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (acquiredGlobal)
            {
                _globalThrottle.Release();
            }

            if (acquiredInstitution)
            {
                institutionLock.Release();
            }

            if (acquiredConnection)
            {
                connectionLock.Release();
            }
        }
    }

    /// <summary>
    /// Executes the specified asynchronous action within the scoped concurrency boundaries of the specified institution and connection.
    /// </summary>
    public async Task ExecuteScopedAsync(
        InstitutionId institutionId,
        ConnectionId connectionId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await ExecuteScopedAsync(
            institutionId,
            connectionId,
            async ct =>
            {
                await action(ct).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _globalThrottle.Dispose();

        foreach (var sem in _institutionLocks.Values)
        {
            sem.Dispose();
        }

        foreach (var sem in _connectionLocks.Values)
        {
            sem.Dispose();
        }

        _institutionLocks.Clear();
        _connectionLocks.Clear();
    }
}
