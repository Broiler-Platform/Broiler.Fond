using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs.Sca;

/// <summary>
/// Extracts and parses visual, optical, matrix, and QR TAN challenges from untrusted HITAN segments.
/// Implemented entirely using .NET 10 runtime APIs with zero third-party native libraries.
/// </summary>
public static class VisualChallengeParser
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] GifMagic = [0x47, 0x49, 0x46];

    /// <summary>
    /// Parses the visual challenge payload from a FinTS TAN challenge segment.
    /// </summary>
    public static VisualChallengePayload Parse(FinTsTanChallenge? challenge)
    {
        if (challenge is null)
        {
            return VisualChallengePayload.Empty;
        }

        // 1. Inspect HhdData element (Field 5 in HITAN)
        if (challenge.HhdData is not null && !challenge.HhdData.IsEmpty)
        {
            byte[] rawBytes = challenge.HhdData.IsBinary
                ? challenge.HhdData.CopyValueBytes()
                : Encoding.UTF8.GetBytes(challenge.HhdData.HeaderText());

            if (rawBytes.Length > 0)
            {
                var parsedHhd = ParseHhdBytes(rawBytes);
                if (parsedHhd.Kind != VisualChallengeKind.None)
                {
                    return parsedHhd;
                }
            }
        }

        // 2. Inspect challenge text fallback (Field 4 in HITAN)
        if (challenge.Text is not null && !challenge.Text.IsEmpty)
        {
            string text = challenge.Text.HeaderText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Check if text itself embeds a base64 data URI
                if (text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseDataUri(text);
                }

                // Check if text is a QR code payload (GiroCode / EPC QR or authentication URL)
                if (text.StartsWith("BCD\n", StringComparison.Ordinal) ||
                    text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return new VisualChallengePayload
                    {
                        Kind = VisualChallengeKind.QrCodeText,
                        MimeType = "text/plain",
                        TextContent = text,
                        RawBytes = Encoding.UTF8.GetBytes(text),
                    };
                }

                // Plain textual challenge (e.g. mobileTAN / SMS prompt)
                return VisualChallengePayload.TextOnly(text);
            }
        }

        return VisualChallengePayload.Empty;
    }

    private static VisualChallengePayload ParseHhdBytes(byte[] bytes)
    {
        // 1. Magic bytes detection for standard image formats
        if (HasMagicBytes(bytes, PngMagic))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.MatrixCodeImage,
                MimeType = "image/png",
                RawBytes = bytes,
            };
        }

        if (HasMagicBytes(bytes, JpegMagic))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.MatrixCodeImage,
                MimeType = "image/jpeg",
                RawBytes = bytes,
            };
        }

        if (HasMagicBytes(bytes, GifMagic))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.MatrixCodeImage,
                MimeType = "image/gif",
                RawBytes = bytes,
            };
        }

        // 2. String representation inspection (ASCII / UTF-8)
        string textRepr = Encoding.Latin1.GetString(bytes).Trim();

        if (textRepr.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return ParseDataUri(textRepr);
        }

        if (textRepr.StartsWith("BCD\n", StringComparison.Ordinal) ||
            textRepr.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            textRepr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.QrCodeText,
                MimeType = "text/plain",
                TextContent = textRepr,
                RawBytes = bytes,
            };
        }

        // 3. Optical flicker code detection (chipTAN optisch / HHD 1.3 / HHD 1.4)
        // Can be represented as raw bytes or as a hex ASCII string
        if (IsFlickerHexString(textRepr, out int hexFlickerLen, out byte hexCtrlByte, out var hexElements))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.FlickerCode,
                MimeType = "application/x-flicker",
                TextContent = textRepr,
                RawBytes = bytes,
                FlickerLength = hexFlickerLen,
                FlickerControlByte = hexCtrlByte,
                ExtractedDataElements = hexElements,
            };
        }

        if (IsFlickerBinaryBytes(bytes, out int binFlickerLen, out byte binCtrlByte, out var binElements))
        {
            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.FlickerCode,
                MimeType = "application/x-flicker",
                TextContent = Convert.ToHexString(bytes),
                RawBytes = bytes,
                FlickerLength = binFlickerLen,
                FlickerControlByte = binCtrlByte,
                ExtractedDataElements = binElements,
            };
        }

        // 4. Fallback for unrecognized binary matrix data
        return new VisualChallengePayload
        {
            Kind = VisualChallengeKind.MatrixCodeRaw,
            MimeType = "application/octet-stream",
            RawBytes = bytes,
        };
    }

    private static VisualChallengePayload ParseDataUri(string dataUri)
    {
        int commaIndex = dataUri.IndexOf(',');
        if (commaIndex > 0)
        {
            string header = dataUri[..commaIndex];
            string payload = dataUri[(commaIndex + 1)..];

            string mime = "image/png";
            int semiIndex = header.IndexOf(';');
            if (semiIndex > 5)
            {
                mime = header[5..semiIndex];
            }

            byte[] decoded = Array.Empty<byte>();
            if (header.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    decoded = Convert.FromBase64String(payload.Trim());
                }
                catch
                {
                    // If base64 fails, preserve empty bytes
                }
            }

            return new VisualChallengePayload
            {
                Kind = VisualChallengeKind.MatrixCodeImage,
                MimeType = mime,
                TextContent = dataUri,
                RawBytes = decoded,
            };
        }

        return new VisualChallengePayload
        {
            Kind = VisualChallengeKind.MatrixCodeImage,
            MimeType = "image/png",
            TextContent = dataUri,
        };
    }

    private static bool HasMagicBytes(byte[] bytes, byte[] magic)
    {
        if (bytes.Length < magic.Length)
        {
            return false;
        }

        for (int i = 0; i < magic.Length; i++)
        {
            if (bytes[i] != magic[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFlickerHexString(
        string text,
        out int length,
        out byte controlByte,
        out IReadOnlyList<string> elements)
    {
        length = 0;
        controlByte = 0;
        elements = Array.Empty<string>();

        if (text.Length < 6)
        {
            return false;
        }

        // Verify all characters are valid hexadecimal digits
        foreach (char c in text)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        try
        {
            // First 2 or 3 hex chars indicate length in decimal or hex;
            // Common DK HHD pattern: 2 hex characters for payload length, followed by control byte (e.g. 0x28).
            if (int.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int parsedLen))
            {
                length = parsedLen;
                if (text.Length >= 4 && byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte ctrl))
                {
                    controlByte = ctrl;

                    // Extract L-V elements from remaining string
                    var extracted = new List<string>();
                    int cursor = 4;
                    while (cursor + 2 <= text.Length)
                    {
                        if (int.TryParse(text.AsSpan(cursor, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int elemLen))
                        {
                            cursor += 2;
                            int charLen = elemLen * 2; // hex chars
                            if (cursor + charLen <= text.Length)
                            {
                                extracted.Add(text.Substring(cursor, charLen));
                                cursor += charLen;
                            }
                            else
                            {
                                extracted.Add(text[cursor..]);
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    elements = extracted.AsReadOnly();
                    return true;
                }
            }
        }
        catch
        {
            // Fall closed if parse fails
        }

        return false;
    }

    private static bool IsFlickerBinaryBytes(
        byte[] bytes,
        out int length,
        out byte controlByte,
        out IReadOnlyList<string> elements)
    {
        length = 0;
        controlByte = 0;
        elements = Array.Empty<string>();

        if (bytes.Length < 3)
        {
            return false;
        }

        // Typical HHD 1.3/1.4 starts with length byte and control byte 0x28 or 0x01
        byte lenByte = bytes[0];
        byte ctrl = bytes[1];

        if (ctrl is 0x28 or 0x01 or 0x02 && lenByte > 0 && lenByte <= bytes.Length)
        {
            length = lenByte;
            controlByte = ctrl;

            var extracted = new List<string>();
            int cursor = 2;
            while (cursor + 1 < bytes.Length && cursor < length + 1)
            {
                int elemLen = bytes[cursor++];
                if (cursor + elemLen <= bytes.Length)
                {
                    string elemHex = Convert.ToHexString(bytes, cursor, elemLen);
                    extracted.Add(elemHex);
                    cursor += elemLen;
                }
                else
                {
                    break;
                }
            }

            elements = extracted.AsReadOnly();
            return true;
        }

        return false;
    }
}
