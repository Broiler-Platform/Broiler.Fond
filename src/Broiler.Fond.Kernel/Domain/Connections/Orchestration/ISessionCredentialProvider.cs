using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Provider interface for accessing transient, in-memory banking credentials during an active unlocked session.
/// Invariant: Credentials are never persisted to disk, archives, or logs.
/// </summary>
public interface ISessionCredentialProvider
{
    /// <summary>
    /// Attempts to retrieve the ephemeral PIN for the specified connection.
    /// </summary>
    bool TryGetPin(ConnectionId connectionId, out string? pin);

    /// <summary>
    /// Checks whether an ephemeral credential is registered for the specified connection.
    /// </summary>
    bool HasCredential(ConnectionId connectionId);
}

/// <summary>
/// Thread-safe in-memory store for ephemeral banking credentials during an unlocked user session.
/// Cleared completely upon lock, logout, or disposal.
/// </summary>
public sealed class EphemeralSessionCredentialStore : ISessionCredentialProvider, IDisposable
{
    private readonly ConcurrentDictionary<ulong, char[]> _pins = new();
    private bool _isDisposed;

    /// <summary>
    /// Registers or updates the banking PIN for the specified connection.
    /// </summary>
    public void SetPin(ConnectionId connectionId, string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        ThrowIfDisposed();

        char[] chars = pin.ToCharArray();
        _pins.AddOrUpdate(
            connectionId.Value,
            chars,
            (id, existing) =>
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(existing.AsSpan()));
                Array.Clear(existing);
                return chars;
            });
    }

    /// <summary>
    /// Attempts to retrieve the banking PIN as a transient string.
    /// </summary>
    public bool TryGetPin(ConnectionId connectionId, out string? pin)
    {
        ThrowIfDisposed();

        if (_pins.TryGetValue(connectionId.Value, out var chars))
        {
            pin = new string(chars);
            return true;
        }

        pin = null;
        return false;
    }

    /// <summary>
    /// Checks whether a PIN is currently stored for the specified connection.
    /// </summary>
    public bool HasCredential(ConnectionId connectionId)
    {
        ThrowIfDisposed();
        return _pins.ContainsKey(connectionId.Value);
    }

    /// <summary>
    /// Removes and wipes the PIN for the specified connection.
    /// </summary>
    public bool RemovePin(ConnectionId connectionId)
    {
        ThrowIfDisposed();

        if (_pins.TryRemove(connectionId.Value, out var existing))
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(existing.AsSpan()));
            Array.Clear(existing);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Securely wipes and removes all registered credentials.
    /// </summary>
    public void Clear()
    {
        foreach (var kvp in _pins)
        {
            if (_pins.TryRemove(kvp.Key, out var existing))
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(existing.AsSpan()));
                Array.Clear(existing);
            }
        }
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
        Clear();
    }
}
