using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>One restricted HNSHK-4 PIN/TAN header observation. Contains no PIN/TAN fields and authenticates no message.
/// Identifiers, reference numbers, timestamps and algorithm fillers remain untrusted caller-owned evidence.</summary>
public sealed class FinTsPinTanSignatureHeader
{
    private FinTsPinTanSignatureHeader(FinTsSegment source, int profile, string function, string control,
        int role, int party, string system, ulong reference, DateOnly? date, TimeOnly? time,
        string hash, string signature, string mode, string country, string institution, string user, int keyNumber, int keyVersion)
    {
        Source = source; ProfileVersion = profile; SecurityFunction = function; ControlReference = control;
        SecuritySupplierRole = role; SecurityParty = party; SystemId = system; SecurityReferenceNumber = reference;
        SecurityDate = date; SecurityTime = time; HashAlgorithmCode = hash; SignatureAlgorithmCode = signature;
        OperationModeCode = mode; CountryCode = country; InstitutionId = institution; UserId = user;
        KeyNumber = keyNumber; KeyVersion = keyVersion;
    }

    public FinTsSegment Source { get; }
    public int ProfileVersion { get; }
    public string SecurityFunction { get; }
    public string ControlReference { get; }
    public int SecuritySupplierRole { get; }
    public int SecurityParty { get; }
    /// <summary>Zero can be observed during system-ID synchronization; parsing does not qualify that request context.</summary>
    public string SystemId { get; }
    /// <summary>Exact schema value only. PIN/TAN does not use signature IDs for duplicate-submission protection.</summary>
    public ulong SecurityReferenceNumber { get; }
    public DateOnly? SecurityDate { get; }
    public TimeOnly? SecurityTime { get; }
    public string HashAlgorithmCode { get; }
    /// <summary>PIN/TAN filler, never an algorithm selection or cryptographic claim.</summary>
    public string SignatureAlgorithmCode { get; }
    /// <summary>PIN/TAN filler, preserved exactly rather than interpreted as an operation mode.</summary>
    public string OperationModeCode { get; }
    public string CountryCode { get; }
    public string InstitutionId { get; }
    public string UserId { get; }
    public int KeyNumber { get; }
    public int KeyVersion { get; }

    public static FinTsPinTanSignatureHeader Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Code != "HNSHK" || source.Reference is not null) { throw Invalid(); }
        if (source.Version != 4) { throw Unsupported(); }
        var fields = source.Fields;
        if (fields.Count is < 11 or > 12) { throw Invalid(); }
        var profile = Group(fields[0], 2, 2);
        if (Text(profile[0], 3) != "PIN") { throw Unsupported(); }
        string version = Text(profile[1], 3);
        if (version is not ("1" or "2")) { throw Unsupported(); }
        string function = Scalar(fields[1], 3);
        if (version == "1" ? function != "999" : function.Length != 3 || string.CompareOrdinal(function, "900") < 0 || string.CompareOrdinal(function, "997") > 0 || function.Any(c => c is < '0' or > '9')) { throw Invalid(); }
        string control = Scalar(fields[2], 14);
        if (control == "0" || Scalar(fields[3], 3) != "1") { throw Invalid(); }
        string role = Scalar(fields[4], 3);
        if (role is not ("1" or "3" or "4")) { throw Invalid(); }
        var identity = Group(fields[5], 3, 3);
        string party = Text(identity[0], 3);
        if (party is not ("1" or "2") || !Empty(identity[1])) { throw Invalid(); }
        string system = Text(identity[2], 30);
        ulong reference = Number(Group(fields[6], 1, 1)[0], 16);
        var timestamp = Group(fields[7], 1, 3);
        if (Text(timestamp[0], 3) != "1") { throw Invalid(); }
        string dateText = timestamp.Count > 1 ? Text(timestamp[1], 8, false) : "";
        string timeText = timestamp.Count > 2 ? Text(timestamp[2], 6, false) : "";
        DateOnly? date = null; TimeOnly? time = null;
        if (dateText.Length != 0)
        {
            if (dateText.Length != 8 || !DateOnly.TryParseExact(dateText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) { throw Invalid(); }
            date = parsed;
        }
        if (timeText.Length != 0)
        {
            if (date is null || timeText.Length != 6 || !TimeOnly.TryParseExact(timeText, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) { throw Invalid(); }
            time = parsed;
        }
        var hash = Group(fields[8], 3, 4);
        string hashCode = Text(hash[1], 3);
        // 999 is the PIN/TAN example's placeholder. Current HBCI dictionary codes are observations only here.
        if (Text(hash[0], 3) != "1" || Text(hash[2], 3) != "1" || hash.Count == 4 && !Empty(hash[3])) { throw Invalid(); }
        if (hashCode is not ("3" or "4" or "5" or "6" or "999")) { throw Unsupported(); }
        var signature = Group(fields[9], 3, 3);
        if (Text(signature[0], 3) != "6") { throw Invalid(); }
        string signatureCode = Filler(signature[1]); string mode = Filler(signature[2]);
        var key = Group(fields[10], 6, 6);
        string country = Text(key[0], 3);
        if (country.Length != 3 || country.Any(c => c is < '0' or > '9')) { throw Invalid(); }
        string institution = Text(key[1], 30, false); string user = Text(key[2], 30);
        if (Text(key[3], 1) != "S") { throw Unsupported(); }
        int keyNumber = (int)Number(key[4], 3); int keyVersion = (int)Number(key[5], 3);
        if (fields.Count == 12 && (fields[11].Elements.Count != 1 || !Empty(fields[11].Elements[0]))) { throw Invalid(); }
        cancellationToken.ThrowIfCancellationRequested();
        return new(source, version == "1" ? 1 : 2, function, control, role[0] - '0', party[0] - '0', system,
            reference, date, time, hashCode, signatureCode, mode, country, institution, user, keyNumber, keyVersion);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<FinTsDataElement> Group(FinTsField field, int minimum, int maximum)
    {
        if (field.Elements.Count < minimum || field.Elements.Count > maximum) { throw Invalid(); }
        return field.Elements;
    }
    private static string Scalar(FinTsField field, int maximum) => Text(Group(field, 1, 1)[0], maximum);
    private static bool Empty(FinTsDataElement element) => !element.IsBinary && element.IsEmpty;
    private static string Text(FinTsDataElement element, int maximum, bool required = true)
    {
        if (element.IsBinary) { throw Invalid(); }
        string value = element.HeaderText();
        if (value.Length > maximum || required && value.Length == 0 || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160) ||
            value.Length > 0 && (value[0] == ' ' || value[^1] == ' ')) { throw Invalid(); }
        return value;
    }
    private static ulong Number(FinTsDataElement element, int maximum)
    {
        string value = Text(element, maximum);
        if (value.Any(c => c is < '0' or > '9') || value.Length > 1 && value[0] == '0') { throw Invalid(); }
        return ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }
    private static string Filler(FinTsDataElement element)
    {
        string value = Text(element, 3);
        if (value.Any(c => c is < '0' or > '9')) { throw Invalid(); }
        return value;
    }
    private static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidSignatureHeader);
    private static FinTsFormatException Unsupported() => new(FinTsSyntaxError.UnsupportedSignatureHeader);
}
