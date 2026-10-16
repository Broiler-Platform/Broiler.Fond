namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>
/// Defines the asynchronous transport boundary for transmitting FinTS messages over HTTPS.
/// </summary>
public interface IFinTsTransport : IDisposable
{
    /// <summary>
    /// Sends a FinTS request payload to the confirmed HTTPS endpoint and receives the server response.
    /// Throws <see cref="FinTsTransportException"/> on failure.
    /// </summary>
    ValueTask<FinTsTransportResponse> SendAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> requestPayload,
        CancellationToken cancellationToken = default);
}
