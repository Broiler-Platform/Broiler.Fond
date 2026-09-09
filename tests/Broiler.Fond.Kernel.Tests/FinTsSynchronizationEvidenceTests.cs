using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSynchronizationEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Has(FinTsSynchronizationEvidence result, FinTsSynchronizationIssue issue) => Verify(result.Issues.HasFlag(issue) && !result.HasMatchingEvidence && result.MatchingReport is null && result.NextStep == FinTsSynchronizationNextStep.StopForReview, "Unresolved synchronization evidence selects no report: " + issue);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.synchronization-context-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            var request = Request(v); var response = Data(v); var fixturePrior = Recovery(v);
            var result = FinTsSynchronizationEvidence.Evaluate(request, response, Enum.Parse<FinTsSynchronizationProfile>(v.GetProperty("profile").GetString()!), fixturePrior);
            var expected = v.GetProperty("issues").EnumerateArray().Aggregate(FinTsSynchronizationIssue.None, (flags, value) => flags | Enum.Parse<FinTsSynchronizationIssue>(value.GetString()!));
            Verify(result.Issues == expected, $"Independent synchronization issues differ in {v.GetProperty("name").GetString()}: {result.Issues}.");
            Verify(result.NextStep.ToString() == v.GetProperty("nextStep").GetString() && result.HasMatchingEvidence == (expected == FinTsSynchronizationIssue.None), "Independent next-step and matching expectations agree.");
            Verify(ReferenceEquals(result.Request, request) && ReferenceEquals(result.Response, response) && ReferenceEquals(result.RecoveryContext, fixturePrior), "Exact caller context and response sources are retained.");
            Verify(result.HasMatchingEvidence ? ReferenceEquals(result.MatchingReport, response.Reports[0]) : result.MatchingReport is null, "Only a unique fully matching report is selected.");
            Verify(result.ReportedDialogueId == "SYNTHETIC", "Reported dialogue remains an untrusted observation on all outcomes.");
        }
        Verify(vectors.Length == 15, "All fifteen independent synchronization-context fixtures ran.");
        var sample = vectors[0]; var requestBase = Request(sample); var responseBase = Data(sample);
        FinTsSynchronizationEvidence Evaluate(Func<string, string>? edit = null, FinTsSynchronizationProfile profile = FinTsSynchronizationProfile.PinTan2) => FinTsSynchronizationEvidence.Evaluate(requestBase, Data(sample, edit), profile);
        var clean = Evaluate();
        Verify(clean.NextStep == FinTsSynchronizationNextStep.CloseAndReinitializeRequired && clean.MatchingReport!.SystemId == "PUBLIC+:'?@ü", "Matching system-ID observations require closing and reinitializing, with exact escaped source identity.");
        Verify(Evaluate(profile: FinTsSynchronizationProfile.PinTan1).HasMatchingEvidence, "Both explicitly selected PIN profiles can describe system-ID synchronization.");
        foreach (var profile in new[] { FinTsSynchronizationProfile.Unspecified, (FinTsSynchronizationProfile)999, (FinTsSynchronizationProfile)(-1) })
        { Has(Evaluate(profile: profile), FinTsSynchronizationIssue.ProfileNeedsReview); }
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("+1+SYNTHETIC:1'", "+1'", StringComparison.Ordinal),
            s => s.Replace("+1+SYNTHETIC:1'", "+1+'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "OTHER:1'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "SYNTHETIC:2'", StringComparison.Ordinal),
            s => s.Replace("+300+SYNTHETIC+1+", "+300+SYNTHETIC+2+", StringComparison.Ordinal).Replace("HNHBS:5:1+1'", "HNHBS:5:1+2'", StringComparison.Ordinal),
        }) { Has(Evaluate(edit), FinTsSynchronizationIssue.MessageMismatch); }
        foreach (string dialogue in new[] { "0", "unbekannt", " PADDED", "PADDED " })
        { Has(Evaluate(s => s.Replace("SYNTHETIC", dialogue, StringComparison.Ordinal)), FinTsSynchronizationIssue.InvalidAssignedDialogue); }
        foreach (string reference in new[] { "1", "2", "3", "5", "999" })
        { Has(Evaluate(s => s.Replace("HISYN:4:4:4", "HISYN:4:4:" + reference, StringComparison.Ordinal)), FinTsSynchronizationIssue.ReferenceMismatch); }
        Has(Evaluate(s => s.Replace("HIRMS:3:2:4", "HIRMS:3:2:5", StringComparison.Ordinal)), FinTsSynchronizationIssue.ReferenceMismatch);
        foreach (string system in new[] { "0", "unbekannt" })
        { Has(Evaluate(s => ReplaceReport(s, system)), FinTsSynchronizationIssue.InvalidSystemId); }
        Has(Evaluate(s => ReplaceReport(s, "")), FinTsSynchronizationIssue.ModeShapeMismatch);
        foreach (string fields in new[] { "PUBLIC-SYSTEM+1", "+1", "++1", "+++1" })
        { Has(Evaluate(s => ReplaceReport(s, fields)), FinTsSynchronizationIssue.ModeShapeMismatch); }
        foreach (string status in new[] { "0010", "0030", "3040", "3050", "3920", "9000", "9050", "7777" })
        { Has(Evaluate(s => s.Replace("0020::PUBLIC-SYNC-REPLY", status + "::PUBLIC-SYNC-REPLY", StringComparison.Ordinal)), FinTsSynchronizationIssue.StatusNeedsReview); }
        foreach (string status in new[] { "0020:1:PUBLIC", "0020::PUBLIC:PUBLIC-SECRET", "0020::PUBLIC+0020::PUBLIC" })
        { Has(Evaluate(s => s.Replace("0020::PUBLIC-SYNC-REPLY", status, StringComparison.Ordinal)), FinTsSynchronizationIssue.StatusNeedsReview); }
        Has(Evaluate(s => s.Replace("0010::PUBLIC-REPLY", "9050::PUBLIC-REPLY", StringComparison.Ordinal)), FinTsSynchronizationIssue.ResponseNeedsReview);
        Has(Evaluate(s => s.Replace("HIRMS:3:2:4", "HIRMS:3:2:2", StringComparison.Ordinal)), FinTsSynchronizationIssue.StatusNeedsReview);
        Verify(Evaluate(s => s.Replace("0010::PUBLIC-REPLY", "0020::PUBLIC-REPLY", StringComparison.Ordinal).Replace("HIRMS:3:2:4", "HIRMS:3:2:2", StringComparison.Ordinal)).HasMatchingEvidence, "Message execution can cover the synchronization request while identification status alone cannot.");
        Verify(Evaluate(s => s.Replace("0020::PUBLIC-SYNC-REPLY", "0020::PUBLIC-SYNC-REPLY:", StringComparison.Ordinal)).HasMatchingEvidence, "Empty trailing status parameters remain omissions.");
        var signature = vectors[4]; var signRequest = Request(signature);
        foreach (string fields in new[] { "++9999999999999999", "++1+9999999999999999" })
        { Has(FinTsSynchronizationEvidence.Evaluate(signRequest, Data(signature, s => ReplaceReport(s, fields)), FinTsSynchronizationProfile.Rah10), FinTsSynchronizationIssue.ReservedSecurityReference); }
        Has(FinTsSynchronizationEvidence.Evaluate(signRequest, Data(signature, s => ReplaceReport(s, "++1+2")), FinTsSynchronizationProfile.Rah10), FinTsSynchronizationIssue.SignatureLayoutMismatch);
        var card = vectors[5];
        Has(FinTsSynchronizationEvidence.Evaluate(Request(card), Data(card, s => ReplaceReport(s, "++1")), FinTsSynchronizationProfile.Rah7), FinTsSynchronizationIssue.SignatureLayoutMismatch);
        Has(FinTsSynchronizationEvidence.Evaluate(signRequest, Data(signature), FinTsSynchronizationProfile.Rah7), FinTsSynchronizationIssue.RequestSystemMismatch);
        Has(FinTsSynchronizationEvidence.Evaluate(Request(card), Data(card), FinTsSynchronizationProfile.Rah10), FinTsSynchronizationIssue.RequestSystemMismatch);
        var message = vectors[2]; var messageRequest = Request(message); var prior = Recovery(message)!;
        Has(FinTsSynchronizationEvidence.Evaluate(messageRequest, Data(message), FinTsSynchronizationProfile.PinTan2, new("SYNTHETIC", 9999)), FinTsSynchronizationIssue.RecoveryContextMismatch);
        Has(FinTsSynchronizationEvidence.Evaluate(requestBase, responseBase, FinTsSynchronizationProfile.PinTan2, prior), FinTsSynchronizationIssue.RecoveryContextMismatch);
        Verify(FinTsSynchronizationEvidence.Evaluate(messageRequest, Data(message, s => ReplaceReport(s, "+1")), FinTsSynchronizationProfile.PinTan2, prior).MatchingReport!.LastMessageNumber == 1, "Earlier processed-message observations remain exact without inferring which operations executed.");
        foreach (string invalid in new[] { "", "0", "unbekannt", " padded", "PUBLIC-SECRET\n", "€", new string('x', 31) })
        {
            try { _ = new FinTsSynchronizationRecoveryContext(invalid, 1); Verify(false, "Invalid prior dialogue must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSynchronization && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Recovery-context validation uses fixed diagnostics."); }
        }
        foreach (int number in new[] { -1, 0, 10000, int.MaxValue })
        {
            try { _ = new FinTsSynchronizationRecoveryContext("PUBLIC-PREVIOUS", number); Verify(false, "Invalid prior message bound must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSynchronization, "Prior message bound is explicit and bounded."); }
        }
        Has(FinTsSynchronizationEvidence.Evaluate(requestBase, Wrapped(responseBase), FinTsSynchronizationProfile.PinTan2), FinTsSynchronizationIssue.EnvelopeNeedsReview);
        Has(Evaluate(s => s.Replace("HNHBS:5:1", "ZNEW:5:1:3+PUBLIC'HNHBS:6:1", StringComparison.Ordinal)), FinTsSynchronizationIssue.UninterpretedReports);
        Has(Evaluate(s => s.Replace("HNHBS:5:1", "HIBPA:5:3:3+1+280:PUBLIC-BANK+PUBLIC+1+1+300'HNHBS:6:1", StringComparison.Ordinal)), FinTsSynchronizationIssue.UninterpretedReports);
        var maximum = Data(sample, s =>
        {
            int start = s.IndexOf("HISYN:4:", StringComparison.Ordinal), end = s.IndexOf("HNHBS:", StringComparison.Ordinal);
            string report = s[start..end];
            return s[..start] + string.Concat(Enumerable.Range(4, 128).Select(n => report.Replace("HISYN:4:", "HISYN:" + n.ToString(CultureInfo.InvariantCulture) + ":", StringComparison.Ordinal))) + "HNHBS:132:1+1'";
        });
        var bounded = FinTsSynchronizationEvidence.Evaluate(requestBase, maximum, FinTsSynchronizationProfile.PinTan2);
        Has(bounded, FinTsSynchronizationIssue.DuplicateReport);
        Verify(bounded.Response.Reports.Count == 128 && ReferenceEquals(bounded.Response.Reports[127], maximum.Reports[127]), "Every bounded duplicate remains available without selecting a winner.");
        Verify(FinTsSynchronizationEvidence.Evaluate(requestBase, responseBase, FinTsSynchronizationProfile.PinTan2).HasMatchingEvidence && clean.HasMatchingEvidence, "Repeated comparison consumes no response or recovery state.");
        Verify(new object[] { clean, prior }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default context/evidence diagnostics exclude private identities.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FinTsSynchronizationEvidence.Evaluate(requestBase, maximum, FinTsSynchronizationProfile.PinTan2, cancellationToken: cancelled.Token); Verify(false, "Cancelled comparison must return no evidence."); }
        catch (OperationCanceledException) { Verify(true, "Cancellation propagates."); }
        foreach (Action action in new Action[] { () => FinTsSynchronizationEvidence.Evaluate(null!, responseBase, FinTsSynchronizationProfile.PinTan2), () => FinTsSynchronizationEvidence.Evaluate(requestBase, null!, FinTsSynchronizationProfile.PinTan2), () => new FinTsSynchronizationRecoveryContext(null!, 1) })
        {
            try { action(); Verify(false, "Null inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null inputs rejected."); }
        }
        Console.WriteLine($"FinTS synchronization context verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsUnsignedSynchronizationRequest Request(JsonElement v) => FinTsUnsignedSynchronizationRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty("requestBase64").GetString()!)));
    private static FinTsSynchronizationRecoveryContext? Recovery(JsonElement v) => v.GetProperty("previousDialogueId").GetString() is { } prior ? new(prior, v.GetProperty("lastSubmittedMessageNumber").GetInt32()) : null;
    private static FinTsSynchronizationDataSet Data(JsonElement v, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(v.GetProperty("responseBase64").GetString()!));
        return FinTsSynchronizationDataSet.Parse(FinTsResponse.Parse(Frame(edit is null ? wire : edit(wire))));
    }
    private static FinTsMessageFrame Frame(string wire) => FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
    private static string ReplaceReport(string wire, string fields)
    {
        int start = wire.IndexOf("HISYN:4:4:4", StringComparison.Ordinal), end = wire.IndexOf("HNHBS:", StringComparison.Ordinal);
        return wire[..start] + "HISYN:4:4:4" + (fields.Length == 0 ? "" : "+" + fields) + "'" + wire[end..];
    }
    private static FinTsSynchronizationDataSet Wrapped(FinTsSynchronizationDataSet source)
    {
        string plain = Encoding.Latin1.GetString(source.Source.Frame.Syntax.CopyWireBytes());
        int headerEnd = plain.IndexOf('\'') + 1, trailerStart = plain.LastIndexOf("HNHBS:", StringComparison.Ordinal);
        string body = plain[headerEnd..trailerStart];
        string wrapper = "HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1:20260908:120000+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:10020030:PUBLIC-KEY-ID:V:0:0+0'";
        string wire = plain[..headerEnd] + wrapper + "HNVSD:999:1+@" + Encoding.Latin1.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "@" + body + "'" + plain[trailerStart..];
        return FinTsSynchronizationDataSet.Parse(FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(Frame(wire))));
    }
}
