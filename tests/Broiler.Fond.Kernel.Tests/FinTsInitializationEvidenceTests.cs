using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsInitializationEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Has(FinTsInitializationEvidence result, FinTsInitializationIssue issue) => Verify(result.Issues.HasFlag(issue) && !result.HasMatchingEvidence && result.Outcome == FinTsInitializationOutcome.NeedsReview, "Unresolved initialization evidence requires review: " + issue);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.initialization-context-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var request = Request(vector); var parameters = Parameters(vector);
            string? user = vector.GetProperty("expectedUserId").GetString();
            var result = FinTsInitializationEvidence.Evaluate(request, parameters, user);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsInitializationIssue.None, (flags, value) => flags | Enum.Parse<FinTsInitializationIssue>(value.GetString()!));
            Verify(result.Issues == expected, $"Independent initialization scope differs for {vector.GetProperty("name").GetString()}: {result.Issues}.");
            Verify(result.Outcome.ToString() == vector.GetProperty("outcome").GetString() && result.HasMatchingEvidence == (expected == FinTsInitializationIssue.None), "Independent outcome expectations match.");
            Verify(ReferenceEquals(request, result.Request) && ReferenceEquals(parameters, result.Parameters) && ReferenceEquals(parameters.Source, result.Response) && result.ExpectedUserId == user, "Comparison retains exactly the supplied request, response parameters and explicit user expectation.");
            Verify(result.ReportedDialogueId == "SYNTHETIC", "Reported dialogue remains visible on matching and review outcomes.");
        }
        Verify(vectors.Length == 14, "All fourteen independent initialization-context fixtures ran.");
        var basis = vectors[0]; var initial = Request(basis); var original = Parameters(basis);
        FinTsInitializationEvidence Evaluate(Func<string, string>? edit = null, string? user = "PUBLIC-USER") => FinTsInitializationEvidence.Evaluate(initial, Parameters(basis, edit), user);
        var clean = FinTsInitializationEvidence.Evaluate(initial, original, "PUBLIC-USER");
        Verify(clean.HasMatchingEvidence && clean.BankVersionChanged == false && clean.UserVersionChanged == false && initial.Identification.CustomerId != clean.ExpectedUserId, "Distinct user/customer identities and unchanged returned versions can match.");
        Has(Evaluate(user: null), FinTsInitializationIssue.UserContextMissing);
        Has(Evaluate(user: "PUBLIC-CUSTOMER"), FinTsInitializationIssue.UserMismatch);
        Has(Evaluate(user: "public-user"), FinTsInitializationIssue.UserMismatch);
        foreach (string invalid in new[] { "", " PUBLIC-USER", "PUBLIC-USER ", "PUBLIC-SECRET\n", "€", "\ud800", new string('X', 31) })
        {
            try { Evaluate(user: invalid); Verify(false, "Invalid explicit user context must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidInitialization && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "User-context errors are bounded and fixed."); }
        }
        foreach (string dialogue in new[] { "0", "unbekannt", " PADDED", "PADDED " })
        {
            var result = Evaluate(s => s.Replace("SYNTHETIC", dialogue, StringComparison.Ordinal));
            Has(result, FinTsInitializationIssue.InvalidAssignedDialogue);
            Verify(result.ReportedDialogueId == dialogue, "Invalid assignment remains source evidence without activation.");
        }
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("+1+SYNTHETIC:1'", "+1'", StringComparison.Ordinal),
            s => s.Replace("+1+SYNTHETIC:1'", "+1+'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "SYNTHETIC:2'", StringComparison.Ordinal),
            s => s.Replace("+300+SYNTHETIC+1+", "+300+SYNTHETIC+2+", StringComparison.Ordinal).Replace("HNHBS:8:1+1'", "HNHBS:8:1+2'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "0:1'", StringComparison.Ordinal),
        }) { Has(Evaluate(edit), FinTsInitializationIssue.MessageMismatch); }
        foreach (string prefix in new[] { "HIBPA:5:3", "HIUPA:6:4", "HIUPD:7:6" })
        {
            Has(Evaluate(s => s.Replace(prefix + ":3", prefix + ":2", StringComparison.Ordinal)), FinTsInitializationIssue.ReferenceMismatch);
            Has(Evaluate(s => s.Replace(prefix + ":3", prefix, StringComparison.Ordinal)), FinTsInitializationIssue.ReferenceMismatch);
        }
        Has(Evaluate(s => s.Replace("HIRMS:3:2:2", "HIRMS:3:2:4", StringComparison.Ordinal)), FinTsInitializationIssue.ReferenceMismatch);
        Has(Evaluate(s => s.Replace("+1+1:2:3+300'", "+1+1:3+300'", StringComparison.Ordinal)), FinTsInitializationIssue.LanguageNotAdvertised);
        Has(Evaluate(s => s.Replace("+300'H", "+220'H", StringComparison.Ordinal)), FinTsInitializationIssue.ProtocolNotAdvertised);
        var changed = FinTsInitializationEvidence.Evaluate(Request(vectors[10]), Parameters(vectors[10]), "PUBLIC-USER");
        Verify(changed.HasMatchingEvidence && changed.BankVersionChanged == true && changed.UserVersionChanged == true && changed.Response.HasUninterpretedCodes, "Scoped 3050 records an update without broadening generic reply meanings.");
        var zero = FinTsInitializationEvidence.Evaluate(Request(vectors[2]), Parameters(vectors[2]), "PUBLIC-USER");
        Verify(zero.HasMatchingEvidence && zero.Parameters.User!.IsDialogueScoped && zero.UserVersionChanged == false, "UPD version zero remains dialogue-scoped and is never promoted to reusable cache state.");
        var omitted = FinTsInitializationEvidence.Evaluate(Request(vectors[3]), Parameters(vectors[3]), "PUBLIC-USER");
        Verify(!omitted.HasMatchingEvidence && omitted.BankVersionChanged is null && omitted.UserVersionChanged is null, "Omitted cached versions remain unknown and require separate cache evidence.");
        var lower = Evaluate(s => s.Replace("HIBPA:5:3:3+1+", "HIBPA:5:3:3+0+", StringComparison.Ordinal).Replace("HIUPA:6:4:3+PUBLIC-USER+2+", "HIUPA:6:4:3+PUBLIC-USER+1+", StringComparison.Ordinal));
        Verify(lower.HasMatchingEvidence && lower.BankVersionChanged == true && lower.UserVersionChanged == true, "Version comparisons record changes without inventing monotonic freshness semantics.");
        var guest = Request(vectors[1]);
        var guestParameters = Parameters(basis, s => s.Replace("PUBLIC-USER", new string('9', 30), StringComparison.Ordinal).Replace("PUBLIC-CUSTOMER", "9999999999", StringComparison.Ordinal));
        Verify(FinTsInitializationEvidence.Evaluate(guest, guestParameters).HasMatchingEvidence, "Anonymous UPD preserves the guest user namespace and customer marker without linking identified accounts.");
        Has(FinTsInitializationEvidence.Evaluate(guest, original), FinTsInitializationIssue.UserMismatch);
        Has(FinTsInitializationEvidence.Evaluate(guest, original), FinTsInitializationIssue.CustomerMismatch);
        Has(FinTsInitializationEvidence.Evaluate(guest, guestParameters, "OTHER"), FinTsInitializationIssue.UserMismatch);
        var statusVariants = new[] { "0030", "3040", "3920", "9999", "7777" };
        foreach (string code in statusVariants)
        { Has(Evaluate(s => s.Replace("0020::PUBLIC-PREP-REPLY", code + "::PUBLIC-PREP-REPLY", StringComparison.Ordinal)), FinTsInitializationIssue.StatusNeedsReview); }
        foreach (string reply in new[]
        {
            "0020:1:PUBLIC-PREP-REPLY", "0020::PUBLIC-PREP-REPLY:PUBLIC-SECRET",
            "0020::PUBLIC-PREP-REPLY+0020::PUBLIC-DUPLICATE",
            "0020::PUBLIC-PREP-REPLY+3050::PUBLIC-UPDATE:PUBLIC-SECRET",
            "0020::PUBLIC-PREP-REPLY+3050:1:PUBLIC-UPDATE",
            "0020::PUBLIC-PREP-REPLY+3050::PUBLIC-UPDATE+3050::PUBLIC-UPDATE",
        }) { Has(Evaluate(s => s.Replace("0020::PUBLIC-PREP-REPLY", reply, StringComparison.Ordinal)), FinTsInitializationIssue.StatusNeedsReview); }
        Has(Evaluate(s => s.Replace("0010::PUBLIC-REPLY", "3050::PUBLIC-REPLY", StringComparison.Ordinal)), FinTsInitializationIssue.StatusNeedsReview);
        Has(Evaluate(s => s.Replace("0020::PUBLIC-ID-REPLY", "3050::PUBLIC-ID-REPLY", StringComparison.Ordinal)), FinTsInitializationIssue.StatusNeedsReview);
        var noUpdates = Parameters(vectors[3], s => s.Replace("0020::PUBLIC-PREP-REPLY", "0020::PUBLIC-PREP-REPLY+3050::PUBLIC-UPDATE", StringComparison.Ordinal));
        Has(FinTsInitializationEvidence.Evaluate(initial, noUpdates, "PUBLIC-USER"), FinTsInitializationIssue.StatusNeedsReview);
        // A message execution report can cover omitted segment statuses, while receipt alone cannot.
        var messageOnly = Parameters(vectors[12], s => s.Replace("0010::PUBLIC-REPLY", "0020::PUBLIC-REPLY", StringComparison.Ordinal));
        Verify(FinTsInitializationEvidence.Evaluate(initial, messageOnly, "PUBLIC-USER").HasMatchingEvidence, "A clean message-level execution observation covers the initialization scope.");
        var wrapped = Wrapped(original.Source);
        Has(FinTsInitializationEvidence.Evaluate(initial, FinTsParameterSet.Parse(wrapped), "PUBLIC-USER"), FinTsInitializationIssue.ProfileNeedsReview);
        var maximum = Parameters(basis, s =>
        {
            int start = s.IndexOf("HIUPD:7:", StringComparison.Ordinal), end = s.IndexOf("HNHBS:", StringComparison.Ordinal);
            string account = s[start..end];
            return s[..start] + string.Concat(Enumerable.Range(7, 512).Select(n => account.Replace("HIUPD:7:", "HIUPD:" + n.ToString(CultureInfo.InvariantCulture) + ":", StringComparison.Ordinal))) + "HNHBS:519:1+1'";
        });
        var many = FinTsInitializationEvidence.Evaluate(initial, maximum, "PUBLIC-USER");
        Verify(many.HasMatchingEvidence && many.Parameters.Accounts.Count == 512 && ReferenceEquals(many.Parameters.Accounts[511], maximum.Accounts[511]), "The scope comparator preserves all 512 entries without deduplication or completeness inference.");
        Verify(FinTsInitializationEvidence.Evaluate(initial, original, "PUBLIC-USER").HasMatchingEvidence && clean.HasMatchingEvidence, "Repeated pure comparisons consume no response or replay state.");
        Verify(!clean.ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default evidence diagnostics exclude private scope identifiers.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FinTsInitializationEvidence.Evaluate(initial, maximum, "PUBLIC-USER", cancelled.Token); Verify(false, "Cancelled comparison must not return evidence."); }
        catch (OperationCanceledException) { Verify(true, "Cancellation propagates."); }
        foreach (Action action in new Action[] { () => FinTsInitializationEvidence.Evaluate(null!, original), () => FinTsInitializationEvidence.Evaluate(initial, null!) })
        {
            try { action(); Verify(false, "Null source inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null sources are rejected."); }
        }
        Console.WriteLine($"FinTS initialization response/scope verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsUnsignedInitializationRequest Request(JsonElement vector) => FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)));
    private static FinTsParameterSet Parameters(JsonElement vector, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        return FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(edit is null ? wire : edit(wire))));
    }
    private static FinTsMessageFrame Frame(string value)
    {
        string size = Encoding.Latin1.GetByteCount(value).ToString("D12", CultureInfo.InvariantCulture);
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(value[..10] + size + value[22..]));
    }
    private static FinTsResponse Wrapped(FinTsResponse source)
    {
        string plain = Encoding.Latin1.GetString(source.Frame.Syntax.CopyWireBytes());
        int headerEnd = plain.IndexOf('\'') + 1, trailerStart = plain.LastIndexOf("HNHBS:", StringComparison.Ordinal);
        string body = plain[headerEnd..trailerStart];
        string wrapper = "HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1:20260907:120000+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:10020030:PUBLIC-KEY-ID:V:0:0+0'";
        string wire = plain[..headerEnd] + wrapper + "HNVSD:999:1+@" + Encoding.Latin1.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "@" + body + "'" + plain[trailerStart..];
        return FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(Frame(wire)));
    }
}
