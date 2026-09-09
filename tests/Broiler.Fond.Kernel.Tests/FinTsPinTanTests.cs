using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanTests
{
    private const string Header = "HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1:20260906:120000+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:10020030:PUBLIC-KEY-ID:V:0:0+0'";
    private const string Reply = "HIRMG:2:2+0010::received'";
    private const string Parameters = "HIPINS:3:1+1+1+0+5:12:6:User?: ID::HKSAL:J:HKSPA:N'";

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        void Reject(Action action, FinTsSyntaxError? category = null)
        {
            try { action(); Verify(false, "Invalid PIN/TAN evidence must be rejected."); }
            catch (FinTsFormatException error)
            {
                Verify(category is null || error.Error == category, $"PIN/TAN failure must retain its fixed error category (check {count + 1}, expected {category}, actual {error.Error}).");
                Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "PIN/TAN errors must exclude supplied source data.");
            }
        }
        var bytes = Wrapped(Reply + Parameters, 4);
        var frame = FinTsMessageFrame.Parse(bytes);
        var envelope = FinTsPinTanEnvelope.Parse(frame);
        var response = FinTsResponse.ParsePinTan(envelope);
        var parameters = FinTsPinTanParameterSet.Parse(FinTsParameterSet.Parse(response));
        var advertisement = parameters.Advertisements.Single();
        Verify(ReferenceEquals(frame, response.Frame) && ReferenceEquals(envelope, response.PinTanEnvelope), "Wrapped response provenance must retain the exact outer frame and envelope.");
        Verify(response.BodySegments.Select(s => s.Number).SequenceEqual(new[] { 2, 3 }), "Response schemas must consume the validated inner sequence.");
        Verify(frame.Syntax.CopyWireBytes().SequenceEqual(bytes) && envelope.Body.CopyWireBytes().SequenceEqual(Encoding.Latin1.GetBytes(Reply + Parameters)), "Outer and inner bytes must survive exactly.");
        Verify(envelope.ProfileVersion == 2 && Text(envelope.SystemId) == "PUBLIC-SYSTEM", "Reported profile and system identity remain untrusted evidence.");
        Verify(advertisement.MinimumPinLength == 5 && advertisement.MaximumPinLength == 12 && advertisement.MaximumTanLength == 6 &&
            Text(advertisement.UserIdLabel!) == "User: ID" && advertisement.CustomerIdLabel!.IsEmpty, "HIPINS bounds, escaped labels and empty positions must survive.");
        Verify(advertisement.MaximumOrders == 1 && advertisement.MinimumSignatures == 1 && advertisement.SecurityClass == 0, "Common HIPINS fields must survive without interpreting security class.");
        Verify(parameters.GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.TanReportedRequired &&
            parameters.GetOperationEvidence("HKSPA") == FinTsPinTanOperationEvidence.TanReportedNotRequired &&
            parameters.GetOperationEvidence("HKXYZ") == FinTsPinTanOperationEvidence.Unlisted, "Flags must preserve required, not reported required and unlisted evidence distinctly.");
        bytes[0] = 0;
        var copy = envelope.Body.CopyWireBytes();
        copy[0] = 0;
        Verify(frame.Syntax.CopyWireBytes()[0] == 'H' && envelope.Body.CopyWireBytes()[0] == 'H', "Caller and returned arrays cannot mutate retained wire data.");
        Reject(() => FinTsResponse.Parse(frame), FinTsSyntaxError.UnsupportedSecurityWrapper);
        Reject(() => FinTsPinTanEnvelope.Parse(Plain(Reply, 3)), FinTsSyntaxError.UnsupportedSecurityWrapper);
        foreach (var action in new Action[]
        {
            () => FinTsPinTanEnvelope.Parse(frame, new CancellationToken(true)),
            () => FinTsResponse.ParsePinTan(envelope, new CancellationToken(true)),
            () => FinTsPinTanParameterSet.Parse(parameters.Source, new CancellationToken(true)),
        })
        {
            try { action(); Verify(false, "Cancelled PIN/TAN parsing must stop."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation is supported."); }
        }
        foreach (string header in new[]
        {
            Header.Replace("998:3", "998:2", StringComparison.Ordinal), Header.Replace("PIN:2", "PIN:3", StringComparison.Ordinal),
            Header.Replace("PIN:2", "RAH:7", StringComparison.Ordinal), Header.Replace("+998+", "+4+", StringComparison.Ordinal),
            Header[..^2] + "1'",
        }) { Reject(() => ParseEnvelope(Reply, 3, header), FinTsSyntaxError.UnsupportedSecurityWrapper); }
        foreach (string header in new[]
        {
            Header.Replace("998:3+", "998:3:1+", StringComparison.Ordinal), Header.Replace("PIN:2", "PIN:2:1", StringComparison.Ordinal),
            Header.Replace("+998+1+", "+998+3+", StringComparison.Ordinal),
            Header.Replace("1::PUBLIC-SYSTEM", "1:@0@:PUBLIC-SYSTEM", StringComparison.Ordinal),
            Header.Replace("1::PUBLIC-SYSTEM", "1:CID:PUBLIC-SYSTEM", StringComparison.Ordinal),
            Header.Replace("1::PUBLIC-SYSTEM", "1::", StringComparison.Ordinal),
            Header.Replace("1::PUBLIC-SYSTEM", "3::PUBLIC-SYSTEM", StringComparison.Ordinal),
            Header.Replace("1:20260906:120000", "1::120000", StringComparison.Ordinal),
            Header.Replace("20260906", "20260230", StringComparison.Ordinal), Header.Replace("120000", "250000", StringComparison.Ordinal),
            Header.Replace("1:20260906:120000", "6:20260906:120000", StringComparison.Ordinal),
            Header.Replace("+2:2:13:", "+3:2:13:", StringComparison.Ordinal), Header.Replace("+2:2:13:", "+2:3:13:", StringComparison.Ordinal),
            Header.Replace("+2:2:13:", "+2:2:15:", StringComparison.Ordinal),
            Header.Replace("@8@\0\0\0\0\0\0\0\0", "text", StringComparison.Ordinal),
            Header.Replace("@8@\0\0\0\0\0\0\0\0", "@513@" + new string('x', 513), StringComparison.Ordinal),
            Header.Replace(":5:1+", ":X:1+", StringComparison.Ordinal), Header.Replace(":5:1+", ":5:2+", StringComparison.Ordinal),
            Header.Replace(":5:1+", ":5:1:@0@+", StringComparison.Ordinal),
            Header.Replace("+280:", "+28:", StringComparison.Ordinal), Header.Replace(":V:0:0", ":S:0:0", StringComparison.Ordinal),
            Header.Replace(":V:0:0", ":V:00:0", StringComparison.Ordinal), Header.Replace(":V:0:0", ":V:0:1000", StringComparison.Ordinal),
            Header.Replace("PUBLIC-KEY-ID", new string('x', 31), StringComparison.Ordinal),
            Header[..^1] + "+certificate'", Header[..^1] + "+@0@'", Header[..^1] + "++extra'",
        }) { Reject(() => ParseEnvelope(Reply, 3, header), FinTsSyntaxError.InvalidSecurityEnvelope); }
        foreach (string header in new[]
        {
            Header.Replace("PIN:2", "PIN:1", StringComparison.Ordinal), Header.Replace("1:20260906:120000", "1", StringComparison.Ordinal),
            Header.Replace("1:20260906:120000", "1::", StringComparison.Ordinal), Header.Replace("1:20260906:120000", "1:20240229", StringComparison.Ordinal),
            Header.Replace(":5:1+", ":5:1:+", StringComparison.Ordinal), Header[..^1] + "+'",
            Header.Replace("+998+1+", "+998+4+", StringComparison.Ordinal),
            Header.Replace("2:2:13:", "2:18:14:", StringComparison.Ordinal),
        }) { Verify(ParseEnvelope(Reply, 3, header).Body.Segments.Count == 1, "Allowed profile, optional timestamp and empty placeholders must parse."); }
        foreach (string body in new[] { "", "HIRMG:3:2+0010::received'", Reply + "ZTEST:2:1'", Reply + "ZTEST:4:1'", "HNHBK:2:3'", "HNHBS:2:1'" })
        { Reject(() => ParseEnvelope(body, body.Count(c => c == '\'') + 2), FinTsSyntaxError.InvalidSecurityEnvelope); }
        Reject(() => ParseEnvelope(Reply, 4), FinTsSyntaxError.InvalidSecurityEnvelope);
        foreach (string code in new[] { "HNVSK", "HNVSD", "HNSHK", "HNSHA", "HKTAN", "HKSAL" })
        { Reject(() => ParseEnvelope($"{code}:2:1+PUBLIC-SECRET'", 3), FinTsSyntaxError.UnsupportedSecurityWrapper); }
        foreach (string data in new[] { "HNVSD:999:2+@0@'", "HNVSD:999:1:1+@0@'", "HNVSD:999:1+text'", "HNVSD:999:1+@0@:x'", "HNVSD:999:1+@0@+x'" })
        { Reject(() => FinTsPinTanEnvelope.Parse(Plain(Header + data, 3))); }
        Reject(() => FinTsResponse.ParsePinTan(ParseEnvelope("ZTEST:2:1'", 3)), FinTsSyntaxError.InvalidResponse);

        Verify(ParseParameters("HIPINS:3:1+0+0+4+0:99:0'").Advertisements[0].MinimumPinLength == 0, "Explicit zero is distinct from an omitted PIN bound.");
        var omitted = ParseParameters("HIPINS:3:1+1+1+0+'").Advertisements[0];
        Verify(omitted.MinimumPinLength is null && omitted.MaximumPinLength is null && omitted.UserIdLabel is null, "Omitted fields must remain absent.");
        Verify(ParseParameters("").GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.Unknown, "Missing HIPINS is unknown.");
        Verify(ParseParameters("HIPINS:3:999+opaque'").GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.Unknown, "An unknown HIPINS version cannot supply operation evidence.");
        Verify(ParseParameters(Parameters + Parameters.Replace(":3:1", ":4:1", StringComparison.Ordinal)).GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.Ambiguous, "Duplicate advertisements must not select a winner.");
        var conflict = ParseParameters(Parameters.Replace("5:12:6", "12:5:6", StringComparison.Ordinal));
        Verify(conflict.Advertisements[0].HasConflictingPinLengthBounds && conflict.GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.Ambiguous, "Conflicting PIN bounds remain explicit and cannot yield unique evidence.");
        foreach (string tail in new[] { "1+1+0", "1+1+0++extra", "01+1+0+", "1+4+0+", "1+1+5+", "1+1+0+100", "1+1+0+01", "1+1+0+@0@", "1+1+0+:::::HKSAL", "1+1+0+:::::HKSAL:X", "1+1+0+:::::hksal:J", "1+1+0+::::::J", "1+1+0+:::::HKSAL:@1@J", "1+1+0+:::" + new string('x', 31), "1+1+0+:::PUBLIC-SECRET\r" })
        { Reject(() => ParseParameters("HIPINS:3:1+" + tail + "'"), FinTsSyntaxError.InvalidParameters); }
        Reject(() => parameters.GetOperationEvidence("hksal"), FinTsSyntaxError.InvalidParameters);
        string pairs = string.Concat(Enumerable.Repeat(":HKSAL:J", 125));
        Verify(ParseParameters("HIPINS:3:1+1+1+0+::::" + pairs + "'").Advertisements[0].Operations.Count == 125, "Local maximum operation count must preserve duplicates.");
        Reject(() => ParseParameters("HIPINS:3:1+1+1+0+::::" + pairs + ":HKSPA:N'"), FinTsSyntaxError.LimitExceeded);
        string Many(int number, int version) => string.Concat(Enumerable.Range(3, number).Select(i => $"HIPINS:{i}:{version}+1+1+0+'"));
        Verify(ParseParameters(Many(128, 1)).Advertisements.Count == 128, "Exact advertisement bound is accepted.");
        Reject(() => ParseParameters(Many(129, 1)), FinTsSyntaxError.LimitExceeded);
        Reject(() => ParseParameters(Many(129, 999)), FinTsSyntaxError.LimitExceeded);

        string LargeBody(int elements) => Reply + "ZTEST:3:1+" + string.Join('+', Enumerable.Range(0, (elements + 255) / 256)
            .Select(i => string.Join(':', Enumerable.Repeat("x", Math.Min(256, elements - i * 256))))) + "'";
        Verify(ParseEnvelope(LargeBody(32716), 4).Body.Segments.Count == 2, "Combined outer/inner element budget accepts its exact boundary.");
        Reject(() => ParseEnvelope(LargeBody(32717), 4), FinTsSyntaxError.LimitExceeded);
        Reject(() => ParseEnvelope(LargeBody(32716).Replace("ZTEST:3:1+", "ZTEST:3:1:+", StringComparison.Ordinal), 4), FinTsSyntaxError.LimitExceeded);
        string manySegments = Reply + string.Concat(Enumerable.Range(3, 994).Select(i => $"ZTEST:{i}:1'"));
        Verify(ParseEnvelope(manySegments, 997).Body.Segments.Count == 995, "Maximum wrapped segment sequence must fit reserved outer numbers.");

        var tracker = new FinTsDialogueCorrelation();
        var request = Plain("HKSYN:2:3+0'", 3, request: true);
        Verify(tracker.BeginRequest(request) == FinTsCorrelationStatus.RequestRecorded, "Synthetic request can be recorded mechanically.");
        var wrongReference = FinTsResponse.ParsePinTan(ParseEnvelope(Reply + "ZTEST:3:1:9'", 4));
        Verify(tracker.AcceptResponse(wrongReference) == FinTsCorrelationStatus.UnknownSegmentReference && tracker.State == FinTsCorrelationState.AwaitingResponse,
            "Correlation must inspect inner unknown segment references without consuming a rejected response.");
        var matching = FinTsResponse.ParsePinTan(ParseEnvelope(Reply + "HIRMS:3:2:2+0010::received'", 4));
        Verify(tracker.AcceptResponse(matching) == FinTsCorrelationStatus.Matched && tracker.AcceptResponse(matching) == FinTsCorrelationStatus.NoPendingRequest,
            "Wrapped replies can match mechanically once, without implying authentication.");
        Verify(new FinTsDialogueCorrelation().BeginRequest(frame) == FinTsCorrelationStatus.UnsupportedSecurityWrapper, "Wrapped outgoing requests remain unsupported.");
        Verify(new object[] { envelope, response, parameters, advertisement, advertisement.Operations[0] }.All(o => !o.ToString()!.Contains("PUBLIC-", StringComparison.Ordinal)), "Default diagnostics must exclude source values.");

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (var vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            var parsed = FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(wire));
            var result = FinTsPinTanParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.ParsePinTan(parsed)));
            Verify(parsed.Frame.Syntax.CopyWireBytes().SequenceEqual(wire) && parsed.Body.CopyWireBytes().SequenceEqual(Convert.FromBase64String(vector.GetProperty("bodyBase64").GetString()!)), "Independent wrapper and inner bytes must match exactly.");
            Verify(parsed.ProfileVersion == vector.GetProperty("profileVersion").GetInt32() && result.Advertisements.Count == vector.GetProperty("advertisementCount").GetInt32(), "Independent profile/count expectations must match.");
            Verify(result.GetOperationEvidence("HKSAL").ToString() == vector.GetProperty("balanceEvidence").GetString(), "Independent HIPINS operation evidence must match.");
            Verify(result.UninterpretedSegments.Single(s => s.Code == "ZBLOB").Fields[0].Elements[0].CopyValueBytes().SequenceEqual(Enumerable.Range(0, 256).Select(i => (byte)i)), "All binary octets inside a wrapper must remain opaque and intact.");
        }
        Console.WriteLine($"FinTS PIN/TAN envelope/parameter verification passed ({vectors} independent vectors, {count} checks).");
    }

    private static string Text(FinTsDataElement value) => Encoding.Latin1.GetString(value.CopyValueBytes());
    private static FinTsPinTanEnvelope ParseEnvelope(string body, int trailer, string header = Header) => FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Wrapped(body, trailer, header)));
    private static FinTsPinTanParameterSet ParseParameters(string body) => FinTsPinTanParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Plain(Reply + body, body.Count(c => c == '\'') + 3))));
    private static byte[] Wrapped(string body, int trailer, string header = Header) => Wire(header + "HNVSD:999:1+@" + Encoding.Latin1.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "@" + body + "'", trailer);
    private static FinTsMessageFrame Plain(string body, int trailer, bool request = false) => FinTsMessageFrame.Parse(Wire(body, trailer, request));
    private static byte[] Wire(string body, int trailer, bool request = false)
    {
        string wire = "HNHBK:1:3+000000000000+300+" + (request ? "0+1" : "SYNTHETIC+1+SYNTHETIC:1") + "'" + body + $"HNHBS:{trailer}:1+1'";
        return Encoding.Latin1.GetBytes(wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}
