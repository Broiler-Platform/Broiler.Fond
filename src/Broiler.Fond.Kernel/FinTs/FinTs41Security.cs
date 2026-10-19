using System.Globalization;
using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// FinTS 4.1 signature header (HNSHK v4) for PIN/TAN security operations.
/// </summary>
public sealed record FinTs41SecurityHeader(
    string SecurityProfile,
    string SecurityFunction,
    string SecurityReference,
    DateTimeOffset SecurityTimestamp)
{
    public FinTs41Segment ToSegment(int segNo = 1)
    {
        return new FinTs41Segment("HNSHK", 4, segNo,
        [
            FinTs41Element.Leaf("securityProfile", SecurityProfile),
            FinTs41Element.Leaf("securityFunction", SecurityFunction),
            FinTs41Element.Leaf("securityReference", SecurityReference),
            FinTs41Element.Leaf("securityTimestamp", SecurityTimestamp.ToString("O", CultureInfo.InvariantCulture))
        ]);
    }

    public static FinTs41SecurityHeader FromSegment(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HNSHK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected segment 'HNSHK', but got '{segment.Tag}'.", nameof(segment));
        }

        string profile = segment.GetElementValue("securityProfile") ?? "PIN:1";
        string function = segment.GetElementValue("securityFunction") ?? "999";
        string reference = segment.GetElementValue("securityReference") ?? string.Empty;
        string? tsRaw = segment.GetElementValue("securityTimestamp");
        DateTimeOffset ts = DateTimeOffset.TryParse(tsRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

        return new FinTs41SecurityHeader(profile, function, reference, ts);
    }
}

/// <summary>
/// FinTS 4.1 signature trailer (HNSHA v2) encapsulating transient PIN/TAN transmission.
/// Implements IDisposable to zeroize credentials immediately after use.
/// </summary>
public sealed class FinTs41SecurityTrailer : IDisposable
{
    private char[]? _pin;
    private char[]? _tan;
    private bool _disposed;

    public string SecurityReference { get; }

    public FinTs41SecurityTrailer(string securityReference, ReadOnlySpan<char> pin, ReadOnlySpan<char> tan = default)
    {
        SecurityReference = securityReference ?? string.Empty;
        if (!pin.IsEmpty)
        {
            _pin = pin.ToArray();
        }
        if (!tan.IsEmpty)
        {
            _tan = tan.ToArray();
        }
    }

    public FinTs41Segment ToSegment(int segNo = 999)
    {
        ThrowIfDisposed();

        var elements = new List<FinTs41Element>
        {
            FinTs41Element.Leaf("securityReference", SecurityReference)
        };

        if (_pin != null && _pin.Length > 0)
        {
            elements.Add(FinTs41Element.Leaf("pin", new string(_pin)));
        }

        if (_tan != null && _tan.Length > 0)
        {
            elements.Add(FinTs41Element.Leaf("tan", new string(_tan)));
        }

        return new FinTs41Segment("HNSHA", 2, segNo, elements);
    }

    public static FinTs41SecurityTrailer FromSegment(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HNSHA", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Expected segment 'HNSHA', but got '{segment.Tag}'.", nameof(segment));
        }

        string reference = segment.GetElementValue("securityReference") ?? string.Empty;
        string? pinStr = segment.GetElementValue("pin");
        string? tanStr = segment.GetElementValue("tan");

        return new FinTs41SecurityTrailer(
            reference,
            pinStr.AsSpan(),
            tanStr.AsSpan());
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_pin != null)
        {
            Array.Clear(_pin, 0, _pin.Length);
            _pin = null;
        }

        if (_tan != null)
        {
            Array.Clear(_tan, 0, _tan.Length);
            _tan = null;
        }

        _disposed = true;
    }

    public override string ToString() =>
        $"FinTs41SecurityTrailer(Ref='{SecurityReference}', PIN=***, TAN={(_tan != null ? "***" : "none")})";

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FinTs41SecurityTrailer), "Security trailer credentials have been zeroized.");
        }
    }
}

/// <summary>
/// FinTS 4.1 Two-Step Strong Customer Authentication (SCA) challenge extracted from HITAN v7.
/// </summary>
public sealed record FinTs41ScaChallenge(
    int ProcessVariant,
    string? ChallengeText,
    string? ChallengeMatrix,
    string? ChallengeHhduc,
    string? TanMediumName)
{
    public bool IsDecoupledOrPush => ProcessVariant == 1 && string.IsNullOrEmpty(ChallengeMatrix) && string.IsNullOrEmpty(ChallengeHhduc);
    public bool IsPhotoTan => !string.IsNullOrEmpty(ChallengeMatrix);
    public bool IsFlicker => !string.IsNullOrEmpty(ChallengeHhduc);
}

/// <summary>
/// Parser and generator for FinTS 4.1 SCA challenge continuation (HKTAN v7 / HITAN v7).
/// </summary>
public static class FinTs41ScaChallengeParser
{
    public static FinTs41ScaChallenge? ParseHitan(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HITAN", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int variant = 1;
        if (int.TryParse(segment.GetElementValue("processVariant"), CultureInfo.InvariantCulture, out int v))
        {
            variant = v;
        }

        string? text = segment.GetElementValue("challenge");
        string? matrix = segment.GetElementValue("challengeMatrix");
        string? hhduc = segment.GetElementValue("challengeHhduc");
        string? medium = segment.GetElementValue("tanMediumName");

        return new FinTs41ScaChallenge(variant, text, matrix, hhduc, medium);
    }

    public static FinTs41Segment CreateHktanRequest(
        int segNo,
        int processVariant,
        string? tanMediumName = null,
        string? responseTan = null)
    {
        var elements = new List<FinTs41Element>
        {
            FinTs41Element.Leaf("processVariant", processVariant.ToString(CultureInfo.InvariantCulture))
        };

        if (!string.IsNullOrWhiteSpace(tanMediumName))
        {
            elements.Add(FinTs41Element.Leaf("tanMediumName", tanMediumName));
        }

        if (!string.IsNullOrWhiteSpace(responseTan))
        {
            elements.Add(FinTs41Element.Leaf("responseTan", responseTan));
        }

        return new FinTs41Segment("HKTAN", 7, segNo, elements);
    }

    public static void ValidateSecurityBinding(FinTs41SecurityHeader header, FinTs41SecurityTrailer trailer)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(trailer);

        if (!string.Equals(header.SecurityReference, trailer.SecurityReference, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"FinTS 4.1 security signature binding mismatch: HNSHK reference '{header.SecurityReference}' does not match HNSHA reference '{trailer.SecurityReference}'.");
        }
    }
}
