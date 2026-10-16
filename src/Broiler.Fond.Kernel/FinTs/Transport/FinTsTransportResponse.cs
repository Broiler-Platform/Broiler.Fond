namespace Broiler.Fond.Kernel.FinTs.Transport;

/// <summary>Represents an HTTP transport response received from a FinTS server endpoint.</summary>
public sealed class FinTsTransportResponse
{
    public int StatusCode { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public string? ContentType { get; }
    public TimeSpan Duration { get; }
    public bool IsSuccess => StatusCode is >= 200 and <= 299;

    public FinTsTransportResponse(
        int StatusCode,
        ReadOnlyMemory<byte> payload,
        string? contentType,
        TimeSpan duration)
    {
        if (StatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(StatusCode), StatusCode, "HTTP status code must be between 100 and 599.");
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "Duration cannot be negative.");
        }

        this.StatusCode = StatusCode;
        Payload = payload;
        ContentType = contentType;
        Duration = duration;
    }
}
