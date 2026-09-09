using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsTanSchemaTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        void Reject(Action action, FinTsSyntaxError? category = null)
        {
            try { action(); Verify(false, "Malformed TAN evidence must fail."); }
            catch (FinTsFormatException error)
            {
                Verify(category is null || error.Error == category, $"TAN schema category mismatch at check {count + 1}: {error.Error} versus {category}.");
                Verify(!error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "TAN schema errors must exclude input values.");
            }
        }
        foreach (int version in new[] { 6, 7 })
        {
            var result = Parameters(Ad(version, Procedure(version)));
            var advertisement = result.Advertisements.Single();
            var procedure = advertisement.Procedures.Single();
            Verify(procedure.SegmentVersion == version && procedure.SecurityFunction == 900 && procedure.ProcessVariant == "2", "Procedure identity remains tied to its exact segment version.");
            Verify(Text(procedure.TechnicalId) == "PUBLIC_METHOD" && Text(procedure.Method) == "App" && Text(procedure.MethodVersion) == "1.0" && Text(procedure.Name) == "Public method", "Raw procedure identifiers, versions and names must survive.");
            Verify(procedure.MaximumTanLength == 6 && procedure.InputFormat == "1" && procedure.MaximumReturnValueLength == 2048 && Text(procedure.ReturnValueLabel) == "Approval", "Input and return constraints remain distinct.");
            Verify(!procedure.MultipleTanAllowed && procedure.TimeAndDialogueScope == "1" && !procedure.CancellationAllowed &&
                procedure.SmsAccountRequirement == "0" && procedure.OrderingAccountRequirement == "0", "Procedure scope flags must retain independent meanings.");
            Verify(!procedure.ChallengeClassRequired && procedure.StructuredChallenge && procedure.InitializationMode == "00" &&
                procedure.MediumNameRequirement == "0" && !procedure.HhdResponseRequired && procedure.ActiveMediaCount == 0, "Display, initialization and medium fields must retain exact values.");
            Verify(!procedure.IsDecoupled && procedure.MaximumStatusQueries is null && procedure.FirstQueryDelaySeconds is null &&
                procedure.SubsequentQueryDelaySeconds is null && procedure.ManualConfirmationAllowed is null && procedure.AutomatedQueriesAllowed is null, "Absent polling fields must not become zero or false.");
            Verify(advertisement.MaximumOrders == 1 && advertisement.MinimumSignatures == 1 && advertisement.SecurityClass == 0 &&
                !advertisement.OneStepReportedAllowed && !advertisement.MultipleTanOrdersReportedAllowed && advertisement.OrderHashAlgorithm == "0", "Common TAN parameters remain evidence only.");
            Verify(Parameters(Ad(version, Procedure(version)[..20])).Advertisements[0].Procedures[0].ActiveMediaCount is null, "Trailing optional fields can be omitted in a final procedure.");
        }
        var decoupled = Parameters(Ad(7, Procedure(7, "Decoupled"))).Advertisements[0].Procedures[0];
        Verify(decoupled.IsDecoupled && decoupled.MaximumTanLength is null && decoupled.InputFormat is null && decoupled.MaximumStatusQueries == 10 &&
            decoupled.FirstQueryDelaySeconds == 2 && decoupled.SubsequentQueryDelaySeconds == 5 && decoupled.ManualConfirmationAllowed == false && decoupled.AutomatedQueriesAllowed == true,
            "Decoupled approval must retain polling bounds without expecting a TAN input.");
        var push = Parameters(Ad(7, Procedure(7, "DecoupledPush"))).Advertisements[0].Procedures[0];
        Verify(push.IsDecoupled && push.MaximumStatusQueries is null, "Push approval cannot inherit polling parameters.");
        Verify(Parameters(Ad(7, Procedure(7, "Decoupled")[..24])).Advertisements[0].Procedures[0].AutomatedQueriesAllowed is null, "Omitted polling permission remains unknown.");
        var zero = Procedure(7, "Decoupled");
        zero[21] = zero[22] = zero[23] = "0";
        Verify(Parameters(Ad(7, zero)).Advertisements[0].Procedures[0].MaximumStatusQueries == 0, "An explicit zero query limit is preserved without scheduling a query.");
        var duplicate = Parameters(Ad(6, Procedure(6).Concat(Procedure(6)).ToArray()), Ad(6, Procedure(6)), "HITANS:99+opaque");
        Verify(duplicate.HasDuplicateAdvertisementVersions && duplicate.Advertisements[0].HasDuplicateSecurityFunctions && duplicate.UninterpretedSegments.Count == 1,
            "Duplicates and unknown versions must remain visible without selecting a winner.");
        Verify(Parameters("HITANS:5+opaque", "HITANS:8+opaque").Advertisements.Count == 0, "Neither older nor future schemas silently fall back to 6/7.");
        foreach (var mutation in new (int Index, string Value)[]
        {
            (0, "899"), (0, "998"), (0, "0900"), (1, "S"), (2, ""), (2, new string('x', 31)), (3, new string('x', 33)),
            (4, new string('x', 11)), (5, ""), (5, new string('x', 31)), (6, ""), (6, "100"), (6, "06"), (7, "0"),
            (8, ""), (8, new string('x', 31)), (9, "0"), (9, "2049"), (10, "X"), (11, "0"), (12, "X"),
            (13, "3"), (14, "1"), (15, "X"), (16, "X"), (17, "0"), (17, "03"), (18, "3"), (19, "X"),
            (20, "10"), (20, "@0@"), (21, "1"), (5, "PUBLIC\r"), (3, "@1@A"),
        })
        {
            var values = Procedure(7); values[mutation.Index] = mutation.Value;
            Reject(() => Parameters(Ad(7, values)), FinTsSyntaxError.InvalidParameters);
        }
        foreach (var mutation in new (int Index, string Value)[] { (6, "6"), (7, "1"), (21, ""), (21, "1000"), (22, ""), (23, "00"), (24, "X"), (25, "X") })
        {
            var values = Procedure(7, "Decoupled"); values[mutation.Index] = mutation.Value;
            Reject(() => Parameters(Ad(7, values)), FinTsSyntaxError.InvalidParameters);
        }
        Reject(() => Parameters(Ad(7, Procedure(7, "Decoupled")[..23])), FinTsSyntaxError.InvalidParameters);
        var invalidPush = Procedure(7, "DecoupledPush"); invalidPush[21] = "1";
        Reject(() => Parameters(Ad(7, invalidPush)), FinTsSyntaxError.InvalidParameters);
        var variantOne = Procedure(6); variantOne[1] = "1";
        Reject(() => Parameters(Ad(6, variantOne)), FinTsSyntaxError.InvalidParameters);
        variantOne[11] = "4";
        Verify(Parameters(Ad(6, variantOne)).Advertisements[0].Procedures[0].ProcessVariant == "1", "Variant 1 uses the not-applicable dialogue scope code.");
        foreach (string bad in new[] { "HITANS:7+1+1+0+N:N:0", Ad(7, Procedure(7)).Replace("+1+1+0+", "+1+4+0+", StringComparison.Ordinal),
            Ad(7, Procedure(7)).Replace("+N:N:0:", "+X:N:0:", StringComparison.Ordinal), Ad(7, Procedure(7)).Replace("+N:N:0:", "+N:N:3:", StringComparison.Ordinal),
            Ad(7, Procedure(7)) + "+extra", Ad(6, Procedure(6)[..19]), Ad(6, Procedure(6)[..20].Concat(Procedure(6)).ToArray()) })
        { Reject(() => Parameters(bad)); }
        foreach (int version in new[] { 6, 7 })
        {
            int maximum = version == 6 ? 12 : 9;
            var values = Enumerable.Range(0, maximum).SelectMany(_ => Procedure(version)).ToArray();
            Verify(Parameters(Ad(version, values)).Advertisements[0].Procedures.Count == maximum, "The local component budget permits the documented repetition boundary.");
            Reject(() => Parameters(Ad(version, values.Concat(Procedure(version)).ToArray())), FinTsSyntaxError.LimitExceeded);
        }
        var many = Enumerable.Repeat(Ad(6, Enumerable.Range(0, 12).SelectMany(_ => Procedure(6)).ToArray()), 85).ToList();
        many.Add(Ad(6, Enumerable.Range(0, 4).SelectMany(_ => Procedure(6)).ToArray()));
        Verify(Parameters(many.ToArray()).Advertisements.Sum(a => a.Procedures.Count) == 1024, "Exact total procedure bound must parse.");
        many[^1] = Ad(6, Enumerable.Range(0, 5).SelectMany(_ => Procedure(6)).ToArray());
        Reject(() => Parameters(many.ToArray()), FinTsSyntaxError.LimitExceeded);
        Verify(Parameters(Enumerable.Repeat(Ad(6, Procedure(6)), 128).ToArray()).Advertisements.Count == 128, "Exact TAN advertisement count must parse.");
        Reject(() => Parameters(Enumerable.Repeat("HITANS:99+opaque", 129).ToArray()), FinTsSyntaxError.LimitExceeded);

        foreach (int version in new[] { 6, 7 })
        {
            foreach (string process in version == 6 ? new[] { "1", "2", "3", "4" } : new[] { "1", "2", "3", "4", "S" })
            {
                var challenge = Challenges($"HITAN:{version}:2+{process}++PUBLIC-ORDER+<b>PUBLIC</b><br>Confirm?+?:?'??").Challenges[0];
                Verify(challenge.Process == process && challenge.RequestSegmentNumber == 2 && Text(challenge.Text!) == "<b>PUBLIC</b><br>Confirm+:'?",
                    "Challenge markup and FinTS escapes remain exact, unrendered source text.");
            }
        }
        Verify(Challenges("HITAN:7:2+S++PUBLIC-ORDER").Challenges[0].Text is null && Challenges("HITAN:6:2+2++PUBLIC-ORDER+").Challenges[0].Text!.IsEmpty,
            "Optional status/completion challenge text preserves omitted and explicitly empty positions.");
        var dummy = Challenges("HITAN:7:2+4++noref+nochallenge").Challenges[0];
        Verify(dummy.HasNoReferencePlaceholder, "The no-reference filler is exposed only as a literal observation.");
        var duplicates = Challenges("HITAN:7:2+4++PUBLIC-ORDER+Text", "HITAN:99:2+opaque");
        Verify(duplicates.HasDuplicateRequestReferences && duplicates.Challenges.Count == 1 && duplicates.UninterpretedSegments.Count == 1,
            "Duplicate request references include opaque future-version challenges.");
        foreach (string bad in new[]
        {
            "HITAN:7+4++PUBLIC-ORDER+Text", "HITAN:6:2+S++PUBLIC-ORDER", "HITAN:7:2+5++PUBLIC-ORDER+Text", "HITAN:7:2+4+++Text",
            "HITAN:7:2+4++PUBLIC-ORDER", "HITAN:7:2+1+++", "HITAN:7:2+4++PUBLIC-ORDER+@4@Text", "HITAN:7:2+4++PUBLIC-ORDER+PUBLIC\r",
            "HITAN:7:2+4++PUBLIC-ORDER+Text+notbinary", "HITAN:7:2+4++PUBLIC-ORDER+Text+@0@", "HITAN:7:2+1+@0@++Text",
            "HITAN:7:2+2+@1@x+PUBLIC-ORDER", "HITAN:7:2+1+text++Text", "HITAN:7:2+4++PUBLIC-ORDER+Text++20260230:120000",
            "HITAN:7:2+4++PUBLIC-ORDER+Text++20260906:250000", "HITAN:7:2+4++PUBLIC-ORDER+Text++20260906",
            "HITAN:7:2+4++PUBLIC-ORDER+Text++:120000", "HITAN:7:2+4++PUBLIC-ORDER+Text++::",
            "HITAN:7:2+4++PUBLIC-ORDER+Text+++Medium+extra", "HITAN:7:2+4++PUBLIC-ORDER+Text:more",
            "HITAN:7:2+4++" + new string('x', 36) + "+Text", "HITAN:7:2+4++PUBLIC-ORDER+" + new string('x', 2049),
            "HITAN:7:2+4++PUBLIC-ORDER+Text+++" + new string('x', 33), "HITAN:7:2+1+@257@" + new string('x', 257) + "++Text",
            "HITAN:7:2+4++PUBLIC-ORDER+Text+@65537@" + new string('x', 65537),
        }) { Reject(() => Challenges(bad), FinTsSyntaxError.InvalidTanChallenge); }
        var boundary = Challenges("HITAN:7:2+1+@256@" + new string('x', 256) + "+" + new string('x', 35) + "+" + new string('x', 2048) +
            "+@65536@" + new string('x', 65536) + "+20240229:235959+" + new string('x', 32)).Challenges[0];
        Verify(boundary.OrderHash!.CopyValueBytes().Length == 256 && boundary.HhdData!.CopyValueBytes().Length == 65536 && boundary.ValidUntil!.Elements.Count == 2,
            "Exact hash, text, HHD, date and medium boundaries must survive without decoding or timezone inference.");
        Verify(Challenges("HITAN:7:2+4++PUBLIC-ORDER+Text++:+").Challenges[0].ValidUntil!.Elements.All(e => e.IsEmpty), "Explicitly empty optional expiry DEG is retained.");
        Verify(Challenges(Enumerable.Repeat("HITAN:7:2+4++PUBLIC-ORDER+Text", 32).ToArray()).Challenges.Count == 32, "Exact challenge count preserves ambiguous evidence.");
        Reject(() => Challenges(Enumerable.Repeat("HITAN:99:2+opaque", 33).ToArray()), FinTsSyntaxError.LimitExceeded);
        var input = Response(Ad(7, Procedure(7)), "HITAN:7:2+4++PUBLIC-ORDER+Text");
        foreach (var action in new Action[]
        {
            () => FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(input), new CancellationToken(true)),
            () => FinTsTanChallengeSet.Parse(input, new CancellationToken(true)),
        })
        {
            try { action(); Verify(false, "TAN schema cancellation must stop."); }
            catch (OperationCanceledException) { Verify(true, "TAN schema cancellation works."); }
        }
        Verify(new object[] { Parameters(Ad(7, Procedure(7))), decoupled, Challenges("HITAN:7:2+4++PUBLIC-ORDER+Text"), dummy }
            .All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default TAN diagnostics must exclude source values.");

        string inner = "HIRMG:2:2+0030::Pending'HITAN:3:7:2+4++PUBLIC-ORDER+PUBLIC-CHALLENGE'";
        string wrapper = "HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1+2:2:13:@8@00000000:5:1+280:10020030:PUBLIC-KEY:V:0:0+0'" +
            $"HNVSD:999:1+@{inner.Length}@{inner}'";
        var envelope = FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Wire(wrapper, 4)));
        var wrappedChallenges = FinTsTanChallengeSet.Parse(FinTsResponse.ParsePinTan(envelope));
        Verify(ReferenceEquals(wrappedChallenges.Source.PinTanEnvelope, envelope) && Text(wrappedChallenges.Challenges[0].Text!) == "PUBLIC-CHALLENGE",
            "Wrapped challenge evidence retains outer security-envelope provenance.");

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.tan-schemas-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (var vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(wire));
            var ads = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response));
            var challenges = FinTsTanChallengeSet.Parse(response);
            Verify(response.Frame.Syntax.CopyWireBytes().SequenceEqual(wire), "Independent TAN fixture wire must be retained exactly.");
            Verify(ads.Advertisements.Sum(a => a.Procedures.Count) == vector.GetProperty("procedureCount").GetInt32() &&
                challenges.Challenges.Count == vector.GetProperty("challengeCount").GetInt32(), "Independent procedure/challenge counts must match.");
            Verify(challenges.HasDuplicateRequestReferences == vector.GetProperty("duplicates").GetBoolean(), "Independent duplicate expectations must match.");
            if (challenges.Challenges.Count == 0)
            {
                Verify(ads.UninterpretedSegments.Count == 2 && challenges.UninterpretedSegments.Count == 2, "Future schemas stay opaque without fallback.");
                continue;
            }
            var procedure = ads.Advertisements[0].Procedures[0];
            var challenge = challenges.Challenges[0];
            Verify(procedure.IsDecoupled == vector.GetProperty("decoupled").GetBoolean() && procedure.SegmentVersion == vector.GetProperty("version").GetInt32(), "Independent procedure semantics must match.");
            Verify(challenge.HasNoReferencePlaceholder == vector.GetProperty("noReference").GetBoolean() && challenge.Process == vector.GetProperty("process").GetString(), "Independent challenge process and filler observations must match.");
            Verify(challenge.HhdData!.CopyValueBytes().SequenceEqual(Enumerable.Range(0, 256).Select(i => (byte)i)), "Binary challenge octets stay intact without interpretation.");
            Verify(ads.Advertisements[0].HasDuplicateSecurityFunctions == vector.GetProperty("duplicates").GetBoolean(), "Independent duplicate procedure expectations must match.");
        }
        Console.WriteLine($"FinTS TAN procedure/challenge schemas: {vectors} independent vectors and {count} checks completed.");
    }

    private static string Text(FinTsDataElement value) => Encoding.Latin1.GetString(value.CopyValueBytes());
    private static string[] Procedure(int version, string method = "App")
    {
        bool decoupled = method is "Decoupled" or "DecoupledPush";
        string[] values = ["900", "2", "PUBLIC_METHOD", method, "1.0", "Public method", decoupled ? "" : "6", decoupled ? "" : "1",
            "Approval", "2048", "N", "1", "N", "0", "0", "N", "J", "00", "0", "N", "0"];
        return version == 6 ? values : values.Concat(method == "Decoupled" ? ["10", "2", "5", "N", "J"] : new[] { "", "", "", "", "" }).ToArray();
    }
    private static string Ad(int version, string[] values) => $"HITANS:{version}+1+1+0+N:N:0:" + string.Join(':', values);
    private static FinTsTanParameterSet Parameters(params string[] parts) => FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(Response(parts)));
    private static FinTsTanChallengeSet Challenges(params string[] parts) => FinTsTanChallengeSet.Parse(Response(parts));
    private static FinTsResponse Response(params string[] parts)
    {
        string body = "HIRMG:2:2+0030::Pending'" + string.Concat(parts.Select((part, index) => part.Insert(part.IndexOf(':') + 1, (index + 3).ToString(CultureInfo.InvariantCulture) + ":") + "'"));
        return FinTsResponse.Parse(FinTsMessageFrame.Parse(Wire(body, parts.Length + 3)));
    }
    private static byte[] Wire(string body, int trailer)
    {
        string wire = "HNHBK:1:3+000000000000+300+SYNTHETIC+1+SYNTHETIC:1'" + body + $"HNHBS:{trailer}:1+1'";
        return Encoding.Latin1.GetBytes(wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}
