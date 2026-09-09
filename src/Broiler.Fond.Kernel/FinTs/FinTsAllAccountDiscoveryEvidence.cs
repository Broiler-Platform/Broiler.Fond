using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsDiscoveryAccountIssue
{
    None = 0, UnknownAccount = 1, IdentityConflict = 2, AmbiguousUserAccount = 4,
    DuplicateReturnedIdentity = 8, InstitutionMismatch = 16, PermissionNeedsReview = 32, SignatureConflict = 64,
}

[Flags]
public enum FinTsAllDiscoveryIssue
{
    None = 0, UnsupportedRequest = 1, ParametersNeedReview = 2, ParameterDialogueMismatch = 4,
    AdvertisementNeedsReview = 8, AccountNeedsReview = 16, UnmatchedUserAccounts = 32,
}

/// <summary>One returned account and all national/IBAN candidates in the selected UPD. No winner is inferred from ambiguity.</summary>
public sealed class FinTsDiscoveryAccountEvidence
{
    internal FinTsDiscoveryAccountEvidence(FinTsReadAccount source, List<FinTsAccountParameters> candidates, FinTsDiscoveryAccountIssue issues, int? signatures)
    { Source = source; Candidates = candidates.AsReadOnly(); Issues = issues; MinimumCustomerSignatures = signatures; }
    public FinTsReadAccount Source { get; }
    public ReadOnlyCollection<FinTsAccountParameters> Candidates { get; }
    public FinTsDiscoveryAccountIssue Issues { get; }
    public FinTsAccountParameters? MatchedAccount => Issues == FinTsDiscoveryAccountIssue.None && Candidates.Count == 1 ? Candidates[0] : null;
    public int? MinimumCustomerSignatures { get; }
}

/// <summary>Pure bounded HKSPA-1 all-account comparison. No permission to send, identity allocation, relinking or removal.</summary>
public sealed class FinTsAllAccountDiscoveryEvidence
{
    public const int MaximumCandidateLinks = 4096;
    private FinTsAllAccountDiscoveryEvidence(FinTsReadRequestContext request, FinTsReadDataSet response, FinTsReadParameterSet parameters,
        FinTsAllDiscoveryIssue issues, FinTsReadContextIssue responseIssues, FinTsReadOutcomeObservation outcome,
        List<FinTsDiscoveryAccountEvidence> accounts, List<FinTsAccountParameters> unmatched)
    {
        Request = request; Response = response; Parameters = parameters; Issues = issues; ResponseIssues = responseIssues;
        Accounts = accounts.AsReadOnly(); UnmatchedUserAccounts = unmatched.AsReadOnly();
        Outcome = HasMatchingEvidence ? outcome : FinTsReadOutcomeObservation.NeedsReview;
    }
    public FinTsReadRequestContext Request { get; }
    public FinTsReadDataSet Response { get; }
    public FinTsReadParameterSet Parameters { get; }
    public FinTsAllDiscoveryIssue Issues { get; }
    public FinTsReadContextIssue ResponseIssues { get; }
    public FinTsReadOutcomeObservation Outcome { get; }
    public bool HasMatchingEvidence => Issues == FinTsAllDiscoveryIssue.None && ResponseIssues == FinTsReadContextIssue.None;
    public ReadOnlyCollection<FinTsDiscoveryAccountEvidence> Accounts { get; }
    /// <summary>UPD entries with no clean returned match. Absence never proves closure or revocation.</summary>
    public ReadOnlyCollection<FinTsAccountParameters> UnmatchedUserAccounts { get; }

    public static FinTsAllAccountDiscoveryEvidence Evaluate(FinTsReadRequestContext request, FinTsReadDataSet response,
        FinTsReadParameterSet parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = RequestIssues(request, parameters, out var advertisement);
        var source = parameters.Source;
        var responseIssues = FinTsReadContextEvidence.ResponseIssues(request, response, out bool unavailable, out _, out var outcome, cancellationToken);
        if (response.UninterpretedSegments.Count != 0 || response.Balances.Count != 0) { responseIssues |= FinTsReadContextIssue.UnexpectedReport; }
        if (response.Discovery.Count > 1) { responseIssues |= FinTsReadContextIssue.DuplicateReport; }
        if (response.Discovery.Count == 0 && !unavailable) { responseIssues |= FinTsReadContextIssue.MissingReport; }
        var returned = new List<FinTsReadAccount>();
        foreach (var report in response.Discovery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Source.Version != 1) { responseIssues |= FinTsReadContextIssue.VersionMismatch; }
            if (report.Accounts.Count == 0 && !unavailable) { responseIssues |= FinTsReadContextIssue.MissingReport; }
            returned.AddRange(report.Accounts);
        }
        var nationalIndex = new Dictionary<(string, string, string, string), List<FinTsAccountParameters>>();
        var ibanIndex = new Dictionary<string, List<FinTsAccountParameters>>(StringComparer.Ordinal);
        foreach (var account in source.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (account.HasAccountConnection) { Add(nationalIndex, National(account), account); }
            string iban = account.Iban.HeaderText();
            if (iban.Length != 0) { Add(ibanIndex, iban, account); }
        }
        var nationalCounts = new Dictionary<(string, string, string, string), int>();
        var ibanCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var account in returned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = National(account); nationalCounts[key] = nationalCounts.GetValueOrDefault(key) + 1;
            if (account.Iban.Length != 0) { ibanCounts[account.Iban] = ibanCounts.GetValueOrDefault(account.Iban) + 1; }
        }
        List<FinTsDiscoveryAccountEvidence> matches = [];
        HashSet<FinTsAccountParameters> matched = [];
        int totalCandidates = 0;
        foreach (var account in returned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rowIssues = FinTsDiscoveryAccountIssue.None;
            var key = National(account);
            HashSet<FinTsAccountParameters> candidates = [];
            if (nationalIndex.TryGetValue(key, out var nationalCandidates)) { candidates.UnionWith(nationalCandidates); }
            if (account.Iban.Length != 0 && ibanIndex.TryGetValue(account.Iban, out var ibanCandidates)) { candidates.UnionWith(ibanCandidates); }
            // Preserve UPD order even when candidates came from conflicting national and international keys.
            var ordered = source.Accounts.Where(candidates.Contains).ToList();
            totalCandidates += ordered.Count;
            if (totalCandidates > MaximumCandidateLinks) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (ordered.Count == 0) { rowIssues |= FinTsDiscoveryAccountIssue.UnknownAccount; }
            if (ordered.Count > 1) { rowIssues |= FinTsDiscoveryAccountIssue.AmbiguousUserAccount; }
            if (nationalCounts[key] > 1 || account.Iban.Length != 0 && ibanCounts[account.Iban] > 1)
            { rowIssues |= FinTsDiscoveryAccountIssue.DuplicateReturnedIdentity; }
            if (source.Bank is not null)
            {
                var institution = source.Bank.Source.Fields[1].Elements;
                if (account.Country != institution[0].HeaderText() || account.Institution != (institution.Count > 1 ? institution[1].HeaderText() : ""))
                { rowIssues |= FinTsDiscoveryAccountIssue.InstitutionMismatch; }
            }
            int? signatures = null;
            foreach (var candidate in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string iban = candidate.Iban.HeaderText();
                if (!candidate.HasAccountConnection || National(candidate) != key || iban.Length != 0 && iban != account.Iban)
                { rowIssues |= FinTsDiscoveryAccountIssue.IdentityConflict; }
            }
            if (ordered.Count == 1)
            {
                var candidate = ordered[0];
                if (source.GetOperationEvidence(candidate, "HKSPA") != FinTsOperationEvidence.Listed) { rowIssues |= FinTsDiscoveryAccountIssue.PermissionNeedsReview; }
                var permissions = candidate.Permissions.Where(p => p.Operation == "HKSPA").ToArray();
                if (permissions.Length == 1 && advertisement is not null)
                {
                    if (permissions[0].RequiredSignatures < advertisement.MinimumSignatures) { rowIssues |= FinTsDiscoveryAccountIssue.SignatureConflict; }
                    else { signatures = Math.Max(1, permissions[0].RequiredSignatures); }
                }
            }
            var entry = new FinTsDiscoveryAccountEvidence(account, ordered, rowIssues, signatures);
            matches.Add(entry);
            if (entry.MatchedAccount is { } clean) { matched.Add(clean); }
            if (rowIssues != FinTsDiscoveryAccountIssue.None) { issues |= FinTsAllDiscoveryIssue.AccountNeedsReview; }
        }
        var unmatched = source.Accounts.Where(a => !matched.Contains(a)).ToList();
        if (unmatched.Count != 0 && !unavailable) { issues |= FinTsAllDiscoveryIssue.UnmatchedUserAccounts; }
        return new(request, response, parameters, issues, responseIssues, outcome, matches, unmatched);
    }

    internal static FinTsAllDiscoveryIssue RequestIssues(FinTsReadRequestContext request, FinTsReadParameterSet parameters,
        out FinTsReadAdvertisement? advertisement)
    {
        var issues = FinTsAllDiscoveryIssue.None;
        var read = request.Request;
        if (read.Source.Code != "HKSPA" || read.Source.Version != 1 || !read.AllAccounts || read.Accounts.Count != 0)
        { issues |= FinTsAllDiscoveryIssue.UnsupportedRequest; }
        var source = parameters.Source;
        if (source.Bank is null || !source.Bank.ProtocolVersions.Contains(300) || source.User is null ||
            source.Source.HasErrors || source.Source.HasConflictingClasses || source.Source.ReplySegments.SelectMany(s => s.Replies)
                .Any(r => r.Meaning is not (FinTsReplyMeaning.ReceiptReported or FinTsReplyMeaning.ExecutionReported)))
        { issues |= FinTsAllDiscoveryIssue.ParametersNeedReview; }
        if (FinTsReadContextEvidence.Dialog(source.Source.Frame) != FinTsReadContextEvidence.Dialog(request.Frame) ||
            source.Source.Frame.MessageNumber >= request.ExpectedBankMessageNumber) { issues |= FinTsAllDiscoveryIssue.ParameterDialogueMismatch; }
        var advertisements = parameters.Advertisements.Where(a => a.Operation == FinTsReadOperation.SepaAccountDetails && a.Version == 1).ToArray();
        advertisement = advertisements.Length == 1 ? advertisements[0] : null;
        if (advertisement is null || advertisement.MaximumOrders == 0) { issues |= FinTsAllDiscoveryIssue.AdvertisementNeedsReview; }
        // SingleAccountRequestAllowed controls selected-account requests; an N does not forbid the empty all-account request.
        return issues;
    }

    private static (string, string, string, string) National(FinTsReadAccount account) => (account.Number, account.Subaccount, account.Country, account.Institution);
    private static (string, string, string, string) National(FinTsAccountParameters account)
    {
        var e = account.AccountConnection.Elements;
        return (e[0].HeaderText(), e[1].HeaderText(), e[2].HeaderText(), e.Count > 3 ? e[3].HeaderText() : "");
    }
    private static void Add<TKey>(Dictionary<TKey, List<FinTsAccountParameters>> index, TKey key, FinTsAccountParameters account) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var values)) { values = []; index.Add(key, values); }
        values.Add(account);
    }
}
