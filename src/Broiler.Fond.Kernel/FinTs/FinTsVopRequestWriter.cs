using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes credential-free, unsigned HKVOP (Verification of Payee / Empfängerüberprüfung) requests
/// per FinTS 3.0 specifications and Regulation (EU) 2024/886.
/// </summary>
public static class FinTsVopRequestWriter
{
    public const int DefaultVersion = 1;
    public const int MaxPayeeNameLength = 70;

    /// <summary>
    /// Builds the escaped HKVOP segment string without outer envelope.
    /// </summary>
    public static string BuildSegment(
        int ordinal,
        int version,
        string originIban,
        string originBic,
        string payeeName,
        string payeeIban,
        string? payeeBic,
        decimal amount,
        string currency)
    {
        if (version is not (1 or 2))
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion);
        }

        if (!SepaIbanValidator.Validate(originIban, out string normOriginIban))
        {
            throw new ArgumentException("Invalid origin IBAN.", nameof(originIban));
        }

        if (!SepaIbanValidator.Validate(payeeIban, out string normPayeeIban))
        {
            throw new ArgumentException("Invalid payee IBAN.", nameof(payeeIban));
        }

        if (string.IsNullOrWhiteSpace(payeeName) || payeeName.Length > MaxPayeeNameLength)
        {
            throw new ArgumentException($"Payee name must be between 1 and {MaxPayeeNameLength} characters.", nameof(payeeName));
        }

        if (amount <= 0m)
        {
            throw new ArgumentException("Amount must be strictly positive.", nameof(amount));
        }

        if (!string.Equals(currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Currency must be EUR for SEPA VoP.", nameof(currency));
        }

        string escapedOriginBic = Text(originBic ?? string.Empty, 11);
        string escapedPayeeName = Text(payeeName.Trim(), MaxPayeeNameLength);
        string escapedPayeeBic = Text(payeeBic ?? string.Empty, 11);
        string amountStr = amount.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

        var segment = new StringBuilder("HKVOP:")
            .Append(ordinal.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+')
            // DE 1: Origin account [IBAN]:[BIC]
            .Append(normOriginIban)
            .Append(':')
            .Append(escapedOriginBic)
            .Append('+')
            // DE 2: Payee details [Name]:[IBAN]:[BIC]
            .Append(escapedPayeeName)
            .Append(':')
            .Append(normPayeeIban)
            .Append(':')
            .Append(escapedPayeeBic)
            .Append('+')
            // DE 3: Amount [Amount]:[Currency]
            .Append(amountStr)
            .Append(':')
            .Append("EUR")
            .Append('\'');

        return segment.ToString();
    }

    /// <summary>
    /// Encodes a complete 3-segment unsigned FinTS message containing HKVOP.
    /// </summary>
    public static byte[] Encode(
        string dialogueId,
        int messageNumber,
        int version,
        string originIban,
        string originBic,
        string payeeName,
        string payeeIban,
        string? payeeBic,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string segmentStr = BuildSegment(
            ordinal: 2,
            version: version,
            originIban: originIban,
            originBic: originBic,
            payeeName: payeeName,
            payeeIban: payeeIban,
            payeeBic: payeeBic,
            amount: amount,
            currency: currency);

        var body = new StringBuilder(segmentStr);
        return Frame(dialogueId, messageNumber, body, cancellationToken);
    }

    private static string Text(string value, int maximum) =>
        FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidReadData);

    private static byte[] Frame(string dialogueId, int messageNumber, StringBuilder body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string dialogue = Text(dialogueId, 30);
        if (dialogueId.Length == 0 || dialogueId is "0" or "unbekannt" || messageNumber is < 1 or > 9999)
        {
            throw ReadDataFields.Invalid();
        }

        byte[] bytes = FinTsUnsignedWireEncoding.Frame(dialogue, messageNumber, body, 3);
        token.ThrowIfCancellationRequested();
        return bytes;
    }
}
