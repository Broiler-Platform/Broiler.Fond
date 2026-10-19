using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes FinTS 3.0 Scheduled SEPA Credit Transfer submission (HKCME) and cancellation (HKCBD) request segments.
/// </summary>
public static class FinTsScheduledTransferRequestWriter
{
    public const int DefaultHkcmeVersion = 1;
    public const int DefaultHkcbdVersion = 1;

    /// <summary>
    /// Builds the raw wire bytes of an HKCME segment (SEPA-Terminüberweisung einreichen).
    /// </summary>
    public static byte[] BuildHkcmeSegment(
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

        var prefixBuilder = new StringBuilder("HKCME:")
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
    /// Encodes a complete 3-segment unsigned FinTS message containing HNHBK, HKCME, and HNHBS.
    /// </summary>
    public static byte[] EncodeUnsignedScheduled(
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

        byte[] hkcmeBytes = BuildHkcmeSegment(
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

        int totalLength = hnhbkBytes.Length + hkcmeBytes.Length + hnhbsBytes.Length;
        if (totalLength > FinTsSyntax.MaximumWireBytes)
        {
            throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
        }

        string sizeStr = totalLength.ToString("D12", CultureInfo.InvariantCulture);
        byte[] sizeAscii = Encoding.ASCII.GetBytes(sizeStr);
        Buffer.BlockCopy(sizeAscii, 0, hnhbkBytes, 10, 12);

        byte[] wire = new byte[totalLength];
        Buffer.BlockCopy(hnhbkBytes, 0, wire, 0, hnhbkBytes.Length);
        Buffer.BlockCopy(hkcmeBytes, 0, wire, hnhbkBytes.Length, hkcmeBytes.Length);
        Buffer.BlockCopy(hnhbsBytes, 0, wire, hnhbkBytes.Length + hkcmeBytes.Length, hnhbsBytes.Length);

        return wire;
    }

    /// <summary>
    /// Builds an HKCBD segment string to delete or cancel an active scheduled credit transfer at the bank.
    /// </summary>
    public static string BuildHkcbdSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        string orderReference)
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

        if (string.IsNullOrWhiteSpace(orderReference))
        {
            throw new ArgumentException("Order reference cannot be empty.", nameof(orderReference));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string escapedRef = Text(orderReference.Trim(), 35);

        var builder = new StringBuilder("HKCBD:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            builder.Append(':').Append(escapedBic);
        }

        builder.Append('+').Append(escapedRef).Append('\'');
        return builder.ToString();
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HNHBK, HKCBD, and HNHBS.
    /// </summary>
    public static byte[] EncodeUnsignedCancellation(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        string orderReference,
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

        string hkcbd = BuildHkcbdSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            orderReference: orderReference);

        string escapedDialogue = Text(dialogueId.Trim(), 30);
        string msgNumStr = messageNumber.ToString(CultureInfo.InvariantCulture);

        string hnhbkHeader = $"HNHBK:1:3+000000000000+300+{escapedDialogue}+{msgNumStr}'";
        string hnhbsTrailer = $"HNHBS:3:1+{msgNumStr}'";

        byte[] hnhbkBytes = Encoding.Latin1.GetBytes(hnhbkHeader);
        byte[] hkcbdBytes = Encoding.Latin1.GetBytes(hkcbd);
        byte[] hnhbsBytes = Encoding.Latin1.GetBytes(hnhbsTrailer);

        int totalLength = hnhbkBytes.Length + hkcbdBytes.Length + hnhbsBytes.Length;
        if (totalLength > FinTsSyntax.MaximumWireBytes)
        {
            throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
        }

        string sizeStr = totalLength.ToString("D12", CultureInfo.InvariantCulture);
        byte[] sizeAscii = Encoding.ASCII.GetBytes(sizeStr);
        Buffer.BlockCopy(sizeAscii, 0, hnhbkBytes, 10, 12);

        byte[] wire = new byte[totalLength];
        Buffer.BlockCopy(hnhbkBytes, 0, wire, 0, hnhbkBytes.Length);
        Buffer.BlockCopy(hkcbdBytes, 0, wire, hnhbkBytes.Length, hkcbdBytes.Length);
        Buffer.BlockCopy(hnhbsBytes, 0, wire, hnhbkBytes.Length + hkcbdBytes.Length, hnhbsBytes.Length);

        return wire;
    }

    private static string Text(string value, int maximum) =>
        FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidReadData);
}
