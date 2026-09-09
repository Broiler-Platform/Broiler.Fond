using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanRequestBindingTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-request-binding-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var signature = FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context"));
            var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(signature);
            var response = Response(vector);
            byte[] original = response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanResponseBinding.Evaluate(request, response);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanResponseBindingIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsPinTanResponseBindingIssue>(value.GetString()!));
            Verify(result.Issues == expected && result.HasMatchingReferences == (expected == FinTsPinTanResponseBindingIssue.None), "Independent request-binding issues match: " + vector.GetProperty("name").GetString() + " (" + result.Issues + ").");
            Verify(ReferenceEquals(result.Request, request) && ReferenceEquals(result.Response, response) && ReferenceEquals(request.SignatureEvidence, signature), "Binding retains the exact caller metadata and original response instances.");
            Verify(result.ReportedDialogueId == vector.GetProperty("dialogue").GetString() && response.HasErrors == vector.GetProperty("hasErrors").GetBoolean(), "Reported dialogue and original error semantics remain visible on every binding result.");
            Verify(request.IdentificationNumber == 3 && request.PreparationNumber == 4 && request.SynchronizationNumber == (signature.Request.Synchronization is null ? null : 5) && request.ExpectedUserId == "PUBLIC-USER" && request.MessageNumber == 1, "Candidate metadata describes actual signed business positions and explicit user identity.");
            var links = vector.GetProperty("references").EnumerateArray().ToArray();
            Verify(result.References.Count == links.Length, "Every non-message reply/data segment has a source-preserving reference observation.");
            for (int i = 0; i < links.Length; i++)
            {
                var actual = result.References[i]; var expectedLink = links[i];
                int? reference = expectedLink.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : expectedLink.GetProperty("reference").GetInt32();
                Verify(actual.ResponseSegment.Code == expectedLink.GetProperty("code").GetString() && actual.ResponseSegment.Reference == reference && actual.Target?.Role.ToString() == expectedLink.GetProperty("role").GetString(), "Each response reference resolves to the independent actual role, including signature/framing errors.");
                Verify(response.BodySegments.Any(s => ReferenceEquals(s, actual.ResponseSegment)) && (actual.Target is null || request.Segments.Any(s => ReferenceEquals(s, actual.Target))), "No response segment or target descriptor is synthesized or renumbered during binding.");
            }
            Verify(original.SequenceEqual(response.Frame.Syntax.CopyWireBytes()), "Reference binding leaves original response framing and payload bytes unchanged.");
            Verify(FinTsPinTanResponseBinding.Evaluate(request, response).Issues == result.Issues, "Pure binding consumes no response and claims no replay protection.");
        }
        Verify(vectors.Length == 23, "All twenty-three independent binding fixtures ran.");
        // Compare metadata with actual synthetic envelope output, then erase/dispose all local credential material.
        foreach (int index in new[] { 0, 1, 2 })
        {
            var signature = FinTsPinTanSignatureTrailerTests.Evidence(vectors[index].GetProperty("context"));
            var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(signature);
            byte[] wire = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
            using (var pin = FinTsSessionCredential.CaptureAndClear("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1)))
            {
                try
                {
                    Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(signature, pin, null, wire, out int written) == FinTsPinTanRequestWriteResult.Written, "The context's exact signature evidence can drive the restricted envelope writer.");
                    var frame = FinTsMessageFrame.Parse(wire.AsSpan(0, written));
                    var outer = frame.Syntax.Segments;
                    var inner = FinTsSyntax.ParseSegments(outer[2].Fields[0].Elements[0].CopyValueBytes()).Segments;
                    var actual = outer.Take(3).Concat(inner).Append(outer[^1]).ToArray();
                    Verify(actual.Length == request.Segments.Count && actual.Zip(request.Segments).All(pair => pair.First.Code == pair.Second.Code && pair.First.Number == pair.Second.Number && pair.First.Version == pair.Second.Version), "Metadata matches all actual wrapper, signature, business and framing segment numbers/versions.");
                }
                finally { CryptographicOperations.ZeroMemory(wire); }
            }
            Verify(wire.All(b => b == 0) && FinTsPinTanResponseBinding.Evaluate(request, Response(vectors[index])).HasMatchingReferences, "Candidate metadata remains usable after credential disposal and complete caller-output erasure.");
            try { ((IList<FinTsPinTanRequestSegmentBinding>)request.Segments).Clear(); Verify(false, "Callers must not mutate candidate geometry."); }
            catch (NotSupportedException) { Verify(true, "Candidate geometry is immutable."); }
        }
        var basis = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vectors[0].GetProperty("context")));
        var responseBasis = Response(vectors[0]);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanRequestBinding.ForEnvelopeCandidate(basis.SignatureEvidence, cancellation.Token); Verify(false, "Cancelled context creation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Context construction observes cancellation."); }
            try { FinTsPinTanResponseBinding.Evaluate(basis, responseBasis, cancellation.Token); Verify(false, "Cancelled binding must throw."); }
            catch (OperationCanceledException) { Verify(true, "Response binding observes cancellation without publishing a result."); }
        }
        using (var missingDocument = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-context-v1.json")!))
        {
            var unresolved = FinTsPinTanSignatureTrailerTests.Evidence(missingDocument.RootElement.GetProperty("vectors")[3]);
            try { FinTsPinTanRequestBinding.ForEnvelopeCandidate(unresolved); Verify(false, "Unresolved signature context cannot produce candidate geometry."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext && !error.ToString().Contains("PUBLIC-USER", StringComparison.Ordinal), "Invalid candidate context uses fixed diagnostics."); }
        }
        foreach (int input in new[] { 0, 1, 2 })
        {
            try
            {
                if (input == 0) { FinTsPinTanRequestBinding.ForEnvelopeCandidate(null!); }
                else { FinTsPinTanResponseBinding.Evaluate(input == 1 ? null! : basis, input == 2 ? null! : responseBasis); }
                Verify(false, "Null required inputs must throw.");
            }
            catch (ArgumentNullException) { Verify(true, "Required null inputs fail without constructing partial binding evidence."); }
        }
        var repeated = new FinTsPinTanResponseBinding[16];
        Parallel.For(0, repeated.Length, i => repeated[i] = FinTsPinTanResponseBinding.Evaluate(basis, responseBasis));
        Verify(repeated.All(r => r.HasMatchingReferences && ReferenceEquals(r.Response, responseBasis)), "Concurrent pure comparisons retain one source without consumption or mutation.");
        var abort = FinTsPinTanResponseBinding.Evaluate(basis, Response(vectors[21]));
        Verify(abort.HasMatchingReferences && abort.Response.HasErrors && abort.Response.ReplySegments.Single().Replies.Single().Code == "9800", "A correctly scoped abort remains an error and cannot be treated as execution success.");
        var foreign = FinTsPinTanResponseBinding.Evaluate(basis, Response(vectors[22]));
        Verify(foreign.HasMatchingReferences && Encoding.Latin1.GetString(FinTsParameterSet.Parse(foreign.Response).User!.UserId.CopyValueBytes()) != basis.ExpectedUserId, "Matching references do not establish bank/user/parameter identity; semantic comparison remains required.");
        var old = FinTsPinTanResponseBinding.Evaluate(basis, Response(vectors[3]));
        Verify(old.References.Single(r => r.ResponseSegment.Code == "HIBPA").Target!.Role == FinTsPinTanRequestSegmentRole.Identification && !old.HasMatchingReferences, "Old preparation number 3 resolves to identification and is rejected for parameter data.");
        var signatureError = FinTsPinTanResponseBinding.Evaluate(basis, Response(vectors[7]));
        Verify(signatureError.References.Single(r => r.ResponseSegment.Code == "HIRMS").Target!.Role == FinTsPinTanRequestSegmentRole.SignatureHeader && signatureError.Response.HasErrors, "Reference 2 denotes the signature header, never successful identification.");
        Console.WriteLine($"FinTS PIN/TAN request/response reference binding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsResponse Response(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        return vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }
}
