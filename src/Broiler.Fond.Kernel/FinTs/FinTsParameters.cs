using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsUnlistedOperationPolicy { Blocked = 0, Unknown = 1 }
public enum FinTsOperationEvidence { Unknown, Listed, UnlistedBlocked, Ambiguous }

/// <summary>Untrusted HIBPA#3 evidence. Zero/absent values remain distinct.</summary>
public sealed class FinTsBankParameters
{
    internal FinTsBankParameters(FinTsSegment source)
    {
        Source = source;
        Version = ParameterFields.Number(source.Fields[0], 3)!.Value;
        MaximumOperationTypes = ParameterFields.Number(source.Fields[3], 3)!.Value;
        Languages = source.Fields[4].Elements.Select(e => ParameterFields.Number(e, 3)).ToList().AsReadOnly();
        ProtocolVersions = source.Fields[5].Elements.Select(e => ParameterFields.Number(e, 3)).ToList().AsReadOnly();
        MaximumMessageKiB = ParameterFields.OptionalNumber(source, 6, 4);
        MinimumTimeoutSeconds = ParameterFields.OptionalNumber(source, 7, 4);
        MaximumTimeoutSeconds = ParameterFields.OptionalNumber(source, 8, 4);
    }
    public FinTsSegment Source { get; }
    public int Version { get; }
    public int MaximumOperationTypes { get; }
    public ReadOnlyCollection<int> Languages { get; }
    public ReadOnlyCollection<int> ProtocolVersions { get; }
    public int? MaximumMessageKiB { get; }
    public int? MinimumTimeoutSeconds { get; }
    public int? MaximumTimeoutSeconds { get; }
}

public sealed class FinTsUserParameters
{
    internal FinTsUserParameters(FinTsSegment source)
    {
        Source = source;
        Version = ParameterFields.Number(source.Fields[1], 3)!.Value;
        UnlistedOperations = (FinTsUnlistedOperationPolicy)ParameterFields.Number(source.Fields[2], 1)!.Value;
    }
    public FinTsSegment Source { get; }
    public FinTsDataElement UserId => Source.Fields[0].Elements[0];
    public int Version { get; }
    public FinTsUnlistedOperationPolicy UnlistedOperations { get; }
    /// <summary>Version zero evidence must not become reusable across dialogues.</summary>
    public bool IsDialogueScoped => Version == 0;
}

public sealed class FinTsOperationPermission
{
    internal FinTsOperationPermission(FinTsField source)
    {
        Source = source;
        Operation = source.Elements[0].HeaderText();
        RequiredSignatures = ParameterFields.Number(source.Elements[1], 2);
    }
    public FinTsField Source { get; }
    public string Operation { get; }
    public int RequiredSignatures { get; }
    public bool HasLimit => Source.Elements.Count > 2 && !Source.Elements[2].IsEmpty;
}

public sealed class FinTsAccountParameters
{
    internal FinTsAccountParameters(FinTsSegment source, List<FinTsOperationPermission> permissions)
    {
        Source = source;
        Permissions = permissions.AsReadOnly();
        AccountType = ParameterFields.OptionalNumber(source, 3, 2);
    }
    public FinTsSegment Source { get; }
    public FinTsField AccountConnection => Source.Fields[0];
    public bool HasAccountConnection => !ParameterFields.Empty(AccountConnection);
    public FinTsDataElement Iban => Source.Fields[1].Elements[0];
    public FinTsDataElement CustomerId => Source.Fields[2].Elements[0];
    public int? AccountType { get; }
    public FinTsDataElement? Currency => Source.Fields.Count > 4 ? Source.Fields[4].Elements[0] : null;
    public ReadOnlyCollection<FinTsOperationPermission> Permissions { get; }
    public bool HasAccountLimit => Source.Fields.Count > 8 && !ParameterFields.Empty(Source.Fields[8]);
    public FinTsDataElement? Extension => Source.Fields.Count == 1009 ? Source.Fields[1008].Elements[0] : null;
}

/// <summary>
/// Pure parameter evidence, never activated configuration or authorization.
/// No account allocation, cached-state replacement, endpoint editing or requests.
/// </summary>
public sealed class FinTsParameterSet
{
    public const int MaximumAccounts = 512;
    public const int MaximumPermissions = 8192;
    private FinTsParameterSet(FinTsResponse source, FinTsBankParameters? bank, FinTsUserParameters? user,
        List<FinTsAccountParameters> accounts, List<FinTsSegment> unparsed)
    {
        Source = source;
        Bank = bank;
        User = user;
        Accounts = accounts.AsReadOnly();
        UninterpretedSegments = unparsed.AsReadOnly();
    }
    public FinTsResponse Source { get; }
    public FinTsBankParameters? Bank { get; }
    public FinTsUserParameters? User { get; }
    public ReadOnlyCollection<FinTsAccountParameters> Accounts { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }

    public FinTsOperationEvidence GetOperationEvidence(FinTsAccountParameters account, string operation)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!ParameterFields.IsOperation(operation)) { throw ParameterFields.Invalid(); }
        if (!Accounts.Contains(account)) { throw ParameterFields.Invalid(); }
        int count = account.Permissions.Count(p => p.Operation == operation);
        if (count > 1) { return FinTsOperationEvidence.Ambiguous; }
        if (count == 1) { return FinTsOperationEvidence.Listed; }
        return User?.UnlistedOperations == FinTsUnlistedOperationPolicy.Blocked ? FinTsOperationEvidence.UnlistedBlocked : FinTsOperationEvidence.Unknown;
    }

    public static FinTsParameterSet Parse(FinTsResponse source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        FinTsBankParameters? bank = null;
        FinTsUserParameters? user = null;
        List<FinTsAccountParameters> accounts = [];
        List<FinTsSegment> unparsed = [];
        int permissionCount = 0;
        foreach (FinTsSegment segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (segment.Code)
            {
                case "HIBPA":
                    ParameterFields.Version(segment, 3);
                    if (bank is not null) { throw ParameterFields.Invalid(); }
                    ParseBank(segment);
                    bank = new(segment);
                    break;
                case "HIUPA":
                    ParameterFields.Version(segment, 4);
                    if (user is not null) { throw ParameterFields.Invalid(); }
                    ParseUser(segment);
                    user = new(segment);
                    break;
                case "HIUPD":
                    ParameterFields.Version(segment, 6);
                    if (accounts.Count == MaximumAccounts) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
                    accounts.Add(ParseAccount(segment, ref permissionCount));
                    break;
                default:
                    unparsed.Add(segment);
                    break;
            }
        }
        return new(source, bank, user, accounts, unparsed);
    }

    private static void ParseBank(FinTsSegment segment)
    {
        var fields = segment.Fields;
        if (fields.Count is < 6 or > 9) { throw ParameterFields.Invalid(); }
        _ = ParameterFields.Number(fields[0], 3);
        ParameterFields.Institution(fields[1].Elements);
        ParameterFields.Text(fields[2], 60, true);
        _ = ParameterFields.Number(fields[3], 3);
        foreach (int index in new[] { 4, 5 })
        {
            if (fields[index].Elements.Count is < 1 or > 9) { throw ParameterFields.Invalid(); }
            foreach (FinTsDataElement item in fields[index].Elements)
            {
                int value = ParameterFields.Number(item, 3);
                if (index == 4 && value is not (1 or 2 or 3)) { throw ParameterFields.Invalid(); }
            }
        }
        for (int index = 6; index < fields.Count; index++) { _ = ParameterFields.Number(fields[index], 4, false); }
    }

    private static void ParseUser(FinTsSegment segment)
    {
        var fields = segment.Fields;
        if (fields.Count is < 3 or > 5) { throw ParameterFields.Invalid(); }
        ParameterFields.Text(fields[0], 30, true);
        _ = ParameterFields.Number(fields[1], 3);
        if (ParameterFields.Number(fields[2], 1) is not (0 or 1)) { throw ParameterFields.Invalid(); }
        if (fields.Count > 3) { ParameterFields.Text(fields[3], 35, false); }
        if (fields.Count > 4) { ParameterFields.Text(fields[4], 2048, false); }
    }

    private static FinTsAccountParameters ParseAccount(FinTsSegment segment, ref int totalPermissions)
    {
        var fields = segment.Fields;
        if (fields.Count is < 3 or > 1009) { throw ParameterFields.Invalid(); }
        if (fields[0].Elements.Count > 4 || fields.Count > 8 && fields[8].Elements.Count > 4) { throw ParameterFields.Invalid(); }
        bool accountBound = !ParameterFields.Empty(fields[0]);
        if (accountBound)
        {
            if (fields.Count < 6) { throw ParameterFields.Invalid(); }
            var connection = fields[0].Elements;
            if (connection.Count is < 3 or > 4) { throw ParameterFields.Invalid(); }
            ParameterFields.Text(connection[0], 30, true);
            ParameterFields.Text(connection[1], 30, false);
            ParameterFields.Institution(connection.Skip(2).ToArray());
        }
        ParameterFields.Text(fields[1], 34, false); // Never truncate an overlong IBAN into a different source identity.
        ParameterFields.Text(fields[2], 30, true);
        if (fields.Count > 3) { _ = ParameterFields.Number(fields[3], 2, false); }
        if (fields.Count > 4) { ParameterFields.Currency(fields[4].Elements, false); }
        if (fields.Count > 5) { ParameterFields.Text(fields[5], 35, accountBound); } // Formals E.3 admits 35 despite the 27-byte table.
        if (fields.Count > 6) { ParameterFields.Text(fields[6], 35, false); }
        if (fields.Count > 7) { ParameterFields.Text(fields[7], 30, false); }
        bool accountLimit = fields.Count > 8 && !ParameterFields.Empty(fields[8]);
        if (accountLimit) { ParameterFields.Limit(fields[8].Elements); }
        if (!accountBound && fields.Take(9).Where((_, index) => index != 0 && index != 2).Any(f => !ParameterFields.Empty(f)))
        {
            throw ParameterFields.Invalid();
        }
        List<FinTsOperationPermission> permissions = [];
        for (int index = 9; index < Math.Min(fields.Count, 1008); index++)
        {
            FinTsField field = fields[index];
            if (field.Elements.Count > 6) { throw ParameterFields.Invalid(); }
            if (ParameterFields.Empty(field)) { continue; }
            var elements = field.Elements;
            // Repeated optional permissions precede the extension. Do not guess that a short scalar is JSON.
            if (elements.Count == 1) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedParameterLayout); }
            if (elements.Count is < 2 or > 6) { throw ParameterFields.Invalid(); }
            ParameterFields.Text(elements[0], 6, true);
            if (!ParameterFields.IsOperation(elements[0].HeaderText()) || ParameterFields.Number(elements[1], 2) is < 0 or > 3) { throw ParameterFields.Invalid(); }
            if (elements.Count > 2)
            {
                var limit = elements.Skip(2).ToArray();
                if (limit.Any(e => !e.IsEmpty))
                {
                    if (accountLimit) { throw ParameterFields.Invalid(); }
                    ParameterFields.Limit(limit);
                }
                else { foreach (var item in limit) { ParameterFields.Text(item, 1, false); } }
            }
            if (totalPermissions == MaximumPermissions) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            totalPermissions++;
            permissions.Add(new(field));
        }
        if (fields.Count == 1009) { ParameterFields.Text(fields[1008], 2048, false); }
        return new(segment, permissions);
    }
}

internal static class ParameterFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidParameters);
    internal static void Version(FinTsSegment segment, int expected)
    {
        if (segment.Version != expected) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedParameterVersion); }
    }
    internal static bool Empty(FinTsField field) => field.Elements.All(e => !e.IsBinary && e.IsEmpty);
    internal static bool IsOperation(string? value) => value is { Length: >= 1 and <= 6 } && value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');
    internal static void Text(FinTsField field, int maximum, bool required)
    {
        if (field.Elements.Count != 1) { throw Invalid(); }
        Text(field.Elements[0], maximum, required);
    }
    internal static void Text(FinTsDataElement element, int maximum, bool required)
    {
        if (element.IsBinary) { throw Invalid(); }
        byte[] bytes = element.CopyValueBytes();
        if (bytes.Length > maximum || required && bytes.Length == 0 || bytes.Any(b => b < 32 || b is >= 127 and <= 160)) { throw Invalid(); }
    }
    internal static int Number(FinTsDataElement element, int digits)
    {
        Text(element, digits, true);
        string text = element.HeaderText();
        if (text.Any(c => c is < '0' or > '9') || text.Length > 1 && text[0] == '0') { throw Invalid(); }
        return int.Parse(text, CultureInfo.InvariantCulture);
    }
    internal static int? Number(FinTsField field, int digits, bool required = true)
    {
        Text(field, digits, required);
        return field.Elements[0].IsEmpty ? null : Number(field.Elements[0], digits);
    }
    internal static int? OptionalNumber(FinTsSegment segment, int index, int digits) => segment.Fields.Count <= index ? null : Number(segment.Fields[index], digits, false);
    internal static void Institution(IReadOnlyList<FinTsDataElement> elements)
    {
        if (elements.Count is < 1 or > 2) { throw Invalid(); }
        Text(elements[0], 3, true);
        string country = elements[0].HeaderText();
        if (country.Length != 3 || country.Any(c => c is < '0' or > '9')) { throw Invalid(); }
        if (elements.Count == 2) { Text(elements[1], 30, false); }
        // Country-specific institution-code requirements are intentionally not inferred here.
    }
    internal static void Currency(IReadOnlyList<FinTsDataElement> elements, bool required)
    {
        if (elements.Count != 1) { throw Invalid(); }
        Text(elements[0], 3, required);
        string value = elements[0].HeaderText();
        if (value.Length != 0 && (value.Length != 3 || value.Any(c => c is < 'A' or > 'Z'))) { throw Invalid(); }
    }
    internal static void Limit(IReadOnlyList<FinTsDataElement> elements)
    {
        if (elements.Count is < 1 or > 4) { throw Invalid(); }
        Text(elements[0], 1, true);
        string kind = elements[0].HeaderText();
        if (kind is not ("E" or "T" or "W" or "M" or "Z")) { throw Invalid(); }
        for (int index = 1; index < elements.Count; index++) { Text(elements[index], index == 1 ? 15 : 3, false); }
        bool amount = elements.Count > 1 && !elements[1].IsEmpty;
        bool currency = elements.Count > 2 && !elements[2].IsEmpty;
        bool days = elements.Count > 3 && !elements[3].IsEmpty;
        if (kind == "Z")
        {
            if (amount || currency || days && Number(elements[3], 3) == 0) { throw Invalid(); }
        }
        else
        {
            if (days || amount != currency) { throw Invalid(); }
            if (amount)
            {
                string value = elements[1].HeaderText();
                int comma = value.IndexOf(',');
                if (comma < 1 || comma != value.LastIndexOf(',') || value.Where(c => c != ',').Any(c => c is < '0' or > '9') ||
                    comma > 1 && value[0] == '0' || comma < value.Length - 1 && value[^1] == '0') { throw Invalid(); }
                Currency([elements[2]], true);
            }
        }
    }
}
