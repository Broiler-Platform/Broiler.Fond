using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;

namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>
/// Bounded, secret-safe HTTPS transport implementation for FinTS 3.0 communication.
/// Enforces HTTPS scheme, payload size limits, request timeouts, and prevents credential leakage.
/// </summary>
public sealed class FinTsHttpsTransport : IFinTsTransport
{
    public const int DefaultMaximumRequestBodyBytes = 128 * 1024;      // 128 KiB
    public const int DefaultMaximumResponseBodyBytes = 1024 * 1024;    // 1 MiB
    public const int MaximumEndpointLength = 2048;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public int MaximumRequestBodyBytes { get; }
    public int MaximumResponseBodyBytes { get; }
    public TimeSpan Timeout { get; }

    public FinTsHttpsTransport(
        HttpMessageHandler? handler = null,
        bool disposeHandler = true,
        int maximumRequestBodyBytes = DefaultMaximumRequestBodyBytes,
        int maximumResponseBodyBytes = DefaultMaximumResponseBodyBytes,
        TimeSpan? timeout = null)
    {
        if (maximumRequestBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequestBodyBytes), maximumRequestBodyBytes, "Maximum request body bytes must be positive.");
        }

        if (maximumResponseBodyBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBodyBytes), maximumResponseBodyBytes, "Maximum response body bytes must be positive.");
        }

        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }

        MaximumRequestBodyBytes = maximumRequestBodyBytes;
        MaximumResponseBodyBytes = maximumResponseBodyBytes;
        Timeout = effectiveTimeout;

        if (handler is not null)
        {
            _httpClient = new HttpClient(handler, disposeHandler);
        }
        else
        {
            var socketsHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.None,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _httpClient = new HttpClient(socketsHandler, disposeHandler: true);
        }

        _httpClient.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // Controlled per-request via CancellationTokenSource
        _ownsHttpClient = true;
    }

    public async ValueTask<FinTsTransportResponse> SendAsync(
        Uri endpoint,
        ReadOnlyMemory<byte> requestPayload,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(endpoint);

        ValidateEndpoint(endpoint);

        if (requestPayload.IsEmpty)
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.EmptyPayload,
                "Request payload must not be empty.",
                endpoint);
        }

        if (requestPayload.Length > MaximumRequestBodyBytes)
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.PayloadTooLarge,
                $"Request payload of {requestPayload.Length} bytes exceeds maximum allowed limit of {MaximumRequestBodyBytes} bytes.",
                endpoint);
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(Timeout);

        long startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new ReadOnlyMemoryContent(requestPayload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

            using HttpResponseMessage response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linkedCts.Token).ConfigureAwait(false);

            int statusCode = (int)response.StatusCode;
            if (statusCode is < 200 or > 299)
            {
                throw new FinTsTransportException(
                    FinTsTransportIssue.HttpError,
                    $"Server responded with non-success HTTP status code.",
                    endpoint,
                    statusCode);
            }

            // Early check on advertised content-length header before streaming
            if (response.Content.Headers.ContentLength.HasValue &&
                response.Content.Headers.ContentLength.Value > MaximumResponseBodyBytes)
            {
                throw new FinTsTransportException(
                    FinTsTransportIssue.PayloadTooLarge,
                    $"Server advertised response length {response.Content.Headers.ContentLength.Value} exceeds maximum allowed limit of {MaximumResponseBodyBytes} bytes.",
                    endpoint,
                    statusCode);
            }

            // Bounded stream reading
            byte[] responseBytes = await ReadBoundedContentAsync(
                response.Content,
                MaximumResponseBodyBytes,
                endpoint,
                statusCode,
                linkedCts.Token).ConfigureAwait(false);

            TimeSpan duration = Stopwatch.GetElapsedTime(startTimestamp);
            string? contentType = response.Content.Headers.ContentType?.MediaType;

            return new FinTsTransportResponse(statusCode, responseBytes, contentType, duration);
        }
        catch (OperationCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new FinTsTransportException(
                    FinTsTransportIssue.Cancelled,
                    "Transport operation was canceled by the caller token.",
                    endpoint,
                    innerException: ex);
            }

            throw new FinTsTransportException(
                FinTsTransportIssue.Timeout,
                $"Transport operation timed out after {Timeout.TotalSeconds:F1}s.",
                endpoint,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            bool isTls = ex.InnerException is AuthenticationException ||
                         ex.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                         ex.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
                         ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase);

            var issue = isTls ? FinTsTransportIssue.TlsFailure : FinTsTransportIssue.NetworkFailure;
            string message = isTls
                ? "Secure TLS negotiation or certificate validation failed."
                : "An underlying network communication failure occurred.";

            throw new FinTsTransportException(issue, message, endpoint, (int?)ex.StatusCode, ex);
        }
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri)
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                "The endpoint must be an absolute URI.",
                endpoint);
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                "Only secure HTTPS endpoints are permitted for FinTS communication.",
                endpoint);
        }

        if (string.IsNullOrEmpty(endpoint.Host))
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                "The endpoint must specify a valid host.",
                endpoint);
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                "Endpoints with embedded user information are strictly prohibited.",
                endpoint);
        }

        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                "Endpoints with URI fragments are prohibited.",
                endpoint);
        }

        if (endpoint.AbsoluteUri.Length > MaximumEndpointLength)
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.InvalidEndpoint,
                $"Endpoint length exceeds maximum limit of {MaximumEndpointLength} characters.",
                endpoint);
        }
    }

    private static async Task<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        int maximumBytes,
        Uri endpoint,
        int statusCode,
        CancellationToken cancellationToken)
    {
        using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memoryStream = new MemoryStream();
        byte[] buffer = new byte[8192];
        int totalRead = 0;

        while (true)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maximumBytes)
            {
                throw new FinTsTransportException(
                    FinTsTransportIssue.PayloadTooLarge,
                    $"Response body exceeded maximum allowed limit of {maximumBytes} bytes.",
                    endpoint,
                    statusCode);
            }

            memoryStream.Write(buffer, 0, read);
        }

        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsHttpClient)
            {
                _httpClient.Dispose();
            }

            _disposed = true;
        }
    }
}
