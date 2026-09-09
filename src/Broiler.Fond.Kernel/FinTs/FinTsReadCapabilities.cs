using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsReadOperation { Balance, SepaAccountDetails }

/// <summary>Untrusted HISALS/HISPAS advertisement, including version-specific options.</summary>
public sealed class FinTsReadAdvertisement
{
    internal FinTsReadAdvertisement(FinTsSegment source, FinTsReadOperation operation)
    {
        Source = source;
        Operation = operation;
        MaximumOrders = ParameterFields.Number(source.Fields[0], 3)!.Value;
        MinimumSignatures = ParameterFields.Number(source.Fields[1], 1)!.Value;
        SecurityClass = ParameterFields.Number(source.Fields[2], 1)!.Value;
        List<FinTsDataElement> formats = [];
        if (operation == FinTsReadOperation.Balance)
        {
            EntryCountInputAllowed = source.Version == 8 ? Flag(source.Fields[3].Elements[0]) : null;
        }
        else
        {
            var options = source.Fields[3].Elements;
            SingleAccountRequestAllowed = Flag(options[0]);
            NationalAccountConnectionAllowed = Flag(options[1]);
            StructuredRemittanceAllowed = Flag(options[2]);
            EntryCountInputAllowed = source.Version >= 2 ? Flag(options[3]) : null;
            ReservedRemittancePositions = source.Version == 3 ? ParameterFields.Number(options[4], 2) : null;
            formats.AddRange(options.Skip(source.Version + 2));
        }
        SepaFormats = formats.AsReadOnly();
    }
    public FinTsSegment Source { get; }
    public FinTsReadOperation Operation { get; }
    public int Version => Source.Version;
    public int MaximumOrders { get; }
    public int MinimumSignatures { get; }
    /// <summary>Raw advertised class; do not apply RDH-specific class rules to PIN/TAN.</summary>
    public int SecurityClass { get; }
    public bool? SingleAccountRequestAllowed { get; }
    public bool? NationalAccountConnectionAllowed { get; }
    public bool? StructuredRemittanceAllowed { get; }
    public bool? EntryCountInputAllowed { get; }
    public int? ReservedRemittancePositions { get; }
    public ReadOnlyCollection<FinTsDataElement> SepaFormats { get; }

    internal static bool Flag(FinTsDataElement element)
    {
        ParameterFields.Text(element, 1, true);
        return element.HeaderText() switch { "J" => true, "N" => false, _ => throw ParameterFields.Invalid() };
    }
}

public sealed class FinTsReadParameterSet
{
    public const int MaximumAdvertisements = 128;
    private FinTsReadParameterSet(FinTsParameterSet source, List<FinTsReadAdvertisement> advertisements, List<FinTsSegment> unknown)
    {
        Source = source;
        Advertisements = advertisements.AsReadOnly();
        UninterpretedSegments = unknown.AsReadOnly();
    }
    public FinTsParameterSet Source { get; }
    public ReadOnlyCollection<FinTsReadAdvertisement> Advertisements { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }

    public static FinTsReadParameterSet Parse(FinTsParameterSet source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsReadAdvertisement> advertisements = [];
        List<FinTsSegment> unknown = [];
        int count = 0;
        foreach (FinTsSegment segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code is not ("HISALS" or "HISPAS")) { unknown.Add(segment); continue; }
            if (++count > MaximumAdvertisements) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            FinTsReadOperation operation = segment.Code == "HISALS" ? FinTsReadOperation.Balance : FinTsReadOperation.SepaAccountDetails;
            if (!SupportsSchema(operation, segment.Version)) { unknown.Add(segment); continue; }
            int fields = operation == FinTsReadOperation.Balance && segment.Version != 8 ? 3 : 4;
            if (segment.Fields.Count != fields) { throw ParameterFields.Invalid(); }
            _ = ParameterFields.Number(segment.Fields[0], 3);
            if (ParameterFields.Number(segment.Fields[1], 1) > 3 || ParameterFields.Number(segment.Fields[2], 1) > 4) { throw ParameterFields.Invalid(); }
            if (fields == 4)
            {
                var options = segment.Fields[3].Elements;
                if (operation == FinTsReadOperation.Balance)
                {
                    if (options.Count != 1) { throw ParameterFields.Invalid(); }
                    _ = FinTsReadAdvertisement.Flag(options[0]);
                }
                else
                {
                    int prefix = segment.Version + 2;
                    if (options.Count < prefix || options.Count > prefix + 99) { throw ParameterFields.Invalid(); }
                    for (int index = 0; index < Math.Min(prefix, 4); index++) { _ = FinTsReadAdvertisement.Flag(options[index]); }
                    if (segment.Version == 3) { _ = ParameterFields.Number(options[4], 2); }
                    foreach (FinTsDataElement format in options.Skip(prefix)) { ParameterFields.Text(format, 256, false); }
                }
            }
            advertisements.Add(new(segment, operation));
        }
        return new(source, advertisements, unknown);
    }
    internal static bool SupportsSchema(FinTsReadOperation operation, int version) => operation switch
    {
        FinTsReadOperation.Balance => version is 6 or 7 or 8,
        FinTsReadOperation.SepaAccountDetails => version is 1 or 2 or 3,
        _ => false,
    };
}

[Flags]
public enum FinTsReadEvidenceIssue
{
    None = 0, MissingBankParameters = 1, Protocol300NotAdvertised = 2, MissingUserParameters = 4,
    MissingAccountConnection = 8, PermissionUnknown = 16, PermissionBlocked = 32,
    PermissionAmbiguous = 64, MissingAdvertisement = 128, UnsupportedVersion = 256,
    DuplicateAdvertisement = 512, SignatureConflict = 1024, ResponseNeedsReview = 2048,
    DuplicateAccountIdentity = 4096, SingleAccountRequestNotAdvertised = 8192,
    MissingInternationalIdentity = 16384, ZeroOrderCapacity = 32768,
    InstitutionContextMismatch = 65536,
}

/// <summary>Single-account, explicit-version evidence comparison; never a send authorization.</summary>
public sealed class FinTsReadCapabilityEvidence
{
    private FinTsReadCapabilityEvidence(FinTsReadParameterSet source, FinTsAccountParameters account, FinTsReadOperation operation,
        int version, FinTsReadEvidenceIssue issues, List<FinTsReadAdvertisement> candidates, int? signatures)
    {
        Source = source;
        Account = account;
        Operation = operation;
        Version = version;
        Issues = issues;
        Candidates = candidates.AsReadOnly();
        MinimumCustomerSignatures = signatures;
    }
    public FinTsReadParameterSet Source { get; }
    public FinTsAccountParameters Account { get; }
    public FinTsReadOperation Operation { get; }
    public int Version { get; }
    public FinTsReadEvidenceIssue Issues { get; }
    public ReadOnlyCollection<FinTsReadAdvertisement> Candidates { get; }
    /// <summary>Reported customer-signature lower bound; no signing/SCA procedure has been verified.</summary>
    public int? MinimumCustomerSignatures { get; }
    public bool HasMatchingEvidence => Issues == FinTsReadEvidenceIssue.None;

    public static FinTsReadCapabilityEvidence Evaluate(FinTsReadParameterSet source, FinTsAccountParameters account,
        FinTsReadOperation operation, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(operation) || version is < 1 or > 999 || !source.Source.Accounts.Contains(account)) { throw ParameterFields.Invalid(); }
        var parameters = source.Source;
        var issues = FinTsReadEvidenceIssue.None;
        if (parameters.Bank is null) { issues |= FinTsReadEvidenceIssue.MissingBankParameters; }
        else if (!parameters.Bank.ProtocolVersions.Contains(300)) { issues |= FinTsReadEvidenceIssue.Protocol300NotAdvertised; }
        if (parameters.User is null) { issues |= FinTsReadEvidenceIssue.MissingUserParameters; }
        if (!account.HasAccountConnection) { issues |= FinTsReadEvidenceIssue.MissingAccountConnection; }
        if (parameters.Bank is not null && account.HasAccountConnection)
        {
            var bankIdentity = parameters.Bank.Source.Fields[1].Elements;
            var accountInstitution = account.AccountConnection.Elements.Skip(2).ToArray();
            if (bankIdentity.Count != accountInstitution.Length || !bankIdentity.Zip(accountInstitution).All(pair =>
                pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes())))
            {
                issues |= FinTsReadEvidenceIssue.InstitutionContextMismatch;
            }
        }
        string code = operation == FinTsReadOperation.Balance ? "HKSAL" : "HKSPA";
        issues |= parameters.GetOperationEvidence(account, code) switch
        {
            FinTsOperationEvidence.Unknown => FinTsReadEvidenceIssue.PermissionUnknown,
            FinTsOperationEvidence.UnlistedBlocked => FinTsReadEvidenceIssue.PermissionBlocked,
            FinTsOperationEvidence.Ambiguous => FinTsReadEvidenceIssue.PermissionAmbiguous,
            _ => FinTsReadEvidenceIssue.None,
        };
        if (!FinTsReadParameterSet.SupportsSchema(operation, version)) { issues |= FinTsReadEvidenceIssue.UnsupportedVersion; }
        var candidates = source.Advertisements.Where(a => a.Operation == operation && a.Version == version).ToList();
        if (candidates.Count == 0) { issues |= FinTsReadEvidenceIssue.MissingAdvertisement; }
        if (candidates.Count > 1) { issues |= FinTsReadEvidenceIssue.DuplicateAdvertisement; }
        if (parameters.Source.HasErrors || parameters.Source.HasConflictingClasses ||
            parameters.Source.ReplySegments.SelectMany(s => s.Replies).Any(r => r.Meaning is not (FinTsReplyMeaning.ReceiptReported or FinTsReplyMeaning.ExecutionReported)))
        {
            issues |= FinTsReadEvidenceIssue.ResponseNeedsReview;
        }
        foreach (var other in parameters.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(other, account)) { continue; }
            bool sameIban = !account.Iban.IsEmpty && account.Iban.CopyValueBytes().AsSpan().SequenceEqual(other.Iban.CopyValueBytes());
            bool sameConnection = account.HasAccountConnection && other.HasAccountConnection &&
                account.AccountConnection.Elements.Count == other.AccountConnection.Elements.Count &&
                account.AccountConnection.Elements.Zip(other.AccountConnection.Elements).All(pair => pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes()));
            if (sameIban || sameConnection) { issues |= FinTsReadEvidenceIssue.DuplicateAccountIdentity; }
        }
        int? signatures = null;
        var permissions = account.Permissions.Where(p => p.Operation == code).ToArray();
        if (candidates.Count == 1)
        {
            var advertisement = candidates[0];
            if (advertisement.MaximumOrders == 0) { issues |= FinTsReadEvidenceIssue.ZeroOrderCapacity; }
            if (operation == FinTsReadOperation.SepaAccountDetails && advertisement.SingleAccountRequestAllowed != true)
            {
                issues |= FinTsReadEvidenceIssue.SingleAccountRequestNotAdvertised;
            }
            if (permissions.Length == 1)
            {
                if (permissions[0].RequiredSignatures < advertisement.MinimumSignatures) { issues |= FinTsReadEvidenceIssue.SignatureConflict; }
                else { signatures = Math.Max(1, permissions[0].RequiredSignatures); }
            }
        }
        if (operation == FinTsReadOperation.Balance && version is 7 or 8 && account.Iban.IsEmpty) { issues |= FinTsReadEvidenceIssue.MissingInternationalIdentity; }
        return new(source, account, operation, version, issues, candidates, signatures);
    }
}
