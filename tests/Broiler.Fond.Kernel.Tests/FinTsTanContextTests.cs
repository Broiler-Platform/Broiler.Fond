using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsTanContextTests
{
    private const string Permit = "HIRMS:2:2+3920::Reported methods:900+0030::Reported state";
    private const string Challenge = "HITAN:7:2+4++PUBLIC-ORDER+Public challenge";
    private static string Ad => "HITANS:7+1+1+0+N:N:0:" + string.Join(':', Procedure());

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        void Reject(Action action, FinTsSyntaxError category = FinTsSyntaxError.InvalidTanContext)
        {
            try { action(); Verify(false, "Invalid TAN context input must fail."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == category, "TAN context failure must use its expected fixed category.");
                Verify(!error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "TAN context errors must exclude input values.");
            }
        }
        FinTsTanChallengeEvidence Evaluate(string[]? parts = null, FinTsTanRequestContext? request = null, string messageReply = "0010", string dialog = "SYNTHETIC")
        {
            var response = Response(parts ?? [Permit, Ad, Challenge], messageReply, dialog);
            var parameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response));
            return FinTsTanChallengeEvidence.Evaluate(request ?? Request(), parameters, parameters.Advertisements[0].Procedures[0],
                FinTsPermittedProcedureSet.Parse(response), FinTsTanChallengeSet.Parse(response));
        }
        void Issue(FinTsTanChallengeEvidence evidence, FinTsTanContextIssue issue)
        {
            Verify(evidence.Issues.HasFlag(issue), $"Expected TAN context issue {issue}, found {evidence.Issues}.");
            Verify(evidence.Observation == FinTsTanOutcomeObservation.NeedsReview, "Any context issue must prevent a clean outcome observation.");
        }
        var baseline = Evaluate();
        Verify(baseline.Issues == FinTsTanContextIssue.None && baseline.Observation == FinTsTanOutcomeObservation.ChallengeReported, "A unique matching synthetic context reports a challenge only.");
        Verify(ReferenceEquals(baseline.Permissions.Source, baseline.Response.Source) && ReferenceEquals(baseline.Request.Segment, baseline.Request.Frame.Syntax.Segments[1]), "Evidence must retain the exact response and original request segment.");
        var initial = Evaluate(request: Request(dialog: "0"));
        Verify(initial.Issues == FinTsTanContextIssue.None, "Initial dialogue zero can bind one assigned nonzero response dialogue.");
        Issue(Evaluate(request: Request(expected: 2)), FinTsTanContextIssue.MessageMismatch);
        Issue(Evaluate(request: Request(message: 2)), FinTsTanContextIssue.MessageMismatch);
        Issue(Evaluate(dialog: "0"), FinTsTanContextIssue.MessageMismatch);
        Issue(Evaluate(dialog: "unbekannt"), FinTsTanContextIssue.MessageMismatch);
        Issue(Evaluate(dialog: "OTHER"), FinTsTanContextIssue.MessageMismatch);
        Issue(Evaluate([Ad, Challenge, "HIRMS:2:2+0030::Pending"]), FinTsTanContextIssue.MissingPermissionReport);
        Issue(Evaluate([Permit.Replace(":900", ":901", StringComparison.Ordinal), Ad, Challenge]), FinTsTanContextIssue.ProcedureNotListed);
        Issue(Evaluate([Permit.Replace(":900", ":900:900", StringComparison.Ordinal), Ad, Challenge]), FinTsTanContextIssue.AmbiguousPermissions);
        Issue(Evaluate([Permit + "+3920::Again:900", Ad, Challenge]), FinTsTanContextIssue.AmbiguousPermissions);
        Issue(Evaluate([Permit, Ad, Ad, Challenge]), FinTsTanContextIssue.AmbiguousProcedure);
        Issue(Evaluate([Permit, Ad + ":" + string.Join(':', Procedure()), Challenge]), FinTsTanContextIssue.AmbiguousProcedure);
        Issue(Evaluate([Permit, Ad]), FinTsTanContextIssue.MissingChallenge);
        Issue(Evaluate([Permit, Ad, "HITAN:99:2+opaque"]), FinTsTanContextIssue.MissingChallenge);
        Issue(Evaluate([Permit, Ad, Challenge, Challenge]), FinTsTanContextIssue.AmbiguousChallenge);
        Issue(Evaluate([Permit, Ad, Challenge, "HITAN:99:2+opaque"]), FinTsTanContextIssue.AmbiguousChallenge);
        Issue(Evaluate([Permit, Ad, Challenge.Replace(":7:2", ":6:2", StringComparison.Ordinal)]), FinTsTanContextIssue.VersionMismatch);
        Issue(Evaluate([Permit, Ad, Challenge.Replace("+4++", "+1++", StringComparison.Ordinal)]), FinTsTanContextIssue.ProcessMismatch);
        Issue(Evaluate([Permit, Ad, Challenge.Replace(":7:2", ":7:9", StringComparison.Ordinal)]), FinTsTanContextIssue.UnknownRequestReference);
        Issue(Evaluate([Permit, Ad, Challenge, "ZTEST:1:9+opaque"]), FinTsTanContextIssue.UnknownRequestReference);
        Issue(Evaluate(messageReply: "9800"), FinTsTanContextIssue.ParameterResponseNeedsReview | FinTsTanContextIssue.ResponseNeedsReview);
        foreach (string code in new[] { "3040", "9000", "3999" })
        { Issue(Evaluate([Permit + $"+{code}::Review", Ad, Challenge]), FinTsTanContextIssue.ResponseNeedsReview); }
        foreach (string status in new[] { "0010", "0020", "3956", "0030::Pending+0030", "0030::Pending+0020" })
        { Issue(Evaluate([Permit.Replace("0030::Reported state", status + "::Reported state", StringComparison.Ordinal), Ad, Challenge]), FinTsTanContextIssue.StatusNeedsReview); }
        Issue(Evaluate([Permit, "HIRMS:2:1+3076::Other request", Ad, Challenge]), FinTsTanContextIssue.StatusNeedsReview);
        Issue(Evaluate([Permit.Replace("0030::", "0030:1:", StringComparison.Ordinal), Ad, Challenge]), FinTsTanContextIssue.StatusNeedsReview);
        Issue(Evaluate([Permit, Ad, Challenge + "++20260906:120000"]), FinTsTanContextIssue.ExpiryNeedsReview);
        Issue(Evaluate([Permit, Ad, Challenge.Replace("PUBLIC-ORDER", "OTHER-ORDER", StringComparison.Ordinal)], Request("HKTAN:2:7+4+HKIDN+++PUBLIC-ORDER")), FinTsTanContextIssue.OrderReferenceMismatch);

        string Dummy(string reference, string text) => $"HITAN:7:2+4++{reference}+{text}";
        var exemption = Evaluate([Permit.Replace("0030", "3076", StringComparison.Ordinal), Ad, Dummy("noref", "nochallenge")]);
        Verify(exemption.Issues == FinTsTanContextIssue.None && exemption.Observation == FinTsTanOutcomeObservation.ExemptionReported, "A scoped 3076 plus exact dummy values is a reported exemption only.");
        Issue(Evaluate([Permit, Ad, Dummy("noref", "nochallenge")]), FinTsTanContextIssue.StatusNeedsReview);
        Issue(Evaluate([Permit.Replace("0030", "3076", StringComparison.Ordinal), Ad, Dummy("PUBLIC-ORDER", "nochallenge")]), FinTsTanContextIssue.DummyMismatch);
        Issue(Evaluate([Permit.Replace("0030", "3076", StringComparison.Ordinal), Ad, Dummy("noref", "Other text")]), FinTsTanContextIssue.DummyMismatch);
        Issue(Evaluate([Permit.Replace("0030", "3076", StringComparison.Ordinal), Ad, Dummy("noref", "nochallenge") + "+@1@x"]), FinTsTanContextIssue.DummyMismatch);
        Issue(Evaluate([Permit, Ad, Dummy("PUBLIC-ORDER", "nochallenge")]), FinTsTanContextIssue.StatusNeedsReview);

        string ChangeProcedure(int index, string value) { var values = Procedure(); values[index] = value; return "HITANS:7+1+1+0+N:N:0:" + string.Join(':', values); }
        Issue(Evaluate([Permit, ChangeProcedure(20, ""), Challenge]), FinTsTanContextIssue.MissingMedium);
        Verify(Evaluate([Permit, ChangeProcedure(20, ""), Challenge + "+++PUBLIC-MEDIUM"]).Issues == FinTsTanContextIssue.None, "Missing active-media count requires a response medium name.");
        var mediumRequest = Request("HKTAN:2:7+4+HKIDN+++++++++PUBLIC-MEDIUM");
        Issue(Evaluate([Permit, Ad, Challenge + "+++OTHER-MEDIUM"], mediumRequest), FinTsTanContextIssue.MediumMismatch);
        Verify(Evaluate([Permit, Ad, Challenge + "+++PUBLIC-MEDIUM"], mediumRequest).Issues == FinTsTanContextIssue.None, "Supplied medium names compare exactly.");
        Issue(Evaluate([Permit, ChangeProcedure(13, "2"), Challenge]), FinTsTanContextIssue.RequestOptionsNeedReview);
        var multipleMedia = Procedure(); multipleMedia[20] = "2"; multipleMedia[18] = "2";
        Issue(Evaluate([Permit, "HITANS:7+1+1+0+N:N:0:" + string.Join(':', multipleMedia), Challenge]), FinTsTanContextIssue.RequestOptionsNeedReview);

        var one = Procedure()[..21]; one[1] = "1"; one[11] = "4";
        string OneAd(string hash) => "HITANS:6+1+1+0+N:N:" + hash + ":" + string.Join(':', one);
        var hashRequest = Request("HKTAN:2:6+1+HKIDN++@3@abc++N");
        var hashMatch = Evaluate([Permit, OneAd("1"), "HITAN:6:2+1+@3@abc+PUBLIC-ORDER+Text"], hashRequest);
        Verify(hashMatch.Issues == FinTsTanContextIssue.None, "Required order hashes must mirror the supplied request bytes.");
        Issue(Evaluate([Permit, OneAd("1"), "HITAN:6:2+1+@3@xyz+PUBLIC-ORDER+Text"], hashRequest), FinTsTanContextIssue.HashMismatch);
        Issue(Evaluate([Permit, OneAd("0"), "HITAN:6:2+1+@3@abc+PUBLIC-ORDER+Text"], hashRequest), FinTsTanContextIssue.HashMismatch);
        Issue(Evaluate([Permit, OneAd("1"), "HITAN:6:2+1++PUBLIC-ORDER+Text"], Request("HKTAN:2:6+1+HKIDN++++N")), FinTsTanContextIssue.HashMismatch);
        one[15] = "J";
        Issue(Evaluate([Permit, OneAd("1"), "HITAN:6:2+1+@3@abc+PUBLIC-ORDER+Text"], hashRequest), FinTsTanContextIssue.RequestOptionsNeedReview);

        var freshResponse = Response([Permit, Ad, Challenge]);
        var freshParameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(freshResponse));
        Issue(FinTsTanChallengeEvidence.Evaluate(baseline.Request, baseline.Parameters, baseline.Procedure, FinTsPermittedProcedureSet.Parse(freshResponse), baseline.Response), FinTsTanContextIssue.ParameterScopeMismatch);
        Issue(FinTsTanChallengeEvidence.Evaluate(Request(dialog: "0"), baseline.Parameters, baseline.Procedure, baseline.Permissions, FinTsTanChallengeSet.Parse(freshResponse)), FinTsTanContextIssue.ParameterScopeMismatch);
        Reject(() => FinTsTanChallengeEvidence.Evaluate(baseline.Request, freshParameters, baseline.Procedure, baseline.Permissions, baseline.Response));
        foreach (string values in new[] { "", "899", "998", "1000", "0900", "9A0", "900::901" })
        { Reject(() => FinTsPermittedProcedureSet.Parse(Response(["HIRMS:2:2+3920::Reported methods:" + values]))); }
        Reject(() => FinTsPermittedProcedureSet.Parse(Response([], "3920::Methods:900")));
        Reject(() => FinTsPermittedProcedureSet.Parse(Response(["HIRMS:2:2+3920:1:Methods:900"])));
        var reported = FinTsPermittedProcedureSet.Parse(Response(["HIRMS:2:2+3920::Methods:900:997:999:"]));
        Verify(reported.Reports[0].SecurityFunctions.SequenceEqual(new[] { 900, 997, 999 }), "Ordered codes include the distinct one-step 999 and allow trailing omitted positions.");
        Verify(FinTsPermittedProcedureSet.Parse(Response(["HIRMS:2:2+3920::Methods:" + string.Join(':', Enumerable.Range(900, 10))])).Reports[0].SecurityFunctions.Count == 10, "Exactly ten reported functions fit the reply schema.");
        var reports128 = Enumerable.Range(1, 128).Select(i => $"HIRMS:2:{i}+3920::Methods:900").ToArray();
        Verify(FinTsPermittedProcedureSet.Parse(Response(reports128)).Reports.Count == 128, "Exactly 128 reports remain explicit.");
        Reject(() => FinTsPermittedProcedureSet.Parse(Response(reports128.Append("HIRMS:2:129+3920::Methods:900").ToArray())), FinTsSyntaxError.LimitExceeded);

        foreach (string bad in new[]
        {
            "HKTAN:2:5+4+HKIDN", "HKTAN:2:7:1+4+HKIDN", "HKTAN:2:7+2+HKIDN", "HKTAN:2:6+S++++PUBLIC-ORDER+N", "HKTAN:2:7+4+",
            "HKTAN:2:7+S+++++N", "HKTAN:2:7+1+HKIDN++++J", "HKTAN:2:7+4+HKIDN++++N", "HKTAN:2:7+1+HKIDN++text++N",
            "HKTAN:2:7+1+HKIDN++@0@++N", "HKTAN:2:7+4+HKIDN++@1@x", "HKTAN:2:7+4+HKIDN+ACCOUNT", "HKTAN:2:7+4+HKIDN++++++++++HHD",
            "HKTAN:2:7+4+HKIDN+++" + new string('x', 36), "HKTAN:2:7+4+HKIDN+++++++++" + new string('x', 33),
        }) { Reject(() => Request(bad)); }
        Reject(() => Request(expected: 0));
        Reject(() => Request(expected: 10000));
        Reject(() => FinTsTanRequestContext.Parse(Request().Frame, 3, 1));
        foreach (var action in new Action[]
        {
            () => FinTsPermittedProcedureSet.Parse(freshResponse, new CancellationToken(true)),
            () => FinTsTanRequestContext.Parse(baseline.Request.Frame, 2, 1, new CancellationToken(true)),
            () => FinTsTanChallengeEvidence.Evaluate(baseline.Request, baseline.Parameters, baseline.Procedure, baseline.Permissions, baseline.Response, new CancellationToken(true)),
        })
        {
            try { action(); Verify(false, "Cancelled TAN context work must stop."); }
            catch (OperationCanceledException) { Verify(true, "TAN context cancellation works."); }
        }
        Verify(new object[] { baseline, baseline.Request, baseline.Permissions, baseline.Permissions.Reports[0] }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default context diagnostics exclude raw request/challenge fields.");
        Verify(FinTsTanChallengeEvidence.Evaluate(baseline.Request, baseline.Parameters, baseline.Procedure, baseline.Permissions, baseline.Response).Observation == baseline.Observation,
            "Pure comparison does not consume a response or claim replay protection.");

        string outerWire = Encoding.Latin1.GetString(freshResponse.Frame.Syntax.CopyWireBytes());
        string inner = outerWire[(outerWire.IndexOf('\'') + 1)..outerWire.LastIndexOf("HNHBS", StringComparison.Ordinal)];
        foreach (int profile in new[] { 1, 2 })
        {
            string wrapper = $"HNVSK:998:3+PIN:{profile}+998+1+1::PUBLIC-SYSTEM+1+2:2:13:@8@00000000:5:1+280:10020030:PUBLIC-KEY:V:0:0+0'" +
                $"HNVSD:999:1+@{inner.Length}@{inner}'";
            var wrappedResponse = FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Wire(wrapper, 6, "SYNTHETIC", 1, true))));
            var wrappedParameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(wrappedResponse));
            var wrappedEvidence = FinTsTanChallengeEvidence.Evaluate(Request(), wrappedParameters, wrappedParameters.Advertisements[0].Procedures[0],
                FinTsPermittedProcedureSet.Parse(wrappedResponse), FinTsTanChallengeSet.Parse(wrappedResponse));
            if (profile == 1) { Issue(wrappedEvidence, FinTsTanContextIssue.ProfileMismatch); }
            else { Verify(wrappedEvidence.Issues == FinTsTanContextIssue.None, "PIN:2 structural envelopes retain matching context evidence without authentication."); }
        }

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.tan-context-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (var vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] requestWire = Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!);
            byte[] responseWire = Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!);
            var request = FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(requestWire), 2, 1);
            var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(responseWire));
            var parameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response));
            var evidence = FinTsTanChallengeEvidence.Evaluate(request, parameters, parameters.Advertisements[0].Procedures[0], FinTsPermittedProcedureSet.Parse(response), FinTsTanChallengeSet.Parse(response));
            var expected = FinTsTanContextIssue.None;
            foreach (var issue in vector.GetProperty("issues").EnumerateArray()) { expected |= Enum.Parse<FinTsTanContextIssue>(issue.GetString()!); }
            Verify(evidence.Issues == expected, "Independent context issue sets must match exactly.");
            Verify(evidence.Observation.ToString() == vector.GetProperty("observation").GetString(), "Independent outcome observations must match.");
            Verify(evidence.Request.Frame.Syntax.CopyWireBytes().SequenceEqual(requestWire) && evidence.Response.Source.Frame.Syntax.CopyWireBytes().SequenceEqual(responseWire), "Independent request and response bytes survive exactly.");
        }
        Console.WriteLine($"FinTS permitted procedures/request-bound challenge evidence: {vectors} independent vectors and {count} checks completed.");
    }
    private static string[] Procedure() => ["900", "2", "PUBLIC_METHOD", "App", "1.0", "Public method", "6", "1", "Approval", "2048", "N", "1", "N", "0", "0", "N", "J", "00", "0", "N", "0", "", "", "", "", ""];
    private static FinTsTanRequestContext Request(string segment = "HKTAN:2:7+4+HKIDN", int expected = 1, string dialog = "SYNTHETIC", int message = 1) =>
        FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(Wire(segment + "'", 3, dialog, message, false)), 2, expected);
    private static FinTsResponse Response(string[] parts, string messageReply = "0010", string dialog = "SYNTHETIC")
    {
        string body = "HIRMG:2:2+" + (messageReply.Contains(':', StringComparison.Ordinal) ? messageReply : messageReply + "::Synthetic") + "'" +
            string.Concat(parts.Select((part, index) => part.Insert(part.IndexOf(':') + 1, (index + 3).ToString(CultureInfo.InvariantCulture) + ":") + "'"));
        return FinTsResponse.Parse(FinTsMessageFrame.Parse(Wire(body, parts.Length + 3, dialog, 1, true)));
    }
    private static byte[] Wire(string body, int trailer, string dialog, int message, bool response)
    {
        string wire = $"HNHBK:1:3+000000000000+300+{dialog}+{message}" + (response ? $"+{dialog}:1" : "") + "'" + body + $"HNHBS:{trailer}:1+{message}'";
        return Encoding.Latin1.GetBytes(wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}
