using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes FinTS 3.0 SEPA standing order wire segments: query list (HKCDB), setup (HKCDE),
/// modification (HKCDA), and deletion (HKCDL).
/// </summary>
public static class FinTsStandingOrderRequestWriter
{
    public const int DefaultHkcdbVersion = 1;
    public const int DefaultHkcdeVersion = 1;
    public const int DefaultHkcdaVersion = 1;
    public const int DefaultHkcdlVersion = 1;

    /// <summary>
    /// Builds an HKCDB segment string to query active standing orders for an account.
    /// </summary>
    public static string BuildHkcdbSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic)
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

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        var builder = new StringBuilder("HKCDB:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            builder.Append(':').Append(escapedBic);
        }

        builder.Append('\'');
        return builder.ToString();
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message querying standing orders.
    /// </summary>
    public static byte[] EncodeUnsignedListQuery(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateEnvelopeParams(dialogueId, messageNumber);

        string hkcdb = BuildHkcdbSegment(2, version, originIban, originBic);
        return WrapInEnvelope(dialogueId, messageNumber, Encoding.Latin1.GetBytes(hkcdb));
    }

    /// <summary>
    /// Builds the raw wire bytes of an HKCDE segment (setup standing order).
    /// </summary>
    public static byte[] BuildHkcdeSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
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

        string firstDateStr = firstExecutionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string lastDateStr = lastExecutionDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
        string escapedDescriptor = Text(sepaDescriptor.Trim(), 100);

        var prefixBuilder = new StringBuilder("HKCDE:")
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
            .Append(((int)frequency).ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(executionDay.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(firstDateStr)
            .Append('+')
            .Append(lastDateStr)
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
    /// Encodes a complete 3-segment unsigned FinTS message setting up a standing order.
    /// </summary>
    public static byte[] EncodeUnsignedSetup(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvelopeParams(dialogueId, messageNumber);

        byte[] hkcdeBytes = BuildHkcdeSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            frequency: frequency,
            executionDay: executionDay,
            firstExecutionDate: firstExecutionDate,
            lastExecutionDate: lastExecutionDate,
            painXmlBytes: painXmlBytes,
            sepaDescriptor: sepaDescriptor);

        return WrapInEnvelope(dialogueId, messageNumber, hkcdeBytes);
    }

    /// <summary>
    /// Builds the raw wire bytes of an HKCDA segment (modify standing order).
    /// </summary>
    public static byte[] BuildHkcdaSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        string orderReference,
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
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

        if (string.IsNullOrWhiteSpace(orderReference))
        {
            throw new ArgumentException("Order reference cannot be empty.", nameof(orderReference));
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

        string escapedRef = Text(orderReference.Trim(), 35);
        string firstDateStr = firstExecutionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string lastDateStr = lastExecutionDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
        string escapedDescriptor = Text(sepaDescriptor.Trim(), 100);

        var prefixBuilder = new StringBuilder("HKCDA:")
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
            .Append(escapedRef)
            .Append('+')
            .Append(((int)frequency).ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(executionDay.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(firstDateStr)
            .Append('+')
            .Append(lastDateStr)
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
    /// Encodes a complete 3-segment unsigned FinTS message modifying a standing order.
    /// </summary>
    public static byte[] EncodeUnsignedModify(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        string orderReference,
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
        byte[] painXmlBytes,
        string sepaDescriptor = SepaPainDocumentWriter.PainNamespace,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvelopeParams(dialogueId, messageNumber);

        byte[] hkcdaBytes = BuildHkcdaSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            orderReference: orderReference,
            frequency: frequency,
            executionDay: executionDay,
            firstExecutionDate: firstExecutionDate,
            lastExecutionDate: lastExecutionDate,
            painXmlBytes: painXmlBytes,
            sepaDescriptor: sepaDescriptor);

        return WrapInEnvelope(dialogueId, messageNumber, hkcdaBytes);
    }

    /// <summary>
    /// Builds an HKCDL segment string to delete or cancel an active standing order.
    /// </summary>
    public static string BuildHkcdlSegment(
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

        if (string.IsNullOrWhiteSpace(orderReference))
        {
            throw new ArgumentException("Order reference cannot be empty.", nameof(orderReference));
        }

        if (!SepaIbanValidator.Validate(originIban, out string normOriginIban))
        {
            throw new ArgumentException("Invalid origin IBAN.", nameof(originIban));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string escapedRef = Text(orderReference.Trim(), 35);

        var builder = new StringBuilder("HKCDL:")
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
    /// Encodes a complete 3-segment unsigned FinTS message deleting a standing order.
    /// </summary>
    public static byte[] EncodeUnsignedDelete(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        string orderReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvelopeParams(dialogueId, messageNumber);

        string hkcdl = BuildHkcdlSegment(2, version, originIban, originBic, orderReference);
        return WrapInEnvelope(dialogueId, messageNumber, Encoding.Latin1.GetBytes(hkcdl));
    }

    private static void ValidateEnvelopeParams(string dialogueId, int messageNumber)
    {
        if (string.IsNullOrWhiteSpace(dialogueId) || dialogueId is "0" or "unbekannt")
        {
            throw new ArgumentException("A valid established dialogue ID is required.", nameof(dialogueId));
        }

        if (messageNumber is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(messageNumber), "Message number must be between 1 and 9999.");
        }
    }

    private static byte[] WrapInEnvelope(string dialogueId, int messageNumber, byte[] middleSegmentBytes)
    {
        string escapedDialogue = Text(dialogueId.Trim(), 30);
        string msgNumStr = messageNumber.ToString(CultureInfo.InvariantCulture);

        string hnhbkHeader = $"HNHBK:1:3+000000000000+300+{escapedDialogue}+{msgNumStr}'";
        string hnhbsTrailer = $"HNHBS:3:1+{msgNumStr}'";

        byte[] hnhbkBytes = Encoding.Latin1.GetBytes(hnhbkHeader);
        byte[] hnhbsBytes = Encoding.Latin1.GetBytes(hnhbsTrailer);

        int totalLength = hnhbkBytes.Length + middleSegmentBytes.Length + hnhbsBytes.Length;
        if (totalLength > FinTsSyntax.MaximumWireBytes)
        {
            throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
        }

        string sizeStr = totalLength.ToString("D12", CultureInfo.InvariantCulture);
        byte[] sizeAscii = Encoding.ASCII.GetBytes(sizeStr);
        Buffer.BlockCopy(sizeAscii, 0, hnhbkBytes, 10, 12);

        byte[] wire = new byte[totalLength];
        Buffer.BlockCopy(hnhbkBytes, 0, wire, 0, hnhbkBytes.Length);
        Buffer.BlockCopy(middleSegmentBytes, 0, wire, hnhbkBytes.Length, middleSegmentBytes.Length);
        Buffer.BlockCopy(hnhbsBytes, 0, wire, hnhbkBytes.Length + middleSegmentBytes.Length, hnhbsBytes.Length);

        return wire;
    }

    private static string Text(string value, int maximum) =>
        FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidReadData);
}
