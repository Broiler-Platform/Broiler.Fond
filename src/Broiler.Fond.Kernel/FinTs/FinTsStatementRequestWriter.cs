using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Encodes credential-free, unsigned HKKAZ (MT940) and HKCAZ (CAMT) statement range query requests.
/// </summary>
public static class FinTsStatementRequestWriter
{
    public const int MaximumContinuationTokenLength = 35;
    public const int MaximumCamtFormatLength = 256;

    /// <summary>
    /// Encodes an unsigned HKKAZ statement query (versions 5, 6, 7).
    /// </summary>
    public static byte[] EncodeMt940(
        string dialogueId,
        int messageNumber,
        int version,
        FinTsReadAccountInput account,
        bool allAccounts = false,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int? maximumEntries = null,
        string? continuationToken = null,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (version is not (5 or 6 or 7))
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion);
        }

        ValidateRangesAndTokens(fromDate, toDate, maximumEntries, continuationToken);

        var body = new StringBuilder("HKKAZ:2:").Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+').Append(FormatAccount(account, nationalOnly: version == 5 && string.IsNullOrEmpty(account.Iban)))
            .Append(allAccounts ? "+J" : "+N")
            .Append('+').Append(FormatDate(fromDate))
            .Append('+').Append(FormatDate(toDate));

        if (maximumEntries.HasValue || continuationToken is not null || (version == 7 && format is not null))
        {
            body.Append('+').Append(maximumEntries?.ToString(CultureInfo.InvariantCulture));
        }

        if (continuationToken is not null || (version == 7 && format is not null))
        {
            body.Append('+').Append(continuationToken is not null ? Text(continuationToken, MaximumContinuationTokenLength) : "");
        }

        if (version == 7 && format is not null)
        {
            body.Append('+').Append(Text(format, 30));
        }

        body.Append('\'');
        return Frame(dialogueId, messageNumber, body, cancellationToken);
    }

    /// <summary>
    /// Encodes an unsigned HKCAZ statement query (versions 1, 2).
    /// </summary>
    public static byte[] EncodeCamt(
        string dialogueId,
        int messageNumber,
        int version,
        FinTsReadAccountInput account,
        string camtFormat,
        bool allAccounts = false,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int? maximumEntries = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (version is not (1 or 2))
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion);
        }

        if (string.IsNullOrWhiteSpace(camtFormat))
        {
            throw ReadDataFields.Invalid();
        }

        ValidateRangesAndTokens(fromDate, toDate, maximumEntries, continuationToken);

        var body = new StringBuilder("HKCAZ:2:").Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+').Append(FormatAccount(account, nationalOnly: false))
            .Append('+').Append(Text(camtFormat, MaximumCamtFormatLength))
            .Append(allAccounts ? "+J" : "+N")
            .Append('+').Append(FormatDate(fromDate))
            .Append('+').Append(FormatDate(toDate));

        if (maximumEntries.HasValue || continuationToken is not null)
        {
            body.Append('+').Append(maximumEntries?.ToString(CultureInfo.InvariantCulture));
        }

        if (continuationToken is not null)
        {
            body.Append('+').Append(Text(continuationToken, MaximumContinuationTokenLength));
        }

        body.Append('\'');
        return Frame(dialogueId, messageNumber, body, cancellationToken);
    }

    private static void ValidateRangesAndTokens(DateOnly? fromDate, DateOnly? toDate, int? maximumEntries, string? continuationToken)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            throw ReadDataFields.Invalid();
        }

        if (maximumEntries is < 1 or > 9999)
        {
            throw ReadDataFields.Invalid();
        }

        if (continuationToken is { Length: 0 })
        {
            throw ReadDataFields.Invalid();
        }
    }

    private static string FormatDate(DateOnly? date) =>
        date?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "";

    private static string FormatAccount(FinTsReadAccountInput account, bool nationalOnly)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (nationalOnly && (account.Iban.Length != 0 || account.Bic.Length != 0))
        {
            throw ReadDataFields.Invalid();
        }

        string[] national = [Text(account.Number, 30), Text(account.Subaccount, 30), Text(account.Country, 3), Text(account.Institution, 30)];
        string[] fields = nationalOnly ? national : [Text(account.Iban, 34), Text(account.Bic, 11), .. national];
        int length = fields.Length, minimum = nationalOnly ? 3 : 2;
        while (length > minimum && fields[length - 1].Length == 0)
        {
            length--;
        }
        return string.Join(":", fields, 0, length);
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
