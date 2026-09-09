using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsAllAccountDiscoveryTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.all-discovery-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var request = Request(vector); var response = Response(vector); var parameters = Parameters(vector);
            var evidence = FinTsAllAccountDiscoveryEvidence.Evaluate(request, response, parameters);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsAllDiscoveryIssue.None, (v, i) => v | Enum.Parse<FinTsAllDiscoveryIssue>(i.GetString()!));
            Verify(evidence.Issues == expected && evidence.ResponseIssues == FinTsReadContextIssue.None, $"Independent all-discovery issues differ for {vector.GetProperty("name").GetString()}: {evidence.Issues}, {evidence.ResponseIssues}.");
            Verify(evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString() && evidence.UnmatchedUserAccounts.Count == vector.GetProperty("unmatched").GetInt32(), "Independent outcome and unmatched counts match.");
            var rowIssues = vector.GetProperty("rowIssues").EnumerateArray().ToArray();
            Verify(evidence.Accounts.Count == rowIssues.Length, "Every returned account produces a preserved evidence row.");
            for (int i = 0; i < rowIssues.Length; i++)
            {
                var expectedRow = rowIssues[i].EnumerateArray().Aggregate(FinTsDiscoveryAccountIssue.None, (v, item) => v | Enum.Parse<FinTsDiscoveryAccountIssue>(item.GetString()!));
                var row = evidence.Accounts[i];
                Verify(row.Issues == expectedRow && row.Candidates.Count == vector.GetProperty("candidateCounts")[i].GetInt32(), "Independent per-account issues and complete candidate counts match.");
                Verify(ReferenceEquals(row.Source, response.Discovery[0].Accounts[i]) && row.Candidates.All(parameters.Source.Accounts.Contains), "Returned fields and parameter candidates retain exact source instances.");
                Verify(row.MatchedAccount is not null == (expectedRow == FinTsDiscoveryAccountIssue.None), "Ambiguous, unknown or conflicting rows expose no clean match.");
            }
            Verify(ReferenceEquals(evidence.Response, response) && ReferenceEquals(evidence.Parameters, parameters) && ReferenceEquals(evidence.Request, request), "All-account evidence retains the supplied scope objects.");
        }
        Verify(vectors.Length == 8, "All eight independent discovery fixtures ran.");
        var sample = vectors[0]; var requestBase = Request(sample); var responseBase = Response(sample); var parametersBase = Parameters(sample);
        FinTsAllAccountDiscoveryEvidence Evaluate(FinTsReadDataSet? response = null, FinTsReadParameterSet? parameters = null, FinTsReadRequestContext? request = null) =>
            FinTsAllAccountDiscoveryEvidence.Evaluate(request ?? requestBase, response ?? responseBase, parameters ?? parametersBase);
        Verify(Evaluate().HasMatchingEvidence && parametersBase.Advertisements.Single().SingleAccountRequestAllowed == false, "An all-account request works with an all-accounts-only advertisement.");
        Verify(Evaluate(parameters: Parameters(sample, s => s.Replace("+N:J:N", "+J:J:N", StringComparison.Ordinal))).HasMatchingEvidence, "Allowing selected-account requests does not disable all-account discovery.");
        Verify(Evaluate().Accounts[1].Source.SepaUsageReported == false && Evaluate().Accounts[0].MinimumCustomerSignatures == 1, "Non-SEPA shells and reported signature requirements remain explicit.");
        Verify(Evaluate().Accounts[0].MatchedAccount == parametersBase.Source.Accounts[0] && Evaluate().Accounts[1].MatchedAccount == parametersBase.Source.Accounts[1], "Exact source accounts match without local identity allocation.");
        var ibanfilling = Evaluate(parameters: Parameters(sample, s => s.Replace("+PUBLIC-IBAN+", "++", StringComparison.Ordinal)));
        Verify(ibanfilling.HasMatchingEvidence, "Discovery may fill an absent UPD IBAN without changing the source parameters.");
        var priorIban = Evaluate(parameters: Parameters(sample, s => s.Replace("PUBLIC-002:00:280:PUBLIC-BANK++", "PUBLIC-002:00:280:PUBLIC-BANK+OTHER-IBAN+", StringComparison.Ordinal)));
        Verify(priorIban.Accounts[1].Issues.HasFlag(FinTsDiscoveryAccountIssue.IdentityConflict), "A non-SEPA response cannot silently clear a previously reported IBAN.");
        foreach (var parameters in new[]
        {
            Parameters(sample, s => s.Replace("+300'", "+299'", StringComparison.Ordinal)),
            Parameters(sample, s => s.Replace("0010::Synthetic", "3010::Synthetic", StringComparison.Ordinal)),
        })
        { Verify(Evaluate(parameters: parameters).Issues.HasFlag(FinTsAllDiscoveryIssue.ParametersNeedReview), "Missing protocol support and unresolved parameter status prevent a global match."); }
        Verify(Evaluate(parameters: Parameters(sample, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.ParameterDialogueMismatch), "Parameters from a different dialogue cannot qualify returned accounts.");
        foreach (string code in new[] { "HIBPA", "HIUPA" })
        { Verify(Evaluate(parameters: Parameters(sample, s => s.Replace(code, "HIXYZ", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.ParametersNeedReview), "Missing bank or user parameters cannot qualify the complete discovery result."); }
        Verify(Evaluate(parameters: Parameters(sample, s => s.Replace("HISPAS:7:1+1+1+0", "HISPAS:7:1+0+1+0", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.AdvertisementNeedsReview), "Zero advertised order capacity cannot qualify an all-account request.");
        Verify(Evaluate(parameters: Parameters(sample, s => s.Replace("HISPAS:7:1", "HISPAS:7:99", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.AdvertisementNeedsReview), "Unknown parameter versions cannot stand in for HISPAS-1.");
        var duplicateAd = Parameters(sample, s => s.Replace("HNHBS:8:1+1'", "HISPAS:8:1+1+1+0+N:J:N'HNHBS:9:1+1'", StringComparison.Ordinal));
        Verify(Evaluate(parameters: duplicateAd).Issues.HasFlag(FinTsAllDiscoveryIssue.AdvertisementNeedsReview), "Duplicate exact-version advertisements require review.");
        Verify(Evaluate(parameters: Parameters(sample, s => s.Replace("HKSPA:1", "HKSPA:0", StringComparison.Ordinal))).Accounts.All(a => a.Issues.HasFlag(FinTsDiscoveryAccountIssue.SignatureConflict)), "Insufficient reported account signatures are not promoted to the bank minimum.");
        var wrongInstitution = Evaluate(response: Response(sample, s => s.Replace(":280:PUBLIC-BANK", ":280:OTHER-BANK", StringComparison.Ordinal)));
        Verify(wrongInstitution.Accounts.All(a => a.Issues.HasFlag(FinTsDiscoveryAccountIssue.InstitutionMismatch)), "Returned accounts from another institution remain review candidates.");
        var knownIbanWrongNational = Evaluate(response: Response(sample, s => s.Replace("PUBLIC-001:00", "OTHER-001:00", StringComparison.Ordinal)));
        Verify(knownIbanWrongNational.Accounts[0].Candidates.Count == 1 && knownIbanWrongNational.Accounts[0].Issues.HasFlag(FinTsDiscoveryAccountIssue.IdentityConflict), "An IBAN-only candidate cannot override a conflicting national tuple.");
        var changedSubaccount = Evaluate(response: Response(sample, s => s.Replace("PUBLIC-001:00", "PUBLIC-001:0", StringComparison.Ordinal)));
        Verify(changedSubaccount.Accounts[0].Issues.HasFlag(FinTsDiscoveryAccountIssue.IdentityConflict), "Subaccount identifiers are compared exactly, preserving leading zeros.");
        var duplicateIban = Evaluate(response: Response(sample, s => s.Replace("N:::PUBLIC-002", "J:PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-002", StringComparison.Ordinal)));
        Verify(duplicateIban.Accounts.All(a => a.Issues.HasFlag(FinTsDiscoveryAccountIssue.DuplicateReturnedIdentity)), "A duplicate IBAN across different national accounts marks every affected returned row.");
        foreach (string status in new[] { "0010::Synthetic", "0030::Synthetic", "9210::Synthetic", "3010::Synthetic", "3040::Synthetic:PAGE", "0020:1:Synthetic" })
        { Verify(!Evaluate(response: Response(sample, s => s.Replace("0020::Synthetic", status, StringComparison.Ordinal))).HasMatchingEvidence, "Scoped status and returned-data conflicts prevent a global match."); }
        foreach (var response in new[]
        {
            Response(sample, s => s.Replace("HISPA:4:1:2", "HISPA:4:1:1", StringComparison.Ordinal)),
            Response(sample, s => s.Replace("+SYNTHETIC:2", "+OTHER:2", StringComparison.Ordinal)),
            Response(sample, s => s.Replace("HISPA:4:1:2", "HISPA:4:2:2", StringComparison.Ordinal)),
            Response(sample, s => s.Replace("HNHBS:5:1+2'", "HIXYZ:5:1+PUBLIC'HNHBS:6:1+2'", StringComparison.Ordinal)),
        })
        { Verify(Evaluate(response: response).ResponseIssues != FinTsReadContextIssue.None, "Wrong references, dialogue and uninterpreted report versions remain explicit."); }
        var duplicateReport = Response(sample, s => s.Replace("HNHBS:5:1+2'", "HISPA:5:1:2'HNHBS:6:1+2'", StringComparison.Ordinal));
        Verify(Evaluate(response: duplicateReport).ResponseIssues.HasFlag(FinTsReadContextIssue.DuplicateReport), "Multiple HISPA-1 reports are not silently merged into one.");
        Verify(Evaluate(request: Request(sample, s => s.Replace("HKSPA:2:1'", "HKSPA:2:1+PUBLIC-001:00:280:PUBLIC-BANK'", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.UnsupportedRequest), "Selected-account requests remain outside the all-account comparator.");
        Verify(Evaluate(request: Request(sample, s => s.Replace("HKSPA:2:1'", "HKSAL:2:8+PUBLIC-IBAN:PUBLIC-BIC+J'", StringComparison.Ordinal))).Issues.HasFlag(FinTsAllDiscoveryIssue.UnsupportedRequest), "All-account balance queries cannot enter discovery matching.");
        Verify(Evaluate(request: Request(sample, s => s.Replace("HKSPA:2:1'", "HKSPA:2:1+'", StringComparison.Ordinal))).HasMatchingEvidence, "Trailing empty optional discovery fields preserve the all-account request.");
        var noData = vectors[7];
        var unavailable = FinTsAllAccountDiscoveryEvidence.Evaluate(Request(noData), Response(noData), Parameters(noData));
        Verify(unavailable.HasMatchingEvidence && unavailable.UnmatchedUserAccounts.Count == 1 && unavailable.Accounts.Count == 0, "An unavailable report retains unmatched UPD entries without inferring closure.");
        var singleCap = FinTsReadCapabilityEvidence.Evaluate(parametersBase, parametersBase.Source.Accounts[0], FinTsReadOperation.SepaAccountDetails, 1);
        Verify(FinTsReadContextEvidence.Evaluate(requestBase, responseBase, singleCap).Issues.HasFlag(FinTsReadContextIssue.UnsupportedAccountScope), "The selected-account comparator is not loosened by the separate all-account path.");
        var attempt = new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(1));
        Verify(attempt.Start(requestBase, singleCap) == FinTsReadRefreshTransition.RejectedForReview, "The selected-account lifecycle remains explicitly restricted.");
        var manyParameters = ManyParameters(512);
        var manyClean = FinTsAllAccountDiscoveryEvidence.Evaluate(requestBase, ManyResponse(512, knownUnique: true), ManyParameters(512, unique: true));
        Verify(manyClean.HasMatchingEvidence && manyClean.Accounts.Count == 512 && manyClean.UnmatchedUserAccounts.Count == 0, "All 512 distinct supplied UPD accounts can match without collision or truncation.");
        var exact = FinTsAllAccountDiscoveryEvidence.Evaluate(requestBase, ManyResponse(8), manyParameters);
        Verify(exact.Accounts.Sum(a => a.Candidates.Count) == 4096 && exact.Accounts.All(a => a.MatchedAccount is null), "The exact candidate-link budget preserves ambiguity without a winner.");
        try { FinTsAllAccountDiscoveryEvidence.Evaluate(requestBase, ManyResponse(9), manyParameters); Verify(false, "Candidate expansion above its budget must fail."); }
        catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.LimitExceeded && !error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Candidate expansion fails with a fixed resource error."); }
        var manyReturned = FinTsAllAccountDiscoveryEvidence.Evaluate(requestBase, ManyResponse(999, unique: true), parametersBase);
        Verify(manyReturned.Accounts.Count == 999 && manyReturned.Accounts.All(a => a.Issues.HasFlag(FinTsDiscoveryAccountIssue.UnknownAccount)), "The discovery repetition boundary preserves every unknown account.");
        try { FinTsAllAccountDiscoveryEvidence.Evaluate(requestBase, responseBase, parametersBase, new CancellationToken(true)); Verify(false, "Discovery matching must honor cancellation."); }
        catch (OperationCanceledException) { Verify(true, "Discovery cancellation observed."); }
        Verify(new object[] { exact, exact.Accounts[0] }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default discovery diagnostics exclude account identifiers and candidate data.");
        Console.WriteLine($"FinTS all-account discovery verification passed ({count} checks).");
    }
    private static FinTsReadRequestContext Request(JsonElement v, Func<string, string>? transform = null) => FinTsReadRequestContext.Parse(Frame(v.GetProperty("requestBase64").GetString()!, transform), 2);
    private static FinTsReadDataSet Response(JsonElement v, Func<string, string>? transform = null) => FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(v.GetProperty("responseBase64").GetString()!, transform)));
    private static FinTsReadParameterSet Parameters(JsonElement v, Func<string, string>? transform = null) => FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(v.GetProperty("parametersBase64").GetString()!, transform))));
    private static FinTsReadParameterSet ManyParameters(int count, bool unique = false)
    {
        List<string> body = ["HIRMG:2+0010::Synthetic", "HIBPA:3+1+280:PUBLIC-BANK+Bank+1+1+300", "HIUPA:4+PUBLIC-USER+1+1"];
        body.AddRange(Enumerable.Range(0, count).Select(i => unique ? $"HIUPD:6+PUBLIC-{i}:00:280:PUBLIC-BANK++C+1+EUR+Owner++++HKSPA:1" : "HIUPD:6+PUBLIC-001:00:280:PUBLIC-BANK+PUBLIC-IBAN+C+1+EUR+Owner++++HKSPA:1"));
        body.Add("HISPAS:1+1+1+0+N:J:N");
        return FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Build(body, 1))));
    }
    private static FinTsReadDataSet ManyResponse(int count, bool unique = false, bool knownUnique = false)
    {
        var accounts = Enumerable.Range(0, count).Select(i => knownUnique ? $"N:::PUBLIC-{i}:00:280:PUBLIC-BANK" : unique ? $"N:::UNKNOWN-{i}::280:PUBLIC-BANK" : "J:PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-001:00:280:PUBLIC-BANK");
        return FinTsReadDataSet.Parse(FinTsResponse.Parse(Build(["HIRMG:2+0010::Synthetic", "HIRMS:2:2+0020::Synthetic", "HISPA:1:2+" + string.Join('+', accounts)], 2)));
    }
    private static FinTsMessageFrame Build(List<string> body, int number)
    {
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{number}+SYNTHETIC:{number}'";
        wire += string.Concat(body.Select((part, i) => { int colon = part.IndexOf(':'); return part[..colon] + ":" + (i + 2).ToString(CultureInfo.InvariantCulture) + part[colon..] + "'"; }));
        wire += $"HNHBS:{body.Count + 2}:1+{number}'";
        return ParseWire(wire);
    }
    private static FinTsMessageFrame Frame(string base64, Func<string, string>? transform)
    { string wire = Encoding.Latin1.GetString(Convert.FromBase64String(base64)); return ParseWire(transform is null ? wire : transform(wire)); }
    private static FinTsMessageFrame ParseWire(string wire)
    {
        int start = wire.IndexOf('+') + 1;
        wire = wire[..start] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[(start + 12)..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
}
