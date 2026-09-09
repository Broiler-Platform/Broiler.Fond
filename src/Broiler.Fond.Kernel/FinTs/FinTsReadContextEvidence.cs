namespace Broiler.Fond.Kernel.FinTs;

/// <summary>One unsigned read segment in an established synthetic dialogue. No credentials or send authorization.</summary>
public sealed class FinTsReadRequestContext
{
    private FinTsReadRequestContext(FinTsMessageFrame frame, FinTsReadRequest request, int expectedBankMessageNumber)
    { Frame = frame; Request = request; ExpectedBankMessageNumber = expectedBankMessageNumber; }
    public FinTsMessageFrame Frame { get; }
    public FinTsReadRequest Request { get; }
    public int ExpectedBankMessageNumber { get; }

    public static FinTsReadRequestContext Parse(FinTsMessageFrame frame, int expectedBankMessageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedBankMessageNumber is < 1 or > 9999) { throw ReadDataFields.Invalid(); }
        var segments = frame.Syntax.Segments;
        if (segments.Any(s => s.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA")) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
        var header = segments[0].Fields;
        if (segments.Count != 3 || header.Count == 5 && !ParameterFields.Empty(header[4]) ||
            header[2].Elements[0].HeaderText() is "0" or "unbekannt") { throw ReadDataFields.Invalid(); }
        return new(frame, FinTsReadRequest.Parse(segments[1], cancellationToken), expectedBankMessageNumber);
    }
}

[Flags]
public enum FinTsReadContextIssue
{
    None = 0, UnsupportedAccountScope = 1, CapabilityNeedsReview = 2, CapabilityScopeMismatch = 4,
    ParameterDialogueMismatch = 8, RequestAccountMismatch = 16, EntryCountNotAdvertised = 32,
    NationalAccountNotAdvertised = 64, MessageMismatch = 128, ReferenceMismatch = 256,
    UnexpectedReport = 512, MissingReport = 1024, DuplicateReport = 2048, VersionMismatch = 4096,
    ResponseAccountMismatch = 8192, CurrencyMismatch = 16384, DueDateNeedsReview = 32768,
    ResponseNeedsReview = 65536, StatusNeedsReview = 131072, ContinuationNeedsReview = 262144,
    ContinuationScopeMismatch = 524288, PageLimitExceeded = 1048576, ProfileNeedsReview = 2097152,
    AvailabilityConflict = 4194304,
}

public enum FinTsReadOutcomeObservation { NeedsReview, ExecutionReported, PartialReported, UnavailableReported }

/// <summary>Pure single-account comparison; no authentication, page aggregation, replay consumption or domain update.</summary>
public sealed class FinTsReadContextEvidence
{
    public const int MaximumPages = 128;
    private FinTsReadContextEvidence(FinTsReadRequestContext request, FinTsReadDataSet response, FinTsReadCapabilityEvidence capability,
        FinTsReadAdvertisement? nationalAdvertisement, FinTsReadContextIssue issues, FinTsReadOutcomeObservation outcome, string? continuation, int pageNumber)
    {
        Request = request; Response = response; Capability = capability; NationalAdvertisement = nationalAdvertisement;
        Issues = issues; Outcome = issues == FinTsReadContextIssue.None ? outcome : FinTsReadOutcomeObservation.NeedsReview;
        ReportedContinuationToken = continuation; PageNumber = pageNumber;
    }
    public FinTsReadRequestContext Request { get; }
    public FinTsReadDataSet Response { get; }
    public FinTsReadCapabilityEvidence Capability { get; }
    public FinTsReadAdvertisement? NationalAdvertisement { get; }
    public FinTsReadContextIssue Issues { get; }
    public FinTsReadOutcomeObservation Outcome { get; }
    public bool HasMatchingEvidence => Issues == FinTsReadContextIssue.None;
    /// <summary>Raw reported token, even on a result needing review. Never a permission to send a continuation.</summary>
    public string? ReportedContinuationToken { get; }
    public int PageNumber { get; }

    public static FinTsReadContextEvidence Evaluate(FinTsReadRequestContext request, FinTsReadDataSet response,
        FinTsReadCapabilityEvidence capability, FinTsReadAdvertisement? nationalAdvertisement = null,
        FinTsReadContextEvidence? previousPage = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(response); ArgumentNullException.ThrowIfNull(capability);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = RequestIssues(request, capability, nationalAdvertisement);
        var read = request.Request;
        bool discovery = read.Source.Code == "HKSPA";
        var selected = read.Accounts.Count == 1 ? read.Accounts[0] : null;
        issues |= ResponseIssues(request, response, out bool unavailableReported, out string? continuation, out var outcome, cancellationToken);
        if (response.UninterpretedSegments.Count != 0 || discovery && response.Balances.Count != 0 || !discovery && response.Discovery.Count != 0)
        { issues |= FinTsReadContextIssue.UnexpectedReport; }
        int reports = discovery ? response.Discovery.Count : response.Balances.Count;
        if (reports == 0 && !unavailableReported) { issues |= FinTsReadContextIssue.MissingReport; }
        if (reports > 1) { issues |= FinTsReadContextIssue.DuplicateReport; }
        foreach (var report in response.Discovery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Source.Version != read.Source.Version) { issues |= FinTsReadContextIssue.VersionMismatch; }
            if (report.Accounts.Count == 0 && !unavailableReported) { issues |= FinTsReadContextIssue.MissingReport; }
            if (report.Accounts.Count > 1) { issues |= FinTsReadContextIssue.DuplicateReport; }
            foreach (var account in report.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (selected is null || !SameNational(account, selected) || !MatchesUserAccount(account, capability.Account))
                { issues |= FinTsReadContextIssue.ResponseAccountMismatch; }
            }
        }
        foreach (var report in response.Balances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Source.Version != read.Source.Version) { issues |= FinTsReadContextIssue.VersionMismatch; }
            if (selected is null || !SameAccount(report.Account, selected) || !MatchesUserAccount(report.Account, capability.Account))
            { issues |= FinTsReadContextIssue.ResponseAccountMismatch; }
            string currency = capability.Account.Currency?.HeaderText() ?? "";
            if (report.HasCurrencyConflict || currency.Length != 0 && currency != report.AccountCurrency) { issues |= FinTsReadContextIssue.CurrencyMismatch; }
            if (report.DueDate is not null) { issues |= FinTsReadContextIssue.DueDateNeedsReview; }
        }
        issues |= ContinuationIssues(request, capability, nationalAdvertisement, previousPage, out int pageNumber);
        return new(request, response, capability, nationalAdvertisement, issues, outcome, continuation, pageNumber);
    }

    internal static FinTsReadContextIssue ResponseIssues(FinTsReadRequestContext request, FinTsReadDataSet response,
        out bool unavailableReported, out string? continuation, out FinTsReadOutcomeObservation outcome, CancellationToken cancellationToken)
    {
        var issues = FinTsReadContextIssue.None;
        var read = request.Request;
        bool discovery = read.Source.Code == "HKSPA";
        var source = response.Source;
        var related = source.ReplySegments.Where(s => s.RequestSegmentNumber == read.Source.Number).SelectMany(s => s.Replies).ToArray();
        var unavailable = related.Where(r => r.Code == "3010").ToArray();
        unavailableReported = unavailable.Length == 1 && unavailable[0].ElementReference.IsEmpty && unavailable[0].Parameters.All(p => p.IsEmpty);
        var header = source.Frame.Syntax.Segments[0].Fields;
        if (source.Frame.MessageNumber != request.ExpectedBankMessageNumber || Dialog(source.Frame) != Dialog(request.Frame) ||
            header.Count != 5 || header[4].Elements.Count != 2 || header[4].Elements[0].HeaderText() != Dialog(request.Frame) ||
            header[4].Elements[1].HeaderText() != request.Frame.MessageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { issues |= FinTsReadContextIssue.MessageMismatch; }
        if (source.PinTanEnvelope is { ProfileVersion: not 2 }) { issues |= FinTsReadContextIssue.ProfileNeedsReview; }
        foreach (var segment in source.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Reference is int reference && reference != read.Source.Number) { issues |= FinTsReadContextIssue.ReferenceMismatch; }
        }
        // 3010 is interpreted only within this read request. Other consumers retain the raw unknown meaning.
        bool unknownStatus = source.ReplySegments.Any(s => s.Replies.Any(r => r.Meaning == FinTsReplyMeaning.Uninterpreted &&
            !(s.RequestSegmentNumber == read.Source.Number && r.Code == "3010")));
        if (source.HasErrors || source.HasConflictingClasses || source.HasIndeterminateProcessing || unknownStatus)
        { issues |= FinTsReadContextIssue.ResponseNeedsReview; }
        var partial = related.Where(r => r.Code == "3040").ToArray();
        int executions = related.Count(r => r.Code == "0020");
        if (source.ReplySegments.Where(s => s.IsMessageLevel).SelectMany(s => s.Replies).Any(r => r.Code is not ("0010" or "0020") || !r.ElementReference.IsEmpty || r.Parameters.Any(p => !p.IsEmpty)) ||
            related.Any(r => r.Code is not ("0020" or "3040" or "3010") || !r.ElementReference.IsEmpty || r.Code is "0020" or "3010" && r.Parameters.Any(p => !p.IsEmpty)) ||
            executions > 1 || partial.Length > 1 || unavailable.Length > 1 || executions == 0 && partial.Length == 0 && !unavailableReported)
        { issues |= FinTsReadContextIssue.StatusNeedsReview; }
        if (unavailable.Length != 0 && (partial.Length != 0 || response.Balances.Count != 0 || response.Discovery.Any(r => r.Accounts.Count != 0)))
        { issues |= FinTsReadContextIssue.AvailabilityConflict; }
        continuation = null;
        if (partial.Length == 1)
        {
            var values = partial[0].Parameters;
            if (discovery || values.Count == 0 || values[0].IsBinary || values[0].IsEmpty || values.Skip(1).Any(v => v.IsBinary || !v.IsEmpty))
            { issues |= FinTsReadContextIssue.ContinuationNeedsReview; }
            else
            {
                string token = values[0].HeaderText();
                if (token.Length > 35 || token.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { issues |= FinTsReadContextIssue.ContinuationNeedsReview; }
                else { continuation = token; }
            }
        }
        outcome = unavailableReported ? FinTsReadOutcomeObservation.UnavailableReported :
            partial.Length == 1 ? FinTsReadOutcomeObservation.PartialReported : FinTsReadOutcomeObservation.ExecutionReported;
        return issues;
    }

    internal static FinTsReadContextIssue RequestIssues(FinTsReadRequestContext request, FinTsReadCapabilityEvidence capability,
        FinTsReadAdvertisement? nationalAdvertisement)
    {
        var issues = FinTsReadContextIssue.None;
        var read = request.Request;
        bool discovery = read.Source.Code == "HKSPA";
        if (read.AllAccounts || read.Accounts.Count != 1) { issues |= FinTsReadContextIssue.UnsupportedAccountScope; }
        var selected = read.Accounts.Count == 1 ? read.Accounts[0] : null;
        if (!capability.HasMatchingEvidence) { issues |= FinTsReadContextIssue.CapabilityNeedsReview; }
        if (capability.Version != read.Source.Version || capability.Operation != (discovery ? FinTsReadOperation.SepaAccountDetails : FinTsReadOperation.Balance))
        { issues |= FinTsReadContextIssue.CapabilityScopeMismatch; }
        var parameters = capability.Source.Source;
        if (Dialog(parameters.Source.Frame) != Dialog(request.Frame) || parameters.Source.Frame.MessageNumber >= request.ExpectedBankMessageNumber)
        { issues |= FinTsReadContextIssue.ParameterDialogueMismatch; }
        if (selected is not null && !MatchesUserAccount(selected, capability.Account)) { issues |= FinTsReadContextIssue.RequestAccountMismatch; }
        if (read.MaximumEntries is not null && (capability.Candidates.Count != 1 || capability.Candidates[0].EntryCountInputAllowed != true))
        { issues |= FinTsReadContextIssue.EntryCountNotAdvertised; }
        if (!discovery && read.Source.Version is 7 or 8 && selected is { Number.Length: > 0 })
        {
            if (nationalAdvertisement is null || !capability.Source.Advertisements.Contains(nationalAdvertisement) ||
                nationalAdvertisement.Operation != FinTsReadOperation.SepaAccountDetails || nationalAdvertisement.NationalAccountConnectionAllowed != true ||
                capability.Source.Advertisements.Count(a => a.Operation == nationalAdvertisement.Operation && a.Version == nationalAdvertisement.Version) != 1)
            { issues |= FinTsReadContextIssue.NationalAccountNotAdvertised; }
        }
        return issues;
    }

    internal static FinTsReadContextIssue ContinuationIssues(FinTsReadRequestContext request, FinTsReadCapabilityEvidence capability,
        FinTsReadAdvertisement? nationalAdvertisement, FinTsReadContextEvidence? previousPage, out int pageNumber)
    {
        var issues = FinTsReadContextIssue.None;
        var read = request.Request;
        var selected = read.Accounts.Count == 1 ? read.Accounts[0] : null;
        pageNumber = 1;
        if (previousPage is null)
        {
            if (read.ContinuationToken is not null) { issues |= FinTsReadContextIssue.ContinuationScopeMismatch; }
        }
        else
        {
            pageNumber = Math.Min(previousPage.PageNumber + 1, MaximumPages + 1);
            var prior = previousPage.Request;
            if (!previousPage.HasMatchingEvidence || previousPage.Outcome != FinTsReadOutcomeObservation.PartialReported ||
                read.ContinuationToken is null || read.ContinuationToken != previousPage.ReportedContinuationToken ||
                !ReferenceEquals(capability, previousPage.Capability) || !ReferenceEquals(nationalAdvertisement, previousPage.NationalAdvertisement) ||
                Dialog(request.Frame) != Dialog(prior.Frame) || request.Frame.MessageNumber != prior.Frame.MessageNumber + 1 ||
                request.ExpectedBankMessageNumber != prior.ExpectedBankMessageNumber + 1 ||
                read.Source.Code != prior.Request.Source.Code || read.Source.Version != prior.Request.Source.Version ||
                read.AllAccounts != prior.Request.AllAccounts || read.MaximumEntries != prior.Request.MaximumEntries ||
                selected is null || prior.Request.Accounts.Count != 1 || !SameAccount(selected, prior.Request.Accounts[0]))
            { issues |= FinTsReadContextIssue.ContinuationScopeMismatch; }
            if (pageNumber > MaximumPages) { issues |= FinTsReadContextIssue.PageLimitExceeded; }
        }
        return issues;
    }

    internal static string Dialog(FinTsMessageFrame frame) => frame.Syntax.Segments[0].Fields[2].Elements[0].HeaderText();
    private static bool SameNational(FinTsReadAccount a, FinTsReadAccount b) =>
        a.Number == b.Number && a.Subaccount == b.Subaccount && a.Country == b.Country && a.Institution == b.Institution;
    private static bool SameAccount(FinTsReadAccount a, FinTsReadAccount b) => SameNational(a, b) && a.Iban == b.Iban && a.Bic == b.Bic;
    private static bool MatchesUserAccount(FinTsReadAccount account, FinTsAccountParameters user)
    {
        if (!user.HasAccountConnection) { return false; }
        var national = user.AccountConnection.Elements;
        string At(int index) => national.Count > index ? national[index].HeaderText() : "";
        if (account.Number.Length != 0 && (account.Number != At(0) || account.Subaccount != At(1) || account.Country != At(2) || account.Institution != At(3))) { return false; }
        string iban = user.Iban.HeaderText();
        if (account.SepaUsageReported is not null && iban.Length != 0 && account.Iban != iban) { return false; }
        // Discovery can fill a previously absent IBAN. National requests need no invented IBAN.
        if (account.Iban.Length != 0 && iban.Length != 0 && account.Iban != iban) { return false; }
        return account.Number.Length != 0 || iban.Length != 0 && account.Iban == iban;
    }
}
