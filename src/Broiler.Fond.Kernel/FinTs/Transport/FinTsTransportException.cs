using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>
/// Domain exception representing a failure in the FinTS transport layer.
/// Invariant: To prevent credential and financial data leakage, transport exception messages
/// and properties never include raw request payload or response payload bytes.
/// </summary>
public sealed class FinTsTransportException : Exception
{
    public FinTsTransportIssue Issue { get; }
    public int? StatusCode { get; }
    public string? EndpointHost { get; }
    public string? EndpointPath { get; }

    public FinTsTransportException(
        FinTsTransportIssue issue,
        string message,
        Uri? endpoint = null,
        int? statusCode = null,
        Exception? innerException = null)
        : base(FormatMessage(issue, message, endpoint, statusCode), innerException)
    {
        Issue = issue;
        StatusCode = statusCode;
        EndpointHost = endpoint?.IdnHost;
        EndpointPath = endpoint?.AbsolutePath;
    }

    private static string FormatMessage(FinTsTransportIssue issue, string message, Uri? endpoint, int? statusCode)
    {
        string statusPart = statusCode.HasValue
            ? $" (HTTP {statusCode.Value.ToString(CultureInfo.InvariantCulture)})"
            : string.Empty;

        string endpointPart = endpoint is not null
            ? $" [Host: {endpoint.IdnHost}]"
            : string.Empty;

        return $"FinTS transport failure [{issue}]{statusPart}{endpointPart}: {message}";
    }
}
