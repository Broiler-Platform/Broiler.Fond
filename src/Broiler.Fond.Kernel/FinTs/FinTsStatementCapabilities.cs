using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Identifies the statement wire protocol family.
/// </summary>
public enum FinTsStatementOperation
{
    Mt940 = 1,
    Camt = 2,
}

/// <summary>
/// Untrusted HIKAZS / HICAZS advertisement, including version-specific options and format descriptors.
/// </summary>
public sealed class FinTsStatementAdvertisement
{
    internal FinTsStatementAdvertisement(FinTsSegment source, FinTsStatementOperation operation)
    {
        Source = source;
        Operation = operation;
        MaximumOrders = ParameterFields.Number(source.Fields[0], 3)!.Value;
        MinimumSignatures = ParameterFields.Number(source.Fields[1], 1)!.Value;
        SecurityClass = ParameterFields.Number(source.Fields[2], 1)!.Value;

        var options = source.Fields[3].Elements;
        TimeRangeAllowed = FinTsReadAdvertisement.Flag(options[0]);

        List<string> formats = [];
        if (operation == FinTsStatementOperation.Mt940)
        {
            AllAccountsAllowed = source.Version >= 6 && options.Count > 1 ? FinTsReadAdvertisement.Flag(options[1]) : null;
            PaginationTokenAllowed = source.Version >= 6 && options.Count > 2 ? FinTsReadAdvertisement.Flag(options[2]) : null;

            if (source.Version >= 7 && options.Count > 3)
            {
                foreach (FinTsDataElement elem in options.Skip(3))
                {
                    if (!elem.IsEmpty)
                    {
                        formats.Add(elem.HeaderText());
                    }
                }
            }
        }
        else // Camt
        {
            AllAccountsAllowed = options.Count > 1 ? FinTsReadAdvertisement.Flag(options[1]) : null;
            PaginationTokenAllowed = options.Count > 2 ? FinTsReadAdvertisement.Flag(options[2]) : null;

            if (options.Count > 3)
            {
                foreach (FinTsDataElement elem in options.Skip(3))
                {
                    if (!elem.IsEmpty)
                    {
                        formats.Add(elem.HeaderText());
                    }
                }
            }
        }

        SupportedFormats = formats.AsReadOnly();
    }

    public FinTsSegment Source { get; }
    public FinTsStatementOperation Operation { get; }
    public int Version => Source.Version;
    public int MaximumOrders { get; }
    public int MinimumSignatures { get; }
    /// <summary>Raw advertised security class; do not apply RDH class rules to PIN/TAN.</summary>
    public int SecurityClass { get; }
    public bool TimeRangeAllowed { get; }
    public bool? AllAccountsAllowed { get; }
    public bool? PaginationTokenAllowed { get; }
    public ReadOnlyCollection<string> SupportedFormats { get; }
}

/// <summary>
/// Bounded HIKAZS (SWIFT MT940) and HICAZS (ISO 20022 CAMT) statement parameter collection.
/// </summary>
public sealed class FinTsStatementParameterSet
{
    public const int MaximumAdvertisements = 128;
    public const int MaximumCamtFormats = 99;

    private FinTsStatementParameterSet(
        FinTsParameterSet source,
        List<FinTsStatementAdvertisement> advertisements,
        List<FinTsSegment> unknown)
    {
        Source = source;
        Advertisements = advertisements.AsReadOnly();
        UninterpretedSegments = unknown.AsReadOnly();
    }

    public FinTsParameterSet Source { get; }
    public ReadOnlyCollection<FinTsStatementAdvertisement> Advertisements { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }

    public static FinTsStatementParameterSet Parse(FinTsParameterSet source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        List<FinTsStatementAdvertisement> advertisements = [];
        List<FinTsSegment> unknown = [];
        int count = 0;

        foreach (FinTsSegment segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code is not ("HIKAZS" or "HICAZS"))
            {
                unknown.Add(segment);
                continue;
            }

            if (++count > MaximumAdvertisements)
            {
                throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded);
            }

            FinTsStatementOperation operation = segment.Code == "HIKAZS"
                ? FinTsStatementOperation.Mt940
                : FinTsStatementOperation.Camt;

            if (!SupportsSchema(operation, segment.Version))
            {
                unknown.Add(segment);
                continue;
            }

            if (segment.Fields.Count != 4)
            {
                throw ParameterFields.Invalid();
            }

            _ = ParameterFields.Number(segment.Fields[0], 3);
            if (ParameterFields.Number(segment.Fields[1], 1) > 3 || ParameterFields.Number(segment.Fields[2], 1) > 4)
            {
                throw ParameterFields.Invalid();
            }

            var options = segment.Fields[3].Elements;
            if (operation == FinTsStatementOperation.Mt940)
            {
                if (segment.Version == 5)
                {
                    if (options.Count != 1)
                    {
                        throw ParameterFields.Invalid();
                    }
                    _ = FinTsReadAdvertisement.Flag(options[0]);
                }
                else if (segment.Version == 6)
                {
                    if (options.Count != 3)
                    {
                        throw ParameterFields.Invalid();
                    }
                    _ = FinTsReadAdvertisement.Flag(options[0]);
                    _ = FinTsReadAdvertisement.Flag(options[1]);
                    _ = FinTsReadAdvertisement.Flag(options[2]);
                }
                else // v7
                {
                    if (options.Count < 3 || options.Count > 3 + MaximumCamtFormats)
                    {
                        throw ParameterFields.Invalid();
                    }
                    _ = FinTsReadAdvertisement.Flag(options[0]);
                    _ = FinTsReadAdvertisement.Flag(options[1]);
                    _ = FinTsReadAdvertisement.Flag(options[2]);
                    foreach (FinTsDataElement format in options.Skip(3))
                    {
                        ParameterFields.Text(format, 30, false);
                    }
                }
            }
            else // Camt (v1, v2)
            {
                if (options.Count < 3 || options.Count > 3 + MaximumCamtFormats)
                {
                    throw ParameterFields.Invalid();
                }
                _ = FinTsReadAdvertisement.Flag(options[0]);
                _ = FinTsReadAdvertisement.Flag(options[1]);
                _ = FinTsReadAdvertisement.Flag(options[2]);
                foreach (FinTsDataElement format in options.Skip(3))
                {
                    ParameterFields.Text(format, 256, false);
                }
            }

            advertisements.Add(new FinTsStatementAdvertisement(segment, operation));
        }

        return new FinTsStatementParameterSet(source, advertisements, unknown);
    }

    internal static bool SupportsSchema(FinTsStatementOperation operation, int version) => operation switch
    {
        FinTsStatementOperation.Mt940 => version is 5 or 6 or 7,
        FinTsStatementOperation.Camt => version is 1 or 2,
        _ => false,
    };
}

/// <summary>
/// Diagnostic issue flags for statement capability matching.
/// </summary>
[Flags]
public enum FinTsStatementEvidenceIssue
{
    None = 0,
    MissingBankParameters = 1,
    Protocol300NotAdvertised = 2,
    MissingUserParameters = 4,
    MissingAccountConnection = 8,
    PermissionUnknown = 16,
    PermissionBlocked = 32,
    PermissionAmbiguous = 64,
    MissingAdvertisement = 128,
    UnsupportedVersion = 256,
    DuplicateAdvertisement = 512,
    SignatureConflict = 1024,
    ResponseNeedsReview = 2048,
    DuplicateAccountIdentity = 4096,
    InstitutionContextMismatch = 8192,
    ZeroOrderCapacity = 16384,
    MissingInternationalIdentity = 32768,
    UnsupportedCamtFormat = 65536,
}

/// <summary>
/// Single-account, explicit-version statement capability comparison.
/// </summary>
public sealed class FinTsStatementCapabilityEvidence
{
    private FinTsStatementCapabilityEvidence(
        FinTsStatementParameterSet source,
        FinTsAccountParameters account,
        FinTsStatementOperation operation,
        int version,
        string? requestedFormat,
        FinTsStatementEvidenceIssue issues,
        List<FinTsStatementAdvertisement> candidates,
        int? signatures)
    {
        Source = source;
        Account = account;
        Operation = operation;
        Version = version;
        RequestedFormat = requestedFormat;
        Issues = issues;
        Candidates = candidates.AsReadOnly();
        MinimumCustomerSignatures = signatures;
    }

    public FinTsStatementParameterSet Source { get; }
    public FinTsAccountParameters Account { get; }
    public FinTsStatementOperation Operation { get; }
    public int Version { get; }
    public string? RequestedFormat { get; }
    public FinTsStatementEvidenceIssue Issues { get; }
    public ReadOnlyCollection<FinTsStatementAdvertisement> Candidates { get; }
    public int? MinimumCustomerSignatures { get; }
    public bool HasMatchingEvidence => Issues == FinTsStatementEvidenceIssue.None;

    public static FinTsStatementCapabilityEvidence Evaluate(
        FinTsStatementParameterSet source,
        FinTsAccountParameters account,
        FinTsStatementOperation operation,
        int version,
        string? requestedFormat = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Enum.IsDefined(operation) || version is < 1 or > 999 || !source.Source.Accounts.Contains(account))
        {
            throw ParameterFields.Invalid();
        }

        var parameters = source.Source;
        var issues = FinTsStatementEvidenceIssue.None;

        if (parameters.Bank is null)
        {
            issues |= FinTsStatementEvidenceIssue.MissingBankParameters;
        }
        else if (!parameters.Bank.ProtocolVersions.Contains(300))
        {
            issues |= FinTsStatementEvidenceIssue.Protocol300NotAdvertised;
        }

        if (parameters.User is null)
        {
            issues |= FinTsStatementEvidenceIssue.MissingUserParameters;
        }

        if (!account.HasAccountConnection)
        {
            issues |= FinTsStatementEvidenceIssue.MissingAccountConnection;
        }

        if (parameters.Bank is not null && account.HasAccountConnection)
        {
            var bankIdentity = parameters.Bank.Source.Fields[1].Elements;
            var accountInstitution = account.AccountConnection.Elements.Skip(2).ToArray();
            if (bankIdentity.Count != accountInstitution.Length || !bankIdentity.Zip(accountInstitution).All(pair =>
                pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes())))
            {
                issues |= FinTsStatementEvidenceIssue.InstitutionContextMismatch;
            }
        }

        string code = operation == FinTsStatementOperation.Mt940 ? "HKKAZ" : "HKCAZ";
        issues |= parameters.GetOperationEvidence(account, code) switch
        {
            FinTsOperationEvidence.Unknown => FinTsStatementEvidenceIssue.PermissionUnknown,
            FinTsOperationEvidence.UnlistedBlocked => FinTsStatementEvidenceIssue.PermissionBlocked,
            FinTsOperationEvidence.Ambiguous => FinTsStatementEvidenceIssue.PermissionAmbiguous,
            _ => FinTsStatementEvidenceIssue.None,
        };

        if (!FinTsStatementParameterSet.SupportsSchema(operation, version))
        {
            issues |= FinTsStatementEvidenceIssue.UnsupportedVersion;
        }

        var candidates = source.Advertisements.Where(a => a.Operation == operation && a.Version == version).ToList();
        if (candidates.Count == 0)
        {
            issues |= FinTsStatementEvidenceIssue.MissingAdvertisement;
        }

        if (candidates.Count > 1)
        {
            issues |= FinTsStatementEvidenceIssue.DuplicateAdvertisement;
        }

        if (parameters.Source.HasErrors || parameters.Source.HasConflictingClasses ||
            parameters.Source.ReplySegments.SelectMany(s => s.Replies).Any(r => r.Meaning is not (FinTsReplyMeaning.ReceiptReported or FinTsReplyMeaning.ExecutionReported)))
        {
            issues |= FinTsStatementEvidenceIssue.ResponseNeedsReview;
        }

        foreach (var other in parameters.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(other, account))
            {
                continue;
            }

            bool sameIban = !account.Iban.IsEmpty && account.Iban.CopyValueBytes().AsSpan().SequenceEqual(other.Iban.CopyValueBytes());
            bool sameConnection = account.HasAccountConnection && other.HasAccountConnection &&
                account.AccountConnection.Elements.Count == other.AccountConnection.Elements.Count &&
                account.AccountConnection.Elements.Zip(other.AccountConnection.Elements).All(pair => pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes()));
            if (sameIban || sameConnection)
            {
                issues |= FinTsStatementEvidenceIssue.DuplicateAccountIdentity;
            }
        }

        int? signatures = null;
        var permissions = account.Permissions.Where(p => p.Operation == code).ToArray();
        if (candidates.Count == 1)
        {
            var advertisement = candidates[0];
            if (advertisement.MaximumOrders == 0)
            {
                issues |= FinTsStatementEvidenceIssue.ZeroOrderCapacity;
            }

            if (permissions.Length == 1)
            {
                if (permissions[0].RequiredSignatures < advertisement.MinimumSignatures)
                {
                    issues |= FinTsStatementEvidenceIssue.SignatureConflict;
                }
                else
                {
                    signatures = Math.Max(1, permissions[0].RequiredSignatures);
                }
            }

            if (operation == FinTsStatementOperation.Camt && requestedFormat is not null)
            {
                bool formatSupported = advertisement.SupportedFormats.Any(f =>
                    string.Equals(f, requestedFormat, StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(":" + requestedFormat, StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith("/" + requestedFormat, StringComparison.OrdinalIgnoreCase));

                if (!formatSupported)
                {
                    issues |= FinTsStatementEvidenceIssue.UnsupportedCamtFormat;
                }
            }
        }

        if (operation == FinTsStatementOperation.Camt && account.Iban.IsEmpty)
        {
            issues |= FinTsStatementEvidenceIssue.MissingInternationalIdentity;
        }

        return new FinTsStatementCapabilityEvidence(
            source,
            account,
            operation,
            version,
            requestedFormat,
            issues,
            candidates,
            signatures);
    }
}

/// <summary>
/// Result of multi-format statement capability negotiation for an account.
/// </summary>
public sealed class FinTsStatementNegotiationResult
{
    internal FinTsStatementNegotiationResult(
        FinTsStatementCapabilityEvidence? selectedEvidence,
        FinTsStatementCapabilityEvidence? alternateEvidence)
    {
        SelectedEvidence = selectedEvidence;
        AlternateEvidence = alternateEvidence;
    }

    public FinTsStatementCapabilityEvidence? SelectedEvidence { get; }
    public FinTsStatementCapabilityEvidence? AlternateEvidence { get; }
    public bool HasMatchingEvidence => SelectedEvidence is { HasMatchingEvidence: true };
    public FinTsStatementOperation? SelectedOperation => SelectedEvidence?.Operation;
    public int? SelectedVersion => SelectedEvidence?.Version;
}

/// <summary>
/// Negotiates statement capabilities per account, preferring ISO 20022 CAMT over SWIFT MT940
/// when matching evidence exists, without guessing or unverified fallback.
/// </summary>
public static class FinTsStatementPreferenceNegotiator
{
    public const string DefaultCamtFormat = "camt.053.001.02";

    public static FinTsStatementNegotiationResult Negotiate(
        FinTsStatementParameterSet source,
        FinTsAccountParameters account,
        string? preferredCamtFormat = DefaultCamtFormat,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        // 1. Try CAMT candidates in descending version order (v2, v1)
        FinTsStatementCapabilityEvidence? bestCamt = null;
        int[] camtVersions = [2, 1];
        foreach (int v in camtVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = FinTsStatementCapabilityEvidence.Evaluate(source, account, FinTsStatementOperation.Camt, v, preferredCamtFormat, cancellationToken);
            if (evidence.HasMatchingEvidence)
            {
                bestCamt = evidence;
                break;
            }

            bestCamt ??= evidence;
        }

        // 2. Try MT940 candidates in descending version order (v7, v6, v5)
        FinTsStatementCapabilityEvidence? bestMt940 = null;
        int[] mt940Versions = [7, 6, 5];
        foreach (int v in mt940Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = FinTsStatementCapabilityEvidence.Evaluate(source, account, FinTsStatementOperation.Mt940, v, null, cancellationToken);
            if (evidence.HasMatchingEvidence)
            {
                bestMt940 = evidence;
                break;
            }

            bestMt940 ??= evidence;
        }

        // Preference rule: CAMT preferred over MT940 when matching evidence exists
        if (bestCamt is { HasMatchingEvidence: true })
        {
            return new FinTsStatementNegotiationResult(
                selectedEvidence: bestCamt,
                alternateEvidence: bestMt940 is { HasMatchingEvidence: true } ? bestMt940 : null);
        }

        if (bestMt940 is { HasMatchingEvidence: true })
        {
            return new FinTsStatementNegotiationResult(
                selectedEvidence: bestMt940,
                alternateEvidence: null);
        }

        // Neither matches cleanly; return best available candidate for diagnosis
        FinTsStatementCapabilityEvidence? fallback = bestCamt ?? bestMt940;
        return new FinTsStatementNegotiationResult(
            selectedEvidence: fallback,
            alternateEvidence: null);
    }
}
