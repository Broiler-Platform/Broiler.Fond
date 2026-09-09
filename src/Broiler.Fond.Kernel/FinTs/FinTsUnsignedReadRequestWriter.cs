using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Caller-supplied identifiers, preserved verbatim. Validated for the selected schema when encoded.</summary>
public sealed class FinTsReadAccountInput
{
    public FinTsReadAccountInput(string number = "", string subaccount = "", string country = "", string institution = "", string iban = "", string bic = "")
    {
        ArgumentNullException.ThrowIfNull(number); ArgumentNullException.ThrowIfNull(subaccount);
        ArgumentNullException.ThrowIfNull(country); ArgumentNullException.ThrowIfNull(institution);
        ArgumentNullException.ThrowIfNull(iban); ArgumentNullException.ThrowIfNull(bic);
        Number = number; Subaccount = subaccount; Country = country; Institution = institution; Iban = iban; Bic = bic;
    }
    public string Number { get; }
    public string Subaccount { get; }
    public string Country { get; }
    public string Institution { get; }
    public string Iban { get; }
    public string Bic { get; }
}

/// <summary>
/// Credential-free, unsigned three-segment frames for local schema/context verification.
/// No capability decision, negotiated charset, security envelope, counter allocation or send authorization.
/// </summary>
public static class FinTsUnsignedReadRequestWriter
{
    public const int MaximumDiscoveryAccounts = 999;

    /// <summary>An empty list requests all accounts. List order and duplicates are preserved.</summary>
    public static byte[] EncodeDiscovery(string dialogueId, int messageNumber, IReadOnlyList<FinTsReadAccountInput> accounts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        cancellationToken.ThrowIfCancellationRequested();
        int count = accounts.Count;
        if (count > MaximumDiscoveryAccounts) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        var body = new StringBuilder("HKSPA:2:1");
        for (int index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            body.Append('+').Append(Account(accounts[index], nationalOnly: true));
        }
        body.Append('\'');
        return Frame(dialogueId, messageNumber, body, cancellationToken);
    }

    /// <summary>Optional pagination fields encode supplied observations only; continuation scope is checked separately.</summary>
    public static byte[] EncodeBalance(string dialogueId, int messageNumber, int version, FinTsReadAccountInput account,
        bool allAccounts = false, int? maximumEntries = null, string? continuationToken = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (version is not (6 or 7 or 8)) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion); }
        if (maximumEntries is < 1 or > 9999) { throw ReadDataFields.Invalid(); }
        if (continuationToken is { Length: 0 }) { throw ReadDataFields.Invalid(); }
        var body = new StringBuilder("HKSAL:2:").Append(version.ToString(CultureInfo.InvariantCulture))
            .Append('+').Append(Account(account, nationalOnly: version == 6)).Append(allAccounts ? "+J" : "+N");
        if (maximumEntries.HasValue || continuationToken is not null)
        { body.Append('+').Append(maximumEntries?.ToString(CultureInfo.InvariantCulture)); }
        if (continuationToken is not null) { body.Append('+').Append(Text(continuationToken, 35)); }
        body.Append('\'');
        return Frame(dialogueId, messageNumber, body, cancellationToken);
    }

    private static string Account(FinTsReadAccountInput account, bool nationalOnly)
    {
        ArgumentNullException.ThrowIfNull(account);
        // A national schema must never silently discard supplied international identifiers.
        if (nationalOnly && (account.Iban.Length != 0 || account.Bic.Length != 0)) { throw ReadDataFields.Invalid(); }
        string[] national = [Text(account.Number, 30), Text(account.Subaccount, 30), Text(account.Country, 3), Text(account.Institution, 30)];
        string[] fields = nationalOnly ? national : [Text(account.Iban, 34), Text(account.Bic, 11), .. national];
        int length = fields.Length, minimum = nationalOnly ? 3 : 2;
        while (length > minimum && fields[length - 1].Length == 0) { length--; }
        return string.Join(":", fields, 0, length);
    }

    private static string Text(string value, int maximum) => FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidReadData);

    private static byte[] Frame(string dialogueId, int messageNumber, StringBuilder body, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string dialogue = Text(dialogueId, 30);
        if (dialogueId.Length == 0 || dialogueId is "0" or "unbekannt" || messageNumber is < 1 or > 9999) { throw ReadDataFields.Invalid(); }
        byte[] bytes = FinTsUnsignedWireEncoding.Frame(dialogue, messageNumber, body, 3);
        // Share the established schema rules before handing any bytes to the caller.
        _ = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(bytes, token), 1, token);
        token.ThrowIfCancellationRequested();
        return bytes;
    }
}
