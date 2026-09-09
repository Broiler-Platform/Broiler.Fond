using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Untrusted source identifiers; no normalization, checksum, ownership or UPD consistency decision.</summary>
public sealed class FinTsReadAccount
{
    internal FinTsReadAccount(FinTsField source, bool? sepa, string iban, string bic, string number, string subaccount, string country, string institution)
    { Source = source; SepaUsageReported = sepa; Iban = iban; Bic = bic; Number = number; Subaccount = subaccount; Country = country; Institution = institution; }
    public FinTsField Source { get; }
    public bool? SepaUsageReported { get; }
    public string Iban { get; }
    public string Bic { get; }
    public string Number { get; }
    public string Subaccount { get; }
    public string Country { get; }
    public string Institution { get; }
}

/// <summary>Unsigned wire amount, exact to all transmitted digits. Currency is a source code, not a catalog match.</summary>
public sealed class FinTsReadAmount
{
    internal FinTsReadAmount(decimal value, string currency) { Value = value; Currency = currency; }
    public decimal Value { get; }
    public string Currency { get; }
}

/// <summary>Source calendar values without a timezone or trusted retrieval time.</summary>
public sealed class FinTsReadTimestamp
{
    internal FinTsReadTimestamp(DateOnly date, TimeOnly? time) { Date = date; Time = time; }
    public DateOnly Date { get; }
    public TimeOnly? Time { get; }
}

public sealed class FinTsReadBalance
{
    internal FinTsReadBalance(string indicator, FinTsReadAmount amount, FinTsReadTimestamp timestamp)
    { CreditDebitIndicator = indicator; Amount = amount; Timestamp = timestamp; }
    public string CreditDebitIndicator { get; }
    public FinTsReadAmount Amount { get; }
    public FinTsReadTimestamp Timestamp { get; }
    public decimal SignedValue => CreditDebitIndicator == "D" ? -Amount.Value : Amount.Value;
}

/// <summary>Credential-free segment observation only. Parsing never grants permission to send.</summary>
public sealed class FinTsReadRequest
{
    private FinTsReadRequest(FinTsSegment source, List<FinTsReadAccount> accounts, bool all, int? maximum, string? continuation)
    { Source = source; Accounts = accounts.AsReadOnly(); AllAccounts = all; MaximumEntries = maximum; ContinuationToken = continuation; }
    public FinTsSegment Source { get; }
    public ReadOnlyCollection<FinTsReadAccount> Accounts { get; }
    public bool AllAccounts { get; }
    public int? MaximumEntries { get; }
    public string? ContinuationToken { get; }

    public static FinTsReadRequest Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Reference is not null || source.Code is not ("HKSPA" or "HKSAL")) { throw ReadDataFields.Invalid(); }
        if (source.Code == "HKSPA")
        {
            if (source.Version != 1) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion); }
            var accounts = ReadDataFields.Accounts(source, false, cancellationToken);
            return new(source, accounts, accounts.Count == 0, null, null);
        }
        if (source.Version is not (6 or 7 or 8)) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedReadDataVersion); }
        if (source.Fields.Count is < 2 or > 4) { throw ReadDataFields.Invalid(); }
        var account = ReadDataFields.Account(source.Fields[0], source.Version == 6 ? "ktv" : "kti");
        bool all = ReadDataFields.YesNo(ReadDataFields.Scalar(source.Fields[1], 1, true));
        string maximum = source.Fields.Count > 2 ? ReadDataFields.Scalar(source.Fields[2], 4, false) : "";
        int? entries = null;
        if (maximum.Length != 0)
        {
            if (maximum[0] == '0' || maximum.Any(c => c is < '0' or > '9')) { throw ReadDataFields.Invalid(); }
            entries = int.Parse(maximum, CultureInfo.InvariantCulture);
        }
        string? continuation = source.Fields.Count > 3 ? ReadDataFields.Scalar(source.Fields[3], 35, false) : null;
        return new(source, [account], all, entries, string.IsNullOrEmpty(continuation) ? null : continuation);
    }
}

public sealed class FinTsDiscoveryReport
{
    internal FinTsDiscoveryReport(FinTsSegment source, List<FinTsReadAccount> accounts) { Source = source; Accounts = accounts.AsReadOnly(); }
    public FinTsSegment Source { get; }
    public int RequestSegmentNumber => Source.Reference!.Value;
    public ReadOnlyCollection<FinTsReadAccount> Accounts { get; }
}

/// <summary>Separate bank-reported values. No derived available balance, freshness, completeness or authentication.</summary>
public sealed class FinTsBalanceReport
{
    internal FinTsBalanceReport(FinTsSegment source)
    {
        Source = source;
        var fields = source.Fields;
        if (fields.Count < 4 || fields.Count > (source.Version == 8 ? 12 : 11)) { throw ReadDataFields.Invalid(); }
        Account = ReadDataFields.Account(fields[0], source.Version == 6 ? "ktv" : "kti");
        ProductLabel = ReadDataFields.Scalar(fields[1], 30, true);
        AccountCurrency = ReadDataFields.Currency(ReadDataFields.Scalar(fields[2], 3, true));
        Booked = ReadDataFields.Balance(fields[3]);
        Pending = Optional(4, 5) is { } pending ? ReadDataFields.Balance(pending) : null;
        CreditLine = Amount(5); Available = Amount(6); AlreadyUsed = Amount(7); Overdraft = Amount(8);
        BookingTimestamp = Optional(9, 2) is { } booking ? ReadDataFields.Timestamp(booking.Elements, 0) : null;
        DueDate = Optional(10, 1) is { } due ? ReadDataFields.Date(ReadDataFields.Scalar(due, 8, true)) : null;
        GarnishableFromMonthChange = Amount(11);
        if (Overdraft is not null && (Available is null || Available.Value != 0)) { throw ReadDataFields.Invalid(); }
        HasCurrencyConflict = new[] { Booked.Amount, Pending?.Amount, CreditLine, Available, AlreadyUsed, Overdraft, GarnishableFromMonthChange }
            .Any(a => a is not null && a.Currency != AccountCurrency);
    }
    public FinTsSegment Source { get; }
    public int RequestSegmentNumber => Source.Reference!.Value;
    public FinTsReadAccount Account { get; }
    public string ProductLabel { get; }
    public string AccountCurrency { get; }
    public FinTsReadBalance Booked { get; }
    public FinTsReadBalance? Pending { get; }
    public FinTsReadAmount? CreditLine { get; }
    public FinTsReadAmount? Available { get; }
    public FinTsReadAmount? AlreadyUsed { get; }
    public FinTsReadAmount? Overdraft { get; }
    public FinTsReadTimestamp? BookingTimestamp { get; }
    public DateOnly? DueDate { get; }
    public FinTsReadAmount? GarnishableFromMonthChange { get; }
    public bool HasCurrencyConflict { get; }
    private FinTsReadAmount? Amount(int index) => Optional(index, 2) is { } field ? ReadDataFields.Amount(field.Elements, 0) : null;
    private FinTsField? Optional(int index, int maximum)
    {
        if (Source.Fields.Count <= index) { return null; }
        var field = Source.Fields[index];
        if (field.Elements.Count > maximum) { throw ReadDataFields.Invalid(); }
        return ParameterFields.Empty(field) ? null : field;
    }
}

public sealed class FinTsReadDataSet
{
    public const int MaximumReports = 128;
    public const int MaximumAccounts = 1024;
    private FinTsReadDataSet(FinTsResponse source, List<FinTsDiscoveryReport> discovery, List<FinTsBalanceReport> balances, List<FinTsSegment> unknown)
    { Source = source; Discovery = discovery.AsReadOnly(); Balances = balances.AsReadOnly(); UninterpretedSegments = unknown.AsReadOnly(); }
    public FinTsResponse Source { get; }
    public ReadOnlyCollection<FinTsDiscoveryReport> Discovery { get; }
    public ReadOnlyCollection<FinTsBalanceReport> Balances { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }

    public static FinTsReadDataSet Parse(FinTsResponse source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsDiscoveryReport> discovery = []; List<FinTsBalanceReport> balances = []; List<FinTsSegment> unknown = [];
        int reports = 0, accounts = 0;
        foreach (var segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code is not ("HISPA" or "HISAL")) { unknown.Add(segment); continue; }
            if (++reports > MaximumReports) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (segment.Code == "HISPA" && segment.Version != 1 || segment.Code == "HISAL" && segment.Version is not (6 or 7 or 8))
            { unknown.Add(segment); continue; }
            if (segment.Reference is null) { throw ReadDataFields.Invalid(); }
            if (segment.Code == "HISPA")
            {
                var found = ReadDataFields.Accounts(segment, true, cancellationToken);
                accounts += found.Count;
                discovery.Add(new(segment, found));
            }
            else { balances.Add(new(segment)); accounts++; }
            if (accounts > MaximumAccounts) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        }
        return new(source, discovery, balances, unknown);
    }
}

internal static class ReadDataFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidReadData);
    internal static string Text(FinTsDataElement element, int maximum, bool required)
    {
        if (element.IsBinary) { throw Invalid(); }
        string value = element.HeaderText();
        if (value.Length > maximum || required && value.Length == 0 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return value;
    }
    internal static string Scalar(FinTsField field, int maximum, bool required)
    {
        if (field.Elements.Count != 1) { throw Invalid(); }
        return Text(field.Elements[0], maximum, required);
    }
    internal static bool YesNo(string value) => value switch { "J" => true, "N" => false, _ => throw Invalid() };
    internal static List<FinTsReadAccount> Accounts(FinTsSegment segment, bool response, CancellationToken token)
    {
        if (segment.Fields.Count > 999) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        List<FinTsReadAccount> result = [];
        // An omitted optional repetition may be represented by trailing empty fields only.
        bool emptySeen = false;
        foreach (var field in segment.Fields)
        {
            token.ThrowIfCancellationRequested();
            if (field.Elements.Count > (response ? 7 : 4)) { throw Invalid(); }
            if (ParameterFields.Empty(field)) { emptySeen = true; continue; }
            if (emptySeen) { throw Invalid(); }
            result.Add(Account(field, response ? "ktz" : "ktv"));
        }
        return result;
    }
    internal static FinTsReadAccount Account(FinTsField field, string kind)
    {
        var e = field.Elements;
        int minimum = kind == "ktz" ? 6 : kind == "ktv" ? 3 : 2;
        int maximum = kind == "ktz" ? 7 : kind == "ktv" ? 4 : 6;
        if (e.Count < minimum || e.Count > maximum) { throw Invalid(); }
        string At(int i, int length, bool required = false) => i < e.Count ? Text(e[i], length, required) : required ? throw Invalid() : "";
        bool? sepa = kind == "ktz" ? YesNo(At(0, 1, true)) : null;
        int offset = kind == "ktz" ? 1 : 0;
        string iban = kind == "ktv" ? "" : At(offset, 34), bic = kind == "ktv" ? "" : At(offset + 1, 11);
        int national = kind == "ktv" ? 0 : offset + 2;
        string number = At(national, 30, kind != "kti"), subaccount = At(national + 1, 30);
        string country = At(national + 2, 3, kind != "kti"), institution = At(national + 3, 30);
        if (country.Length != 0 && (country.Length != 3 || country.Any(c => c is < '0' or > '9'))) { throw Invalid(); }
        if ((iban.Length == 0) != (bic.Length == 0)) { throw Invalid(); }
        if (kind == "ktz" && sepa != (iban.Length != 0)) { throw Invalid(); }
        bool hasNational = number.Length != 0 || subaccount.Length != 0 || country.Length != 0 || institution.Length != 0;
        if (hasNational && (number.Length == 0 || country.Length == 0) || !hasNational && iban.Length == 0) { throw Invalid(); }
        return new(field, sepa, iban, bic, number, subaccount, country, institution);
    }
    internal static string Currency(string value)
    {
        if (value.Length != 3 || value.Any(c => c is < 'A' or > 'Z')) { throw Invalid(); }
        return value;
    }
    internal static FinTsReadAmount Amount(IReadOnlyList<FinTsDataElement> elements, int start)
    {
        if (elements.Count < start + 2) { throw Invalid(); }
        string value = Text(elements[start], 15, true);
        int comma = value.IndexOf(',');
        if (comma < 1 || comma != value.LastIndexOf(',') || value.Where(c => c != ',').Any(c => c is < '0' or > '9') ||
            comma > 1 && value[0] == '0' || comma < value.Length - 1 && value[^1] == '0') { throw Invalid(); }
        decimal amount = decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new(amount, Currency(Text(elements[start + 1], 3, true)));
    }
    internal static DateOnly Date(string value)
    {
        if (value.Length != 8 || !DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { throw Invalid(); }
        return date;
    }
    internal static FinTsReadTimestamp Timestamp(IReadOnlyList<FinTsDataElement> elements, int start)
    {
        if (elements.Count < start + 1 || elements.Count > start + 2) { throw Invalid(); }
        var date = Date(Text(elements[start], 8, true));
        string value = elements.Count > start + 1 ? Text(elements[start + 1], 6, false) : "";
        TimeOnly? time = null;
        if (value.Length != 0)
        {
            if (value.Length != 6 || !TimeOnly.TryParseExact(value, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) { throw Invalid(); }
            time = parsed;
        }
        return new(date, time);
    }
    internal static FinTsReadBalance Balance(FinTsField field)
    {
        if (field.Elements.Count is < 4 or > 5) { throw Invalid(); }
        string indicator = Text(field.Elements[0], 1, true);
        if (indicator is not ("C" or "D")) { throw Invalid(); }
        return new(indicator, Amount(field.Elements, 1), Timestamp(field.Elements, 3));
    }
}
