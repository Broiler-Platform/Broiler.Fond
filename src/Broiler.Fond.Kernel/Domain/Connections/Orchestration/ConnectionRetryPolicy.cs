using System.Net;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Policy governing retry decisions, failure classification, and monotonic exponential backoff calculations.
/// Invariants:
/// - Purely deterministic calculation; no thread blocking or sleep loops.
/// - Credential and security errors are strictly non-transient and fail immediately to user review.
/// </summary>
public sealed class ConnectionRetryPolicy
{
    public static readonly ConnectionRetryPolicy Default = new();

    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(30);
    public double BackoffMultiplier { get; init; } = 2.0;

    public TimeSpan CalculateBackoff(int consecutiveFailures, TimeSpan? explicitBankDelay = null)
    {
        if (consecutiveFailures <= 0)
        {
            return TimeSpan.Zero;
        }

        if (explicitBankDelay.HasValue && explicitBankDelay.Value > TimeSpan.Zero)
        {
            return explicitBankDelay.Value > MaxBackoff ? MaxBackoff : explicitBankDelay.Value;
        }

        double factor = Math.Pow(BackoffMultiplier, Math.Min(10, consecutiveFailures - 1));
        double calculatedSeconds = BaseBackoff.TotalSeconds * factor;

        TimeSpan calculated = TimeSpan.FromSeconds(calculatedSeconds);
        return calculated > MaxBackoff ? MaxBackoff : (calculated < BaseBackoff ? BaseBackoff : calculated);
    }

    /// <summary>
    /// Determines whether the supplied FinTS return code represents a transient server/dialogue error.
    /// </summary>
    public static bool IsTransientFinTsCode(string returnCode)
    {
        if (string.IsNullOrWhiteSpace(returnCode))
        {
            return false;
        }

        string trimmed = returnCode.Trim();
        return trimmed is "9010" or "9050" or "9800" or "9000";
    }

    /// <summary>
    /// Determines whether the supplied FinTS return code indicates credential expiration, lock, or authorization rejection.
    /// </summary>
    public static bool IsAuthenticationFailureCode(string returnCode)
    {
        if (string.IsNullOrWhiteSpace(returnCode))
        {
            return false;
        }

        string trimmed = returnCode.Trim();
        return trimmed is "9930" or "9931" or "9942" or "9210" or "9340" or "9910";
    }

    /// <summary>
    /// Determines whether the exception represents a transient network, transport, or HTTP failure.
    /// </summary>
    public static bool IsTransientException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex is FinTs.Transport.FinTsTransportException transportEx)
        {
            if (transportEx.StatusCode is 502 or 503 or 504 or 429)
            {
                return true;
            }

            if (transportEx.Issue is FinTs.Transport.FinTsTransportIssue.Timeout
                or FinTs.Transport.FinTsTransportIssue.NetworkFailure
                or FinTs.Transport.FinTsTransportIssue.HttpError)
            {
                return true;
            }
        }

        if (ex is TimeoutException or OperationCanceledException and not TaskCanceledException { CancellationToken.IsCancellationRequested: true })
        {
            return true;
        }

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode is HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or (HttpStatusCode)429) // Too Many Requests
            {
                return true;
            }

            return true; // General network/socket drop
        }

        if (ex is IOException)
        {
            return true;
        }

        return false;
    }
}
