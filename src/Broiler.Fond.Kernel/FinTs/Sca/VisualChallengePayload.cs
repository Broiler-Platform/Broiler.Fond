namespace Broiler.Fond.Kernel.FinTs.Sca;

/// <summary>
/// Parsed visual challenge metadata and raw byte payload.
/// Shipped without third-party native libraries, preserving verbatim bank evidence.
/// </summary>
public sealed record VisualChallengePayload
{
    public required VisualChallengeKind Kind { get; init; }
    public string? MimeType { get; init; }
    public ReadOnlyMemory<byte> RawBytes { get; init; } = ReadOnlyMemory<byte>.Empty;
    public string? TextContent { get; init; }
    public int? FlickerLength { get; init; }
    public byte? FlickerControlByte { get; init; }
    public IReadOnlyList<string> ExtractedDataElements { get; init; } = Array.Empty<string>();

    public bool HasVisualData => Kind is not VisualChallengeKind.None and not VisualChallengeKind.PlainText;

    public static VisualChallengePayload Empty => new()
    {
        Kind = VisualChallengeKind.None,
    };

    public static VisualChallengePayload TextOnly(string text) => new()
    {
        Kind = VisualChallengeKind.PlainText,
        MimeType = "text/plain",
        TextContent = text,
    };
}
