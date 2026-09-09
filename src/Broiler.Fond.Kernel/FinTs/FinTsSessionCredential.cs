using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsCredentialKind { Pin, Tan }
public enum FinTsCredentialState { Available, Consumed, Cancelled, Expired, Disposed, ClockInvalid }
public enum FinTsCredentialCopyResult { Copied, DestinationTooSmall, Unavailable }

/// <summary>Diagnostics without value, length, source identifiers or buffer references.</summary>
public sealed class FinTsCredentialSnapshot
{
    internal FinTsCredentialSnapshot(FinTsCredentialKind kind, FinTsCredentialState state, int copies, TimeSpan remaining)
    { Kind = kind; State = state; CopiesCompleted = copies; RemainingTime = remaining; }
    public FinTsCredentialKind Kind { get; }
    public FinTsCredentialState State { get; }
    public int CopiesCompleted { get; }
    public TimeSpan RemainingTime { get; }
}

/// <summary>Bounded local ownership of opaque credential bytes. No string conversion, banking session, wire encoding or authentication.
/// Dispose promptly; expiry is observed on API calls. Copy-out destinations are owned and cleared by the caller.</summary>
public sealed class FinTsSessionCredential : IDisposable
{
    public const int MaximumLength = 99;
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromMinutes(15);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly TimeSpan _lifetime;
    private readonly FinTsCredentialKind _kind;
    private readonly long _startedAt;
    private long _lastTimestamp;
    private TimeSpan _elapsed;
    private byte[]? _bytes;
    private FinTsCredentialState _state;
    private int _copies;

    private FinTsSessionCredential(FinTsCredentialKind kind, TimeSpan lifetime, TimeProvider clock, long timestamp)
    { _kind = kind; _lifetime = lifetime; _clock = clock; _startedAt = _lastTimestamp = timestamp; }

    /// <summary>Transfers bytes by copying then clearing the entire supplied span on every exit, including invalid input and cancellation.
    /// Caller must exclusively own the input during this call. Length limits are local bounds, not bank credential validation.</summary>
    public static FinTsSessionCredential CaptureAndClear(Span<byte> source, FinTsCredentialKind kind, TimeSpan lifetime,
        TimeProvider? clock = null, CancellationToken cancellationToken = default)
    {
        FinTsSessionCredential? owner = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Length is < 1 or > MaximumLength) { throw new ArgumentException("Credential length is outside the local bounds.", nameof(source)); }
            if (kind is not (FinTsCredentialKind.Pin or FinTsCredentialKind.Tan)) { throw new ArgumentOutOfRangeException(nameof(kind)); }
            if (lifetime <= TimeSpan.Zero || lifetime > MaximumLifetime) { throw new ArgumentOutOfRangeException(nameof(lifetime)); }
            clock ??= TimeProvider.System;
            long timestamp;
            try
            {
                if (clock.TimestampFrequency <= 0) { throw new InvalidOperationException(); }
                timestamp = clock.GetTimestamp();
            }
            catch (Exception) { throw new ArgumentException("A working monotonic clock with positive frequency is required.", nameof(clock)); }
            owner = new(kind, lifetime, clock, timestamp);
            owner._bytes = GC.AllocateArray<byte>(source.Length, pinned: true);
            source.CopyTo(owner._bytes);
            cancellationToken.ThrowIfCancellationRequested();
            owner.ObserveClock();
            cancellationToken.ThrowIfCancellationRequested();
            return owner;
        }
        catch
        {
            owner?.Dispose();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(source); }
    }

    public FinTsCredentialSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            ObserveClock();
            return new(_kind, _state, _copies, _state == FinTsCredentialState.Available ? _lifetime - _elapsed : TimeSpan.Zero);
        }
    }

    /// <summary>Copies to an exclusively caller-owned span. A successful TAN copy consumes and clears this owner once.
    /// Failure writes zero bytes; post-copy expiry/cancellation clears the copied prefix. Unwritten destination bytes are untouched.</summary>
    public FinTsCredentialCopyResult TryCopyTo(Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0;
        lock (_gate)
        {
            ObserveClock();
            CheckCancellation(cancellationToken);
            if (_state != FinTsCredentialState.Available) { return FinTsCredentialCopyResult.Unavailable; }
            int length = _bytes!.Length;
            if (destination.Length < length) { return FinTsCredentialCopyResult.DestinationTooSmall; }
            _bytes.AsSpan().CopyTo(destination);
            ObserveClock();
            if (cancellationToken.IsCancellationRequested)
            {
                CryptographicOperations.ZeroMemory(destination[..length]);
                CheckCancellation(cancellationToken);
            }
            if (_state != FinTsCredentialState.Available)
            {
                CryptographicOperations.ZeroMemory(destination[..length]);
                return FinTsCredentialCopyResult.Unavailable;
            }
            if (_copies < int.MaxValue) { _copies++; }
            if (_kind == FinTsCredentialKind.Tan) { Close(FinTsCredentialState.Consumed); }
            bytesWritten = length;
            return FinTsCredentialCopyResult.Copied;
        }
    }

    public void Cancel()
    {
        lock (_gate) { ObserveClock(); if (_state == FinTsCredentialState.Available) { Close(FinTsCredentialState.Cancelled); } }
    }
    public void Dispose()
    {
        // Cleanup must not invoke a caller-supplied clock.
        lock (_gate) { if (_state == FinTsCredentialState.Available) { Close(FinTsCredentialState.Disposed); } }
        GC.SuppressFinalize(this);
    }
    private void CheckCancellation(CancellationToken token)
    {
        if (!token.IsCancellationRequested) { return; }
        if (_state == FinTsCredentialState.Available) { Close(FinTsCredentialState.Cancelled); }
        token.ThrowIfCancellationRequested();
    }
    private void ObserveClock()
    {
        if (_state != FinTsCredentialState.Available) { return; }
        try
        {
            long now = _clock.GetTimestamp();
            if (now < _lastTimestamp) { Close(FinTsCredentialState.ClockInvalid); return; }
            _lastTimestamp = now;
            _elapsed = _clock.GetElapsedTime(_startedAt, now);
            if (_elapsed < TimeSpan.Zero) { Close(FinTsCredentialState.ClockInvalid); }
            else if (_elapsed >= _lifetime) { Close(FinTsCredentialState.Expired); }
        }
        catch (Exception) { Close(FinTsCredentialState.ClockInvalid); }
    }
    private void Close(FinTsCredentialState state)
    {
        _state = state;
        Erase();
        GC.SuppressFinalize(this);
    }
    private void Erase()
    {
        if (_bytes is { } bytes) { CryptographicOperations.ZeroMemory(bytes); _bytes = null; }
    }
    ~FinTsSessionCredential() { Erase(); }
}
