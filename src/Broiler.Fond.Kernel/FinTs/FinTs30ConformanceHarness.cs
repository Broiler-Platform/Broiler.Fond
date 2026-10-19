using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// High-level classification of FinTS 3.0 return codes (HIRMG/HIRMS).
/// </summary>
public enum FinTsReturnCodeClassification
{
    Success,
    Warning,
    ScaRequired,
    AuthenticationFailed,
    BankSystemError,
    GeneralError
}

/// <summary>
/// A named test vector produced by the failure-injection fuzz generator.
/// </summary>
public sealed record FinTsFuzzVector(string Name, string Description, byte[] WireBytes, Type ExpectedExceptionType);

/// <summary>
/// Conformance harness and boundary hardening validator for FinTS 3.0 messages.
/// Enforces syntax bounds, capability matrix compliance, return code classification,
/// and failure-injection resilience.
/// </summary>
public static class FinTs30ConformanceHarness
{
    /// <summary>
    /// Classifies standard German banking return codes from HIRMG or HIRMS segments.
    /// </summary>
    public static FinTsReturnCodeClassification ClassifyReturnCode(int returnCode)
    {
        return returnCode switch
        {
            10 or 20 or 30 => FinTsReturnCodeClassification.Success,
            3010 or 3060 or 3070 or 3072 or 3076 or 3080 => FinTsReturnCodeClassification.Warning,
            3920 or 3955 or 3956 => FinTsReturnCodeClassification.ScaRequired,
            9010 or 9050 or 9210 or 9931 => FinTsReturnCodeClassification.AuthenticationFailed,
            9000 or 9999 => FinTsReturnCodeClassification.BankSystemError,
            >= 9000 => FinTsReturnCodeClassification.GeneralError,
            >= 3000 and < 4000 => FinTsReturnCodeClassification.Warning,
            _ => FinTsReturnCodeClassification.GeneralError
        };
    }

    /// <summary>
    /// Parses the raw FinTS message wire bytes and verifies that all contained segments
    /// strictly adhere to the frozen FinTS 3.0 capability matrix.
    /// </summary>
    public static FinTsSyntaxDocument ValidateAndParse(ReadOnlySpan<byte> input, bool enforceCapabilityLock = true)
    {
        FinTsSyntaxDocument doc = FinTsSyntax.ParseSegments(input);

        if (enforceCapabilityLock)
        {
            foreach (var seg in doc.Segments)
            {
                FinTs30CapabilityLock.ValidateSegment(seg.Code, seg.Version);
            }
        }

        return doc;
    }

    /// <summary>
    /// Generates a deterministic suite of malformed, truncated, and hostile input vectors
    /// from a valid baseline FinTS 3.0 message for boundary testing.
    /// </summary>
    public static IReadOnlyList<FinTsFuzzVector> GenerateFuzzCorpus(byte[] validBaseline)
    {
        ArgumentNullException.ThrowIfNull(validBaseline);
        if (validBaseline.Length == 0)
        {
            throw new ArgumentException("Valid baseline message cannot be empty.", nameof(validBaseline));
        }

        List<FinTsFuzzVector> vectors = [];

        // 1. Truncated at byte 10
        int truncateLen = Math.Min(10, validBaseline.Length / 2);
        byte[] trunc10 = new byte[truncateLen];
        Array.Copy(validBaseline, trunc10, truncateLen);
        vectors.Add(new FinTsFuzzVector(
            "TruncatedEarly",
            "Message cut off abruptly within initial header segment",
            trunc10,
            typeof(FinTsFormatException)));

        // 2. Truncated before terminating quote
        if (validBaseline[^1] == (byte)'\'')
        {
            byte[] missingTerm = new byte[validBaseline.Length - 1];
            Array.Copy(validBaseline, missingTerm, validBaseline.Length - 1);
            vectors.Add(new FinTsFuzzVector(
                "MissingFinalTerminator",
                "Message missing trailing segment delimiter quote",
                missingTerm,
                typeof(FinTsFormatException)));
        }

        // 3. Invalid escape sequence: replace a segment delimiter with unescaped quote or question mark
        byte[] invalidEscape = (byte[])validBaseline.Clone();
        int mid = invalidEscape.Length / 2;
        invalidEscape[mid] = (byte)'?'; // dangling question mark
        invalidEscape[Math.Min(mid + 1, invalidEscape.Length - 1)] = (byte)'X'; // invalid escape target
        vectors.Add(new FinTsFuzzVector(
            "InvalidEscapeSequence",
            "Dangling question mark escape with invalid escaped character",
            invalidEscape,
            typeof(FinTsFormatException)));

        // 4. Injected unsupported segment version
        string baselineStr = Encoding.Latin1.GetString(validBaseline);
        string injectedStr = "HKVVB:999:99+3+0+1+BroilerFond+1.0'" + baselineStr;
        vectors.Add(new FinTsFuzzVector(
            "UnsupportedSegmentVersion",
            "Contains segment version 99 which violates the frozen FinTS 3.0 matrix",
            Encoding.Latin1.GetBytes(injectedStr),
            typeof(UnsupportedSegmentVersionException)));

        // 5. Injected speculative FinTS 4.1 segment
        string fints41Str = "HK41X:1:1+SpeculativeFinTs41Data'" + baselineStr;
        vectors.Add(new FinTsFuzzVector(
            "SpeculativeFinTs41Segment",
            "Contains unverified speculative FinTS 4.1 segment HK41X",
            Encoding.Latin1.GetBytes(fints41Str),
            typeof(UnsupportedSegmentVersionException)));

        // 6. Oversized wire bytes exceeding 1 MiB limit
        byte[] oversized = new byte[FinTsSyntax.MaximumWireBytes + 16];
        Array.Copy(validBaseline, oversized, Math.Min(validBaseline.Length, oversized.Length));
        for (int i = validBaseline.Length; i < oversized.Length; i++)
        {
            oversized[i] = (byte)'A';
        }
        oversized[^1] = (byte)'\'';
        vectors.Add(new FinTsFuzzVector(
            "OversizedWirePayload",
            "Message payload exceeding maximum 1 MiB wire limit",
            oversized,
            typeof(FinTsFormatException)));

        return vectors.AsReadOnly();
    }
}
