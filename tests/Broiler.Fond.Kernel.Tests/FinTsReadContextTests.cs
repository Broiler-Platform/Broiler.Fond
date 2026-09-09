using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadContextTests
{
    private const string National = "PUBLIC-001:00:280:PUBLIC-BANK";
    private const string International = "PUBLIC-IBAN:PUBLIC-BIC";
    private const string Combined = International + ":" + National;
    private const string Bank = "HIBPA:3+1+280:PUBLIC-BANK+Bank+1+1+300";
    private const string User = "HIUPA:4+PUBLIC-USER+1+1";
    private const string Account = "HIUPD:6+" + National + "+PUBLIC-IBAN+C+1+EUR+Owner++++HKSAL:1+HKSPA:1";
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Has(FinTsReadContextEvidence value, FinTsReadContextIssue issue)
        { Verify(value.Issues.HasFlag(issue) && !value.HasMatchingEvidence && value.Outcome == FinTsReadOutcomeObservation.NeedsReview, $"Read context must expose {issue}; got {value.Issues}."); }
        var capability = Capability();
        var request = Request();
        var data = Data();
        var evidence = FinTsReadContextEvidence.Evaluate(request, data, capability);
        Verify(evidence.HasMatchingEvidence && evidence.Outcome == FinTsReadOutcomeObservation.ExecutionReported && evidence.PageNumber == 1, "Exact single-account evidence matches without claiming authenticated completion.");
        Verify(ReferenceEquals(evidence.Request, request) && ReferenceEquals(evidence.Response, data) && ReferenceEquals(evidence.Capability, capability), "Exact source instances remain attached.");
        Verify(FinTsReadContextEvidence.Evaluate(request, data, capability).HasMatchingEvidence, "Pure comparison consumes no replay state.");
        Verify(Evaluate(FinTsReadRequestContext.Parse(request.Frame, 3), Data(number: 3, referenceNumber: 2), capability).HasMatchingEvidence, "Client and bank counters are checked independently.");
        var wrapped = Wrapped(data);
        Verify(Evaluate(request, wrapped, capability).HasMatchingEvidence && wrapped.Source.PinTanEnvelope is not null, "Explicitly parsed PIN/TAN responses preserve outer/inner context without authentication.");
        Has(Evaluate(request, Wrapped(data, 1), capability), FinTsReadContextIssue.ProfileNeedsReview);
        foreach (int version in new[] { 6, 7, 8 })
        {
            var cap = Capability(version: version);
            Verify(Evaluate(Request(version: version), Data(version: version), cap).HasMatchingEvidence, "Supported balance versions match only their exact evidence.");
        }
        var spaCap = Capability(discovery: true, version: 1);
        var spaRequest = Request(discovery: true, version: 1);
        var spaData = Data(discovery: true, version: 1);
        Verify(Evaluate(spaRequest, spaData, spaCap).HasMatchingEvidence, "Discovery matches exact national tuples and a known IBAN.");
        var newIban = Capability(discovery: true, version: 1, account: Account.Replace("+PUBLIC-IBAN+", "++", StringComparison.Ordinal));
        Verify(Evaluate(spaRequest, spaData, newIban).HasMatchingEvidence, "Discovery may report a previously absent IBAN without allocating an identity.");
        Has(Evaluate(spaRequest, Data(discovery: true, version: 1, account: "N:::" + National), spaCap), FinTsReadContextIssue.ResponseAccountMismatch);
        Verify(Evaluate(spaRequest, Data(discovery: true, version: 1, account: "N:::" + National), newIban).HasMatchingEvidence, "A non-SEPA shell may match an account with no previously reported IBAN.");
        Has(Evaluate(Request(all: true), data, capability), FinTsReadContextIssue.UnsupportedAccountScope);
        Has(Evaluate(Request(discovery: true, version: 1, account: ""), spaData, spaCap), FinTsReadContextIssue.UnsupportedAccountScope);
        Has(Evaluate(Request(discovery: true, version: 1, account: National + "+" + National), spaData, spaCap), FinTsReadContextIssue.UnsupportedAccountScope);
        Has(Evaluate(request, data, Capability(version: 7)), FinTsReadContextIssue.CapabilityScopeMismatch);
        Has(Evaluate(request, data, spaCap), FinTsReadContextIssue.CapabilityScopeMismatch);
        Has(Evaluate(request, data, Capability(account: Account.Replace("HKSAL:1", "HKXYZ:1", StringComparison.Ordinal))), FinTsReadContextIssue.CapabilityNeedsReview);
        Has(Evaluate(request, data, Capability(dialog: "OTHER")), FinTsReadContextIssue.ParameterDialogueMismatch);
        Has(Evaluate(request, data, Capability(number: 2)), FinTsReadContextIssue.ParameterDialogueMismatch);
        Has(Evaluate(Request(account: "OTHER:PUBLIC-BIC"), data, capability), FinTsReadContextIssue.RequestAccountMismatch);
        Has(Evaluate(Request(version: 6, account: National.Replace(":00:", ":0:", StringComparison.Ordinal)), Data(version: 6), Capability(version: 6)), FinTsReadContextIssue.RequestAccountMismatch);
        Has(Evaluate(Request(options: "+1"), data, Capability(entryFlag: "N")), FinTsReadContextIssue.EntryCountNotAdvertised);
        Has(Evaluate(Request(version: 7, options: "+1"), Data(version: 7), Capability(version: 7)), FinTsReadContextIssue.EntryCountNotAdvertised);
        Verify(Evaluate(Request(options: "+1"), data, capability).HasMatchingEvidence, "An explicit matching entry-count J advertisement supplies option evidence.");
        var combined = Request(account: Combined);
        var combinedData = Data(account: Combined);
        Has(Evaluate(combined, combinedData, capability), FinTsReadContextIssue.NationalAccountNotAdvertised);
        var nationalAd = capability.Source.Advertisements.Single(a => a.Operation == FinTsReadOperation.SepaAccountDetails);
        Verify(FinTsReadContextEvidence.Evaluate(combined, combinedData, capability, nationalAd).HasMatchingEvidence, "Combined identifiers require an explicitly selected in-scope national-account advertisement.");
        Has(FinTsReadContextEvidence.Evaluate(combined, combinedData, capability, Capability().Source.Advertisements.Last()), FinTsReadContextIssue.NationalAccountNotAdvertised);
        var nationalBlocked = Capability(nationalFlag: "N");
        Has(FinTsReadContextEvidence.Evaluate(combined, combinedData, nationalBlocked, nationalBlocked.Source.Advertisements.Last()), FinTsReadContextIssue.NationalAccountNotAdvertised);
        foreach (var altered in new[] { Data(number: 3), Data(dialog: "OTHER"), Data(referenceNumber: 1), Data(referenceDialog: "OTHER"), Data(omitReference: true) })
        { Has(Evaluate(request, altered, capability), FinTsReadContextIssue.MessageMismatch); }
        Has(Evaluate(request, Data(reportReference: 1), capability), FinTsReadContextIssue.ReferenceMismatch);
        Has(Evaluate(request, Data(statusReference: 1), capability), FinTsReadContextIssue.ReferenceMismatch);
        Has(Evaluate(request, Data(version: 7), capability), FinTsReadContextIssue.VersionMismatch);
        Has(Evaluate(request, Data(reportVersion: 99), capability), FinTsReadContextIssue.UnexpectedReport);
        Has(Evaluate(request, Data(extra: "HIXYZ:1+PUBLIC"), capability), FinTsReadContextIssue.UnexpectedReport);
        Has(Evaluate(request, Data(extra: Bank), capability), FinTsReadContextIssue.UnexpectedReport);
        Has(Evaluate(request, Data(omitReport: true), capability), FinTsReadContextIssue.MissingReport);
        Has(Evaluate(request, Data(extra: Report(8, International)), capability), FinTsReadContextIssue.DuplicateReport);
        Has(Evaluate(spaRequest, Data(discovery: true, version: 1, account: "J:" + Combined + "+J:" + Combined), spaCap), FinTsReadContextIssue.DuplicateReport);
        Has(Evaluate(spaRequest, Data(discovery: true, version: 1, account: ""), spaCap), FinTsReadContextIssue.MissingReport);
        foreach (string account in new[] { "OTHER:PUBLIC-BIC", "PUBLIC-IBAN:OTHER", Combined })
        { Has(Evaluate(request, Data(account: account), capability), FinTsReadContextIssue.ResponseAccountMismatch); }
        Has(Evaluate(spaRequest, Data(discovery: true, version: 1, account: "J:OTHER:PUBLIC-BIC:" + National), spaCap), FinTsReadContextIssue.ResponseAccountMismatch);
        Has(Evaluate(request, Data(currency: "USD"), capability), FinTsReadContextIssue.CurrencyMismatch);
        Has(Evaluate(request, Data(suffix: "+C:1,:USD:20260907"), capability), FinTsReadContextIssue.CurrencyMismatch);
        Has(Evaluate(request, Data(suffix: "+++++++20261001"), capability), FinTsReadContextIssue.DueDateNeedsReview);
        foreach (string status in new[] { "9000::PUBLIC", "9210::PUBLIC", "3998::PUBLIC", "0030::PUBLIC", "0010::PUBLIC", "0020:1:PUBLIC", "0020::PUBLIC:param", "0020::PUBLIC+0020::PUBLIC" })
        {
            var result = Evaluate(request, Data(status: status), capability);
            Verify(!result.HasMatchingEvidence && result.Outcome == FinTsReadOutcomeObservation.NeedsReview, "Errors, unknown/pending/receipt-only and ambiguous statuses require review.");
        }
        Has(Evaluate(request, Data(messageStatus: "3040::PUBLIC:PAGE"), capability), FinTsReadContextIssue.StatusNeedsReview);
        Has(Evaluate(request, Data(messageStatus: "0020::PUBLIC:param"), capability), FinTsReadContextIssue.StatusNeedsReview);
        var first = Evaluate(Request(options: "+1"), Data(status: "3040::PUBLIC:PUBLIC?+PAGE"), capability);
        Verify(first.HasMatchingEvidence && first.Outcome == FinTsReadOutcomeObservation.PartialReported && first.ReportedContinuationToken == "PUBLIC+PAGE", "A scoped partial token remains opaque and partial.");
        Verify(Evaluate(request, Data(status: "0020::PUBLIC+3040::PUBLIC:PAGE"), capability).Outcome == FinTsReadOutcomeObservation.PartialReported, "A partial report takes precedence over an execution report.");
        foreach (string status in new[] { "3040::PUBLIC", "3040::PUBLIC:", "3040::PUBLIC:PAGE:OTHER" })
        { Has(Evaluate(request, Data(status: status), capability), FinTsReadContextIssue.ContinuationNeedsReview); }
        Has(Evaluate(request, Data(status: "3040::PUBLIC:PAGE+3040::PUBLIC:PAGE"), capability), FinTsReadContextIssue.StatusNeedsReview);
        Has(Evaluate(spaRequest, Data(discovery: true, version: 1, status: "3040::PUBLIC:PAGE"), spaCap), FinTsReadContextIssue.ContinuationNeedsReview);
        Verify(Evaluate(request, Data(status: "3040::PUBLIC:PAGE:"), capability).HasMatchingEvidence, "Trailing empty parameter positions do not create extra tokens.");
        var nextRequest = Request(number: 3, options: "+1+PUBLIC?+PAGE");
        var nextData = Data(number: 3);
        var next = FinTsReadContextEvidence.Evaluate(nextRequest, nextData, capability, previousPage: first);
        Verify(next.HasMatchingEvidence && next.PageNumber == 2 && next.Outcome == FinTsReadOutcomeObservation.ExecutionReported && next.ReportedContinuationToken is null, "An exact next-page pair retains provenance without aggregating or persisting balances.");
        Has(Evaluate(nextRequest, nextData, capability), FinTsReadContextIssue.ContinuationScopeMismatch);
        foreach (var altered in new[] { Request(number: 3, options: "+1+OTHER"), Request(number: 3, options: "+1"), Request(number: 3, options: "+2+PUBLIC?+PAGE"), Request(number: 4, options: "+1+PUBLIC?+PAGE"), Request(number: 3, options: "+1+PUBLIC?+PAGE", dialog: "OTHER"), Request(number: 3, options: "+1+PUBLIC?+PAGE", account: "OTHER:PUBLIC-BIC") })
        { Has(FinTsReadContextEvidence.Evaluate(altered, nextData, capability, previousPage: first), FinTsReadContextIssue.ContinuationScopeMismatch); }
        Has(FinTsReadContextEvidence.Evaluate(nextRequest, nextData, Capability(), previousPage: first), FinTsReadContextIssue.ContinuationScopeMismatch);
        Has(FinTsReadContextEvidence.Evaluate(nextRequest, nextData, capability, previousPage: evidence), FinTsReadContextIssue.ContinuationScopeMismatch);
        var rejected = Evaluate(Request(options: "+1"), Data(status: "3040::PUBLIC:PUBLIC?+PAGE", account: "OTHER:PUBLIC-BIC"), capability);
        Has(FinTsReadContextEvidence.Evaluate(nextRequest, nextData, capability, previousPage: rejected), FinTsReadContextIssue.ContinuationScopeMismatch);
        var chain = Evaluate(request, Data(status: "3040::PUBLIC:PAGE"), capability);
        for (int page = 2; page <= FinTsReadContextEvidence.MaximumPages + 1; page++)
        {
            chain = FinTsReadContextEvidence.Evaluate(Request(number: page + 1, options: "++PAGE"), Data(number: page + 1, status: "3040::PUBLIC:PAGE"), capability, previousPage: chain);
            if (page <= FinTsReadContextEvidence.MaximumPages) { Verify(chain.HasMatchingEvidence && chain.PageNumber == page, "Bounded page provenance accepts sequential counters without assuming token uniqueness."); }
        }
        Has(chain, FinTsReadContextIssue.PageLimitExceeded);
        foreach (Action action in new Action[]
        {
            () => FinTsReadRequestContext.Parse(Frame(2, ["HKSAL:8+" + International + "+N"], false), 0),
            () => FinTsReadRequestContext.Parse(Frame(2, ["HKSAL:8+" + International + "+N"], false), 10000),
            () => Request(dialog: "0"), () => Request(dialog: "unbekannt"),
            () => FinTsReadRequestContext.Parse(Frame(2, ["HKSAL:8+" + International + "+N", "HKSPA:1"], false), 2),
            () => FinTsReadRequestContext.Parse(Frame(2, ["HKSAL:8+" + International + "+N"], true), 2),
            () => FinTsReadRequestContext.Parse(wrapped.Source.Frame, 2),
        })
        {
            try { action(); Verify(false, "Invalid restricted read request must fail."); }
            catch (FinTsFormatException error) { Verify(!error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Fixed restricted-request errors exclude source identifiers."); }
        }
        try { FinTsReadContextEvidence.Evaluate(request, data, capability, cancellationToken: new CancellationToken(true)); Verify(false, "Context cancellation must stop."); }
        catch (OperationCanceledException) { Verify(true, "Context cancellation observed."); }
        try { FinTsReadRequestContext.Parse(request.Frame, 2, new CancellationToken(true)); Verify(false, "Request cancellation must stop."); }
        catch (OperationCanceledException) { Verify(true, "Request cancellation observed."); }
        Verify(!first.ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !nextRequest.ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default context diagnostics exclude identifiers and continuation tokens.");
        Fixtures(Verify);
        Console.WriteLine($"FinTS read-context evidence verification passed ({count} checks).");
    }
    private static FinTsReadContextEvidence Evaluate(FinTsReadRequestContext request, FinTsReadDataSet data, FinTsReadCapabilityEvidence capability) => FinTsReadContextEvidence.Evaluate(request, data, capability);
    private static FinTsReadDataSet Wrapped(FinTsReadDataSet source, int profile = 2)
    {
        string plain = Encoding.Latin1.GetString(source.Source.Frame.Syntax.CopyWireBytes());
        string body = plain[(plain.IndexOf('\'') + 1)..plain.LastIndexOf("HNHBS:", StringComparison.Ordinal)];
        string header = $"HNVSK:998:3+PIN:{profile}+998+1+1::PUBLIC-SYSTEM+1:20260907:120000+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:10020030:PUBLIC-KEY-ID:V:0:0+0'";
        string wire = "HNHBK:1:3+000000000000+300+SYNTHETIC+2+SYNTHETIC:2'" + header + "HNVSD:999:1+@" + Encoding.Latin1.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "@" + body + "'HNHBS:5:1+2'";
        wire = wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsReadDataSet.Parse(FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)))));
    }
    private static FinTsReadCapabilityEvidence Capability(int version = 8, bool discovery = false, string account = Account, string dialog = "SYNTHETIC", int number = 1, string entryFlag = "J", string nationalFlag = "J")
    {
        var parts = new List<string> { "HIRMG:2+0010::PUBLIC", Bank, User, account };
        if (!discovery) { parts.Add($"HISALS:{version}+1+1+0" + (version == 8 ? "+" + entryFlag : "")); }
        parts.Add("HISPAS:1+1+1+0+J:" + nationalFlag + ":N");
        var source = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(number, parts.ToArray(), true, dialog))));
        return FinTsReadCapabilityEvidence.Evaluate(source, source.Source.Accounts[0], discovery ? FinTsReadOperation.SepaAccountDetails : FinTsReadOperation.Balance, version);
    }
    private static FinTsReadRequestContext Request(int version = 8, bool discovery = false, string? account = null, bool all = false, string options = "", int number = 2, string dialog = "SYNTHETIC")
    {
        account ??= discovery || version == 6 ? National : International;
        string body = discovery ? "HKSPA:1" + (account.Length == 0 ? "" : "+" + account) : $"HKSAL:{version}+{account}+{(all ? "J" : "N")}{options}";
        return FinTsReadRequestContext.Parse(Frame(number, [body], false, dialog), number);
    }
    private static string Report(int version, string account, int reference = 2, string currency = "EUR", string suffix = "") => $"HISAL:{version}:{reference}+{account}+PUBLIC+{currency}+C:1,:{currency}:20260907{suffix}";
    private static FinTsReadDataSet Data(int version = 8, bool discovery = false, string? account = null, int number = 2, string dialog = "SYNTHETIC", int? referenceNumber = null, string? referenceDialog = null,
        bool omitReference = false, int reportReference = 2, int statusReference = 2, int? reportVersion = null, string? extra = null, bool omitReport = false, string currency = "EUR", string suffix = "", string status = "0020::PUBLIC", string messageStatus = "0010::PUBLIC")
    {
        account ??= discovery ? "J:" + Combined : version == 6 ? National : International;
        var parts = new List<string> { "HIRMG:2+" + messageStatus, $"HIRMS:2:{statusReference}+{status}" };
        if (!omitReport) { parts.Add(discovery ? $"HISPA:{reportVersion ?? version}:{reportReference}" + (account.Length == 0 ? "" : "+" + account) : Report(reportVersion ?? version, account, reportReference, currency, suffix)); }
        if (extra is not null) { parts.Add(extra); }
        return FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(number, parts.ToArray(), !omitReference, dialog, referenceNumber, referenceDialog)));
    }
    private static FinTsMessageFrame Frame(int number, string[] body, bool response, string dialog = "SYNTHETIC", int? referenceNumber = null, string? referenceDialog = null)
    {
        string header = $"HNHBK:1:3+000000000000+300+{dialog}+{number}" + (response ? $"+{referenceDialog ?? dialog}:{referenceNumber ?? number}" : "") + "'";
        var pieces = body.Select((part, index) => { int colon = part.IndexOf(':'); return part[..colon] + ":" + (index + 2).ToString(CultureInfo.InvariantCulture) + part[colon..] + "'"; });
        string wire = header + string.Concat(pieces) + $"HNHBS:{body.Length + 2}:1+{number}'";
        wire = wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
    private static void Fixtures(Action<bool, string> verify)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-context-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        int count = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            count++;
            FinTsMessageFrame Load(string key) => FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty(key).GetString()!));
            var parameters = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Load("parametersBase64"))));
            var request = FinTsReadRequestContext.Parse(Load("requestBase64"), 2);
            var capability = FinTsReadCapabilityEvidence.Evaluate(parameters, parameters.Source.Accounts[0], request.Request.Source.Code == "HKSPA" ? FinTsReadOperation.SepaAccountDetails : FinTsReadOperation.Balance, request.Request.Source.Version);
            var evidence = FinTsReadContextEvidence.Evaluate(request, FinTsReadDataSet.Parse(FinTsResponse.Parse(Load("responseBase64"))), capability);
            var issues = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsReadContextIssue.None, (result, item) => result | Enum.Parse<FinTsReadContextIssue>(item.GetString()!));
            verify(evidence.Issues == issues, $"Independent read-context issue set differs for {vector.GetProperty("name").GetString()}: {evidence.Issues}.");
            verify(evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString() && evidence.ReportedContinuationToken == vector.GetProperty("continuation").GetString(), "Independent outcome and token expectations match.");
        }
        verify(count == 8, "All eight independent read-context fixtures ran.");
    }
}
