namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>Categorized transport outcomes and failure reasons for FinTS communications.</summary>
public enum FinTsTransportIssue
{
    /// <summary>No transport issue occurred.</summary>
    None = 0,

    /// <summary>The endpoint was not an absolute HTTPS URI, had invalid components, or exceeded length limits.</summary>
    InvalidEndpoint = 1,

    /// <summary>The request or response payload exceeded configured byte bounds.</summary>
    PayloadTooLarge = 2,

    /// <summary>The request payload was empty.</summary>
    EmptyPayload = 3,

    /// <summary>The transport operation timed out before completing.</summary>
    Timeout = 4,

    /// <summary>A TLS handshake, certificate validation, or secure protocol negotiation failed.</summary>
    TlsFailure = 5,

    /// <summary>The server returned a non-successful HTTP status code.</summary>
    HttpError = 6,

    /// <summary>An underlying network failure occurred (connection refused, DNS failure, reset).</summary>
    NetworkFailure = 7,

    /// <summary>The operation was canceled by caller token.</summary>
    Cancelled = 8,
}
