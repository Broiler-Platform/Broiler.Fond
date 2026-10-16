using System.Collections.Concurrent;

namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>
/// In-memory simulated transport for deterministic verification of FinTS dialogue workflows
/// without network sockets.
/// </summary>
public sealed class FinTsSimulatedTransport : IFinTsTransport
{
    private readonly ConcurrentQueue<Func<Uri, ReadOnlyMemory<byte>, CancellationToken, ValueTask<FinTsTransportResponse>>> _handlers = new();
    private readonly ConcurrentBag<(Uri Endpoint, byte[] Payload, DateTimeOffset Timestamp)> _recordedTransmissions = new();
    private bool _disposed;

    public int TransmittedCount => _recordedTransmissions.Count;
    public IReadOnlyCollection<(Uri Endpoint, byte[] Payload, DateTimeOffset Timestamp)> RecordedTransmissions => _recordedTransmissions;

    /// <summary>Enqueues a pre-configured successful response payload.</summary>
    public void EnqueueResponse(byte[] responseBytes, int statusCode = 200, string contentType = "text/plain", TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(responseBytes);
        TimeSpan effDuration = duration ?? TimeSpan.FromMilliseconds(5);

        _handlers.Enqueue((endpoint, _, _) =>
            ValueTask.FromResult(new FinTsTransportResponse(statusCode, responseBytes, contentType, effDuration)));
    }

    /// <summary>Enqueues an exception to be thrown upon sending.</summary>
    public void EnqueueFailure(FinTsTransportException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _handlers.Enqueue((_, _, _) => throw exception);
    }

    /// <summary>Enqueues a custom request-response handler delegate.</summary>
    public void EnqueueHandler(Func<Uri, ReadOnlyMemory<byte>, CancellationToken, ValueTask<FinTsTransportResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Enqueue(handler);
    }

    public ValueTask<FinTsTransportResponse> SendAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> requestPayload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new FinTsTransportException(FinTsTransportIssue.Cancelled, "Operation canceled by caller token.", endpoint);
        }

        _recordedTransmissions.Add((endpoint, requestPayload.ToArray(), DateTimeOffset.UtcNow));

        if (!_handlers.TryDequeue(out var handler))
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.NetworkFailure,
                "No simulated transport response queued.",
                endpoint);
        }

        return handler(endpoint, requestPayload, cancellationToken);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
