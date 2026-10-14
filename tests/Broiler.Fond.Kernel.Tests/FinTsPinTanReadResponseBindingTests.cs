using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadResponseBindingTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-response-binding-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var request = Candidate(vector); var response = Response(vector);
            byte[] before = response.Frame.Syntax.CopyWireBytes();
            byte[] requestBefore = request.Context.Signature.Request.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanReadResponseBinding.Evaluate(request, response);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanResponseBindingIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsPinTanResponseBindingIssue>(value.GetString()!));
            Verify(result.Issues == expected && result.HasMatchingReferences == (expected == FinTsPinTanResponseBindingIssue.None),
                "Independent first-read binding flags: " + vector.GetProperty("name").GetString() + " (" + result.Issues + ").");
            Verify(ReferenceEquals(result.Request, request) && ReferenceEquals(result.Response, response), "Binding preserves the exact candidate and original parsed response.");
            Verify(response.HasErrors == vector.GetProperty("hasErrors").GetBoolean(), "Mapped references retain original error observations, including correctly scoped aborts.");
            var links = vector.GetProperty("references").EnumerateArray().ToArray();
            Verify(result.References.Count == links.Length, "Each non-message body segment retains a reference observation.");
            for (int i = 0; i < links.Length; i++)
            {
                var actual = result.References[i]; var link = links[i];
                int? reference = link.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : link.GetProperty("reference").GetInt32();
                Verify(actual.ResponseSegment.Code == link.GetProperty("code").GetString() && actual.ResponseSegment.Reference == reference &&
                    actual.Target?.Role.ToString() == link.GetProperty("role").GetString(), "Response numbers resolve to the independent assembled roles.");
                Verify(response.BodySegments.Any(s => ReferenceEquals(s, actual.ResponseSegment)) &&
                    (actual.Target is null || request.Segments.Any(s => ReferenceEquals(s, actual.Target))), "Reference links preserve actual source and target objects without renumbering.");
            }
            Verify(before.SequenceEqual(response.Frame.Syntax.CopyWireBytes()) && requestBefore.SequenceEqual(request.Context.Signature.Request.Frame.Syntax.CopyWireBytes()),
                "Reference comparison leaves both response and unsigned request bytes untouched.");
            Verify(FinTsPinTanReadResponseBinding.Evaluate(request, response).Issues == result.Issues, "Pure comparison consumes no response or replay state.");
        }
        Verify(vectors.Length == 46, "All forty-six independent first-read reference vectors ran.");
        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanReadResponseBinding Bound(string name)
        { var vector = Named(name); return FinTsPinTanReadResponseBinding.Evaluate(Candidate(vector), Response(vector)); }

        var old = Bound("old-unsigned-data-reference-two");
        Verify(!old.HasMatchingReferences && old.References.Single(r => r.ResponseSegment.Code == "HISAL").Target!.Role == FinTsPinTanRequestSegmentRole.SignatureHeader,
            "Old unsigned read reference 2 identifies HNSHK, never the signed read operation.");
        var security = Bound("signature-success-is-not-read-execution");
        Verify(security.HasMatchingReferences && security.References.Single().Target!.Role == FinTsPinTanRequestSegmentRole.SignatureHeader &&
            security.Response.ReplySegments.Single(s => !s.IsMessageLevel).Replies.Single().Meaning == FinTsReplyMeaning.ExecutionReported,
            "A security execution report stays attached to security and establishes no read execution.");
        var abort = Bound("message-abort");
        Verify(abort.HasMatchingReferences && abort.Response.HasErrors && abort.References.Count == 0, "Matching message references preserve aborts without manufacturing read evidence.");
        var opaque = Bound("opaque-payload-needs-schema-validation");
        try { FinTsReadDataSet.Parse(opaque.Response); Verify(false, "An opaque HISAL payload must still fail business-schema parsing."); }
        catch (FinTsFormatException) { Verify(opaque.HasMatchingReferences, "Reference vocabulary checks do not validate business payload fields."); }
        var otherVersion = Bound("known-version-needs-semantic-comparison");
        Verify(otherVersion.HasMatchingReferences && otherVersion.References.Single(r => r.ResponseSegment.Code == "HISAL").ResponseSegment.Version !=
            otherVersion.Request.Segments.Single(s => s.Role == FinTsPinTanRequestSegmentRole.ReadOperation).Version,
            "A known response version retains its source for later exact request-version comparison.");
        var foreign = Bound("foreign-envelope-identity-needs-semantics");
        Verify(foreign.HasMatchingReferences && Encoding.Latin1.GetString(foreign.Response.PinTanEnvelope!.SystemId.CopyValueBytes()) != foreign.Request.Context.Signature.Header.SystemId,
            "Profile/reference matches do not establish envelope system or key identity.");
        var duplicate = Bound("duplicate-data-needs-semantics");
        Verify(duplicate.HasMatchingReferences && duplicate.References.Count(r => r.ResponseSegment.Code == "HISAL") == 2,
            "Reference binding preserves duplicate data for semantic review instead of accepting completeness.");
        Verify(Bound("foreign-account-needs-semantics").HasMatchingReferences && Bound("missing-pin-bounds-candidate").HasMatchingReferences,
            "Reference matching proves neither returned account identity nor successful credential encoding.");

        // Actual synthetic envelopes connect these observations to emitted read segment 3; no production secret wire is parsed.
        foreach (var vector in vectors.Take(8))
        {
            var candidate = Candidate(vector); byte[] output = new byte[FinTsPinTanReadRequestEnvelopeWriter.MaximumEncodedLength];
            using (var pin = FinTsSessionCredential.CaptureAndClear(Convert.FromBase64String(vector.GetProperty("candidate").GetProperty("pinBase64").GetString()!),
                FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1)))
            {
                try
                {
                    Verify(FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(candidate, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written,
                        "The exact bound context encodes the independently scoped first-read candidate.");
                    var outer = FinTsMessageFrame.Parse(output.AsSpan(0, written)).Syntax.Segments;
                    byte[] payload = outer[2].Fields[0].Elements[0].CopyValueBytes();
                    try
                    {
                        var inner = FinTsSyntax.ParseSegments(payload).Segments;
                        var actual = outer.Take(3).Concat(inner).Append(outer[^1]).ToArray();
                        Verify(actual.Length == candidate.Segments.Count && actual.Zip(candidate.Segments).All(p => p.First.Code == p.Second.Code &&
                            p.First.Number == p.Second.Number && p.First.Version == p.Second.Version), "Bound reference targets match every emitted outer and inner segment.");
                    }
                    finally { CryptographicOperations.ZeroMemory(payload); }
                }
                finally { CryptographicOperations.ZeroMemory(output); }
            }
            Verify(output.All(b => b == 0) && FinTsPinTanReadResponseBinding.Evaluate(candidate, Response(vector)).HasMatchingReferences,
                "Binding remains usable after credential disposal and caller output erasure.");
        }
        var basis = Candidate(vectors[0]); var responseBasis = Response(vectors[0]);
        var bound = FinTsPinTanReadResponseBinding.Evaluate(basis, responseBasis);
        try { ((IList<FinTsPinTanResponseSegmentBinding>)bound.References).Clear(); Verify(false, "Reference observations must be immutable."); }
        catch (NotSupportedException) { Verify(true, "Callers cannot alter reference observations."); }
        var escaped = Candidate(Named("escaped-dialogue"));
        Verify(FinTsPinTanReadResponseBinding.Evaluate(escaped, responseBasis).Issues == FinTsPinTanResponseBindingIssue.MessageMismatch,
            "Another qualified dialogue cannot inherit matching read references.");
        var reparsed = Response(vectors[0]);
        Verify(FinTsPinTanReadResponseBinding.Evaluate(basis, reparsed).HasMatchingReferences && !ReferenceEquals(reparsed, responseBasis),
            "Reparsed bytes remain independent evidence without replay consumption.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadResponseBinding.Evaluate(basis, responseBasis, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancelled comparison publishes no binding."); }
        }
        foreach (bool missingRequest in new[] { false, true })
        {
            try { FinTsPinTanReadResponseBinding.Evaluate(missingRequest ? null! : basis, missingRequest ? responseBasis : null!); Verify(false, "Null required input must throw."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Required input diagnostics include no source values."); }
        }
        var results = new FinTsPinTanReadResponseBinding[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanReadResponseBinding.Evaluate(basis, responseBasis));
        Verify(results.All(r => r.HasMatchingReferences && ReferenceEquals(r.Request, basis) && ReferenceEquals(r.Response, responseBasis)),
            "Concurrent comparisons preserve immutable source identity without consumption.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadResponseBinding.Evaluate(basis, responseBasis).HasMatchingReferences, "Message and reference comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine($"FinTS first-read response reference binding passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsPinTanReadRequestBinding Candidate(JsonElement vector) => FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(
        FinTsPinTanReadPinTrailerTests.Context(vector.GetProperty("candidate")));
    private static FinTsResponse Response(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        return vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }
}
