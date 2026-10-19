using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes FinTS 3.0 Single SEPA Credit Transfer (HKCCS) requests and HKTAN authorization continuation segments.
/// </summary>
public static class FinTsSepaTransferRequestWriter
{
    public const int DefaultHkccsVersion = 1;

    /// <summary>
    /// Builds the raw wire bytes of an HKCCS segment containing international account connection,
    /// SEPA pain descriptor, and binary ISO 20022 XML payload.
    /// </summary>
    public static byte[] BuildHkccsSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace)
    {
        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Segment ordinal must be positive.");
        }

        if (version is not (1 or 2))
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion);
        }

        if (!SepaIbanValidator.Validate(originIban, out string normOriginIban))
        {
            throw new ArgumentException("Invalid origin IBAN.", nameof(originIban));
        }

        ArgumentNullException.ThrowIfNull(painXmlBytes);
        if (painXmlBytes.Length == 0)
        {
            throw new ArgumentException("Pain XML bytes cannot be empty.", nameof(painXmlBytes));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string escapedDescriptor = Text(sepaDescriptor.Trim(), 100);

        // Segment header: HKCCS:{ordinal}:{version}+
        var prefixBuilder = new StringBuilder("HKCCS:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            prefixBuilder.Append(':').Append(escapedBic);
        }

        prefixBuilder
            .Append('+')
            .Append(escapedDescriptor)
            .Append('+')
            .Append('@')
            .Append(painXmlBytes.Length.ToString(CultureInfo.InvariantCulture))
            .Append('@');

        byte[] prefixBytes = Encoding.Latin1.GetBytes(prefixBuilder.ToString());
        byte[] suffixBytes = [(byte)'\''];

        byte[] segmentBytes = new byte[prefixBytes.Length + painXmlBytes.Length + suffixBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, segmentBytes, 0, prefixBytes.Length);
        Buffer.BlockCopy(painXmlBytes, 0, segmentBytes, prefixBytes.Length, painXmlBytes.Length);
        Buffer.BlockCopy(suffixBytes, 0, segmentBytes, prefixBytes.Length + painXmlBytes.Length, suffixBytes.Length);

        return segmentBytes;
    }

    public const int DefaultHkipbVersion = 1;

    /// <summary>
    /// Builds the raw wire bytes of an HKIPB segment (SEPA Instant Payment Einzelauftrag)
    /// containing international account connection, SEPA pain descriptor, and binary ISO 20022 XML payload.
    /// </summary>
    public static byte[] BuildHkipbSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace)
    {
        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Segment ordinal must be positive.");
        }

        if (version < 1)
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion);
        }

        if (!SepaIbanValidator.Validate(originIban, out string normOriginIban))
        {
            throw new ArgumentException("Invalid origin IBAN.", nameof(originIban));
        }

        ArgumentNullException.ThrowIfNull(painXmlBytes);
        if (painXmlBytes.Length == 0)
        {
            throw new ArgumentException("Pain XML bytes cannot be empty.", nameof(painXmlBytes));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string escapedDescriptor = Text(sepaDescriptor.Trim(), 100);

        // Segment header: HKIPB:{ordinal}:{version}+
        var prefixBuilder = new StringBuilder("HKIPB:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            prefixBuilder.Append(':').Append(escapedBic);
        }

        prefixBuilder
            .Append('+')
            .Append(escapedDescriptor)
            .Append('+')
            .Append('@')
            .Append(painXmlBytes.Length.ToString(CultureInfo.InvariantCulture))
            .Append('@');

        byte[] prefixBytes = Encoding.Latin1.GetBytes(prefixBuilder.ToString());
        byte[] suffixBytes = [(byte)'\''];

        byte[] segmentBytes = new byte[prefixBytes.Length + painXmlBytes.Length + suffixBytes.Length];
        Buffer.BlockCopy(prefixBytes, 0, segmentBytes, 0, prefixBytes.Length);
        Buffer.BlockCopy(painXmlBytes, 0, segmentBytes, prefixBytes.Length, painXmlBytes.Length);
        Buffer.BlockCopy(suffixBytes, 0, segmentBytes, prefixBytes.Length + painXmlBytes.Length, suffixBytes.Length);

        return segmentBytes;
    }

    /// <summary>
    /// Builds an HKTAN process-2 (challenge completion) segment string containing the bank's order reference
    /// and the ephemeral user TAN.
    /// </summary>
    public static string BuildHktanCompletionSegment(
        int ordinal,
        int version,
        string orderReference,
        string tan)
    {
        if (ordinal < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Segment ordinal must be positive.");
        }

        if (string.IsNullOrWhiteSpace(orderReference))
        {
            throw new ArgumentException("Order reference cannot be null or whitespace.", nameof(orderReference));
        }

        if (string.IsNullOrWhiteSpace(tan))
        {
            throw new ArgumentException("TAN cannot be null or whitespace.", nameof(tan));
        }

        string escapedRef = Text(orderReference.Trim(), 35);
        string escapedTan = Text(tan.Trim(), 35);

        return new StringBuilder("HKTAN:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append("+2+")
            .Append(escapedRef)
            .Append('+')
            .Append(escapedTan)
            .Append('\'')
            .ToString();
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HNHBK, HKCCS, and HNHBS.
    /// </summary>
    public static byte[] EncodeUnsigned(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(dialogueId) || dialogueId is "0" or "unbekannt")
        {
            throw new ArgumentException("A valid established dialogue ID is required.", nameof(dialogueId));
        }

        if (messageNumber is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(messageNumber), "Message number must be between 1 and 9999.");
        }

        byte[] hkccsBytes = BuildHkccsSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            painXmlBytes: painXmlBytes,
            sepaDescriptor: sepaDescriptor);

        string escapedDialogue = Text(dialogueId.Trim(), 30);
        string msgNumStr = messageNumber.ToString(CultureInfo.InvariantCulture);

        string hnhbkHeader = $"HNHBK:1:3+000000000000+300+{escapedDialogue}+{msgNumStr}'";
        string hnhbsTrailer = $"HNHBS:3:1+{msgNumStr}'";

        byte[] hnhbkBytes = Encoding.Latin1.GetBytes(hnhbkHeader);
        byte[] hnhbsBytes = Encoding.Latin1.GetBytes(hnhbsTrailer);

        int totalLength = hnhbkBytes.Length + hkccsBytes.Length + hnhbsBytes.Length;
        if (totalLength > FinTsSyntax.MaximumWireBytes)
        {
            throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
        }

        // Format 12-digit size into hnhbkBytes: HNHBK:1:3+ (offset 10)
        string sizeStr = totalLength.ToString("D12", CultureInfo.InvariantCulture);
        byte[] sizeAscii = Encoding.ASCII.GetBytes(sizeStr);
        Buffer.BlockCopy(sizeAscii, 0, hnhbkBytes, 10, 12);

        byte[] wire = new byte[totalLength];
        Buffer.BlockCopy(hnhbkBytes, 0, wire, 0, hnhbkBytes.Length);
        Buffer.BlockCopy(hkccsBytes, 0, wire, hnhbkBytes.Length, hkccsBytes.Length);
        Buffer.BlockCopy(hnhbsBytes, 0, wire, hnhbkBytes.Length + hkccsBytes.Length, hnhbsBytes.Length);

        return wire;
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HNHBK, HKIPB, and HNHBS for SEPA Instant Payments.
    /// </summary>
    public static byte[] EncodeUnsignedInstant(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(dialogueId) || dialogueId is "0" or "unbekannt")
        {
            throw new ArgumentException("A valid established dialogue ID is required.", nameof(dialogueId));
        }

        if (messageNumber is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(messageNumber), "Message number must be between 1 and 9999.");
        }

        byte[] hkipbBytes = BuildHkipbSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            painXmlBytes: painXmlBytes,
            sepaDescriptor: sepaDescriptor);

        string escapedDialogue = Text(dialogueId.Trim(), 30);
        string msgNumStr = messageNumber.ToString(CultureInfo.InvariantCulture);

        string hnhbkHeader = $"HNHBK:1:3+000000000000+300+{escapedDialogue}+{msgNumStr}'";
        string hnhbsTrailer = $"HNHBS:3:1+{msgNumStr}'";

        byte[] hnhbkBytes = Encoding.Latin1.GetBytes(hnhbkHeader);
        byte[] hnhbsBytes = Encoding.Latin1.GetBytes(hnhbsTrailer);

        int totalLength = hnhbkBytes.Length + hkipbBytes.Length + hnhbsBytes.Length;
        if (totalLength > FinTsSyntax.MaximumWireBytes)
        {
            throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
        }

        string sizeStr = totalLength.ToString("D12", CultureInfo.InvariantCulture);
        byte[] sizeAscii = Encoding.ASCII.GetBytes(sizeStr);
        Buffer.BlockCopy(sizeAscii, 0, hnhbkBytes, 10, 12);

        byte[] wire = new byte[totalLength];
        Buffer.BlockCopy(hnhbkBytes, 0, wire, 0, hnhbkBytes.Length);
        Buffer.BlockCopy(hkipbBytes, 0, wire, hnhbkBytes.Length, hkipbBytes.Length);
        Buffer.BlockCopy(hnhbsBytes, 0, wire, hnhbkBytes.Length + hkipbBytes.Length, hnhbsBytes.Length);

        return wire;
    }

    private static string Text(string value, int maximum) =>
        FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidReadData);
}
