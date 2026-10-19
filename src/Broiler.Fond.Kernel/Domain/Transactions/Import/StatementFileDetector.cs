using System.Text;

namespace Broiler.Fond.Kernel.Domain.Transactions.Import;

/// <summary>
/// Fast, bounded statement file format detector for ISO 20022 CAMT and SWIFT MT940/MT942 formats.
/// Zero external libraries, zero network access.
/// </summary>
public static class StatementFileDetector
{
    public const int MaxProbeBytes = 64 * 1024; // 64 KiB probe window

    /// <summary>
    /// Detects the statement file format from text.
    /// </summary>
    public static StatementFileFormat Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return StatementFileFormat.Unknown;
        }

        ReadOnlySpan<char> span = text.AsSpan().TrimStart();
        if (span.IsEmpty)
        {
            return StatementFileFormat.Unknown;
        }

        // XML-based detection (ISO 20022 CAMT)
        if (span.StartsWith("<") || span.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
        {
            if (text.Contains("camt.053.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("BkToCstmrStmt", StringComparison.OrdinalIgnoreCase))
            {
                return StatementFileFormat.Camt053;
            }

            if (text.Contains("camt.052.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("BkToCstmrAcctRpt", StringComparison.OrdinalIgnoreCase))
            {
                return StatementFileFormat.Camt052;
            }

            return StatementFileFormat.Unknown;
        }

        // SWIFT MT940 / MT942 detection
        if (ContainsSwiftTag(text, ":20:") && (ContainsSwiftTag(text, ":25:") || ContainsSwiftTag(text, ":25P:")))
        {
            if (text.Contains("{2:I942", StringComparison.Ordinal) ||
                text.Contains("{2:O942", StringComparison.Ordinal) ||
                ContainsSwiftTag(text, ":34F:"))
            {
                return StatementFileFormat.Mt942;
            }

            if (text.Contains("{2:I940", StringComparison.Ordinal) ||
                text.Contains("{2:O940", StringComparison.Ordinal) ||
                ContainsSwiftTag(text, ":60F:") ||
                ContainsSwiftTag(text, ":60M:") ||
                ContainsSwiftTag(text, ":62F:") ||
                ContainsSwiftTag(text, ":62M:") ||
                ContainsSwiftTag(text, ":61:"))
            {
                return StatementFileFormat.Mt940;
            }
        }

        return StatementFileFormat.Unknown;
    }

    /// <summary>
    /// Detects statement format from raw bytes, handling UTF-8 BOM and common encodings.
    /// </summary>
    public static StatementFileFormat Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return StatementFileFormat.Unknown;
        }

        int probeLen = Math.Min(bytes.Length, MaxProbeBytes);
        ReadOnlySpan<byte> probe = bytes[..probeLen];

        // Strip UTF-8 BOM if present
        if (probe.Length >= 3 && probe[0] == 0xEF && probe[1] == 0xBB && probe[2] == 0xBF)
        {
            probe = probe[3..];
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(probe);
        }
        catch
        {
            text = Encoding.Latin1.GetString(probe);
        }

        return Detect(text);
    }

    /// <summary>
    /// Detects statement format from a stream, preserving the stream position if seekable.
    /// </summary>
    public static StatementFileFormat Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        }

        long originalPos = stream.CanSeek ? stream.Position : 0;
        byte[] buffer = new byte[MaxProbeBytes];
        int read = stream.Read(buffer, 0, buffer.Length);
        if (stream.CanSeek)
        {
            stream.Position = originalPos;
        }

        return Detect(buffer.AsSpan(0, read));
    }

    private static bool ContainsSwiftTag(string text, string tag)
    {
        int index = 0;
        while ((index = text.IndexOf(tag, index, StringComparison.Ordinal)) >= 0)
        {
            if (index == 0 || text[index - 1] == '\n' || text[index - 1] == '\r')
            {
                return true;
            }

            index += tag.Length;
        }

        return false;
    }
}
