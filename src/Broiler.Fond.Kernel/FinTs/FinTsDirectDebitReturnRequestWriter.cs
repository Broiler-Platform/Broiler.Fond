using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes FinTS 3.0 SEPA direct-debit return wire segments (HKDRW) and mandate revocation segments (HKDMB).
/// </summary>
public static class FinTsDirectDebitReturnRequestWriter
{
    public const int DefaultHkdrwVersion = 1;
    public const int DefaultHkdmbVersion = 1;

    /// <summary>
    /// Builds an HKDRW segment string (SEPA-Lastschriftrückbuchung).
    /// </summary>
    public static string BuildHkdrwSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        string originalBankReference,
        DirectDebitReturnReason reason,
        Money amount,
        string creditorId,
        string mandateId,
        DateOnly originalDueDate)
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

        if (string.IsNullOrWhiteSpace(originalBankReference))
        {
            throw new ArgumentException("Original bank reference is required.", nameof(originalBankReference));
        }

        if (amount.Currency != "EUR" || amount.Amount <= 0m)
        {
            throw new ArgumentException("Amount must be strictly positive EUR.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(creditorId))
        {
            throw new ArgumentException("Creditor identifier is required.", nameof(creditorId));
        }

        if (string.IsNullOrWhiteSpace(mandateId))
        {
            throw new ArgumentException("Mandate reference is required.", nameof(mandateId));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string reasonCode = reason.ToReasonCode();
        string amtStr = amount.Amount.ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',');
        string escapedRef = Text(originalBankReference.Trim(), 35);
        string escapedCreditorId = Text(creditorId.Trim(), 35);
        string escapedMandateId = Text(mandateId.Trim(), 35);
        string dueDateStr = originalDueDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var builder = new StringBuilder("HKDRW:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            builder.Append(':').Append(escapedBic);
        }

        builder.Append('+')
            .Append(escapedRef)
            .Append('+')
            .Append(reasonCode)
            .Append('+')
            .Append(amtStr)
            .Append(":EUR+")
            .Append(escapedCreditorId)
            .Append('+')
            .Append(escapedMandateId)
            .Append('+')
            .Append(dueDateStr)
            .Append('\'');

        return builder.ToString();
    }

    /// <summary>
    /// Builds an HKDMB segment string (SEPA-Mandatsverwaltung: Mandatswiderruf / Stornierung).
    /// </summary>
    public static string BuildHkdmbSegment(
        int ordinal,
        int version,
        string originIban,
        string? originBic,
        string creditorId,
        string mandateId,
        DateOnly effectiveDate)
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

        if (string.IsNullOrWhiteSpace(creditorId))
        {
            throw new ArgumentException("Creditor identifier is required.", nameof(creditorId));
        }

        if (string.IsNullOrWhiteSpace(mandateId))
        {
            throw new ArgumentException("Mandate reference is required.", nameof(mandateId));
        }

        string escapedBic = string.IsNullOrWhiteSpace(originBic)
            ? string.Empty
            : Text(originBic.Trim(), 11);

        string escapedCreditorId = Text(creditorId.Trim(), 35);
        string escapedMandateId = Text(mandateId.Trim(), 35);
        string effectiveDateStr = effectiveDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var builder = new StringBuilder("HKDMB:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            .Append(normOriginIban);

        if (!string.IsNullOrEmpty(escapedBic))
        {
            builder.Append(':').Append(escapedBic);
        }

        builder.Append('+')
            .Append(escapedCreditorId)
            .Append('+')
            .Append(escapedMandateId)
            .Append("+1+") // 1 = Widerrufen / Cancel
            .Append(effectiveDateStr)
            .Append('\'');

        return builder.ToString();
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HKDRW.
    /// </summary>
    public static byte[] EncodeUnsignedReturn(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        string originalBankReference,
        DirectDebitReturnReason reason,
        Money amount,
        string creditorId,
        string mandateId,
        DateOnly originalDueDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvelopeParams(dialogueId, messageNumber);

        string hkdrw = BuildHkdrwSegment(
            2,
            version,
            originIban,
            originBic,
            originalBankReference,
            reason,
            amount,
            creditorId,
            mandateId,
            originalDueDate);

        return WrapInEnvelope(dialogueId, messageNumber, Encoding.Latin1.GetBytes(hkdrw));
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HKDMB (Mandatswiderruf).
    /// </summary>
    public static byte[] EncodeUnsignedMandateRevocation(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string? originBic,
        string creditorId,
        string mandateId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateEnvelopeParams(dialogueId, messageNumber);

        string hkdmb = BuildHkdmbSegment(
            2,
            version,
            originIban,
            originBic,
            creditorId,
            mandateId,
            effectiveDate);

        return WrapInEnvelope(dialogueId, messageNumber, Encoding.Latin1.GetBytes(hkdmb));
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
