using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanDialogueEndResponseTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-dialogue-end-response-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = FinTsPinTanDialogueEndTests.Context(vector.GetProperty("context"));
            var candidate = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(context);
            var response = Response(vector); byte[] before = response.Frame.Syntax.CopyWireBytes();
            var binding = FinTsPinTanDialogueEndResponseBinding.Evaluate(candidate, response);
            var evidence = FinTsPinTanDialogueEndEvidence.Evaluate(binding);
            string name = vector.GetProperty("name").GetString()!;
            Verify(binding.Issues == Flags<FinTsPinTanResponseBindingIssue>(vector, "bindingIssues"), "Independent closing binding flags match: " + name);
            Verify(evidence.Issues == Flags<FinTsPinTanDialogueEndIssue>(vector, "issues") && evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString(), "Independent termination flags and outcome match: " + name);
            Verify(evidence.CloseReported == vector.GetProperty("closeReported").GetBoolean() && evidence.AbortReported == vector.GetProperty("abortReported").GetBoolean(), "Raw termination observations survive foreign and unresolved scope.");
            Verify(ReferenceEquals(candidate.Context, context) && ReferenceEquals(evidence.Binding, binding) && ReferenceEquals(evidence.Request, candidate) && ReferenceEquals(evidence.Response, response), "Evidence retains the exact candidate, binding and response source.");
            Verify(before.SequenceEqual(response.Frame.Syntax.CopyWireBytes()), "Comparison preserves original response bytes.");
            var references = vector.GetProperty("references").EnumerateArray().ToArray();
            Verify(binding.References.Count == references.Length, "All non-message reply/data references are preserved.");
            for (int i = 0; i < references.Length; i++)
            {
                var actual = binding.References[i]; var expected = references[i];
                int? reference = expected.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("reference").GetInt32();
                Verify(actual.ResponseSegment.Code == expected.GetProperty("code").GetString() && actual.ResponseSegment.Reference == reference && actual.Target?.Role.ToString() == expected.GetProperty("role").GetString(), "Independent reference and actual outgoing role match.");
                Verify(response.BodySegments.Any(s => ReferenceEquals(s, actual.ResponseSegment)) && (actual.Target is null || candidate.Segments.Any(s => ReferenceEquals(s, actual.Target))), "Reference links preserve both original source and immutable target objects.");
            }
            if (evidence.Outcome == FinTsDialogueEndOutcome.AbortReported)
            { Verify(response.HasErrors && response.HasUninterpretedCodes, "Bound abort remains an error/uninterpreted observation in the generic response vocabulary."); }
        }
        Verify(vectors.Length == 46, "All forty-six independent closing response vectors ran.");
        // Compare the metadata map with actual synthetic output for all distinct positive closing contexts.
        foreach (var vector in vectors.Take(5))
        {
            var context = FinTsPinTanDialogueEndTests.Context(vector.GetProperty("context"));
            var candidate = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(context);
            using var pin = FinTsSessionCredential.CaptureAndClear(Convert.FromBase64String(vector.GetProperty("context").GetProperty("pinBase64").GetString()!), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
            byte[] output = new byte[FinTsPinTanDialogueEndWriter.MaximumEncodedLength];
            try
            {
                Verify(FinTsPinTanDialogueEndWriter.TryEncode(context, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written, "Bound closing candidate is accepted by the actual encoder.");
                var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written));
                var outer = frame.Syntax.Segments; var inner = FinTsSyntax.ParseSegments(outer[2].Fields[0].Elements[0].CopyValueBytes()).Segments;
                var actual = outer.Take(3).Concat(inner).Concat(outer.TakeLast(1)).ToArray();
                Verify(actual.Length == candidate.Segments.Count && actual.Zip(candidate.Segments).All(pair => pair.First.Code == pair.Second.Code && pair.First.Number == pair.Second.Number && pair.First.Version == pair.Second.Version), "Every mapped segment code, number and version matches real assembled output.");
                Verify(candidate.MessageNumber == 2 && candidate.ExpectedBankMessageNumber == 2 && candidate.DialogueEndNumber == 3 && candidate.ProfileVersion == context.Header.ProfileVersion, "Closing counters and HKEND role are independent of recovered counters and old unsigned positions.");
            }
            finally { CryptographicOperations.ZeroMemory(output); }
        }
        var basis = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndTests.Context(vectors[0].GetProperty("context")));
        var reply = Response(vectors[0]); var bound = FinTsPinTanDialogueEndResponseBinding.Evaluate(basis, reply);
        var foreign = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndTests.Context(vectors[2].GetProperty("context")));
        var foreignEvidence = FinTsPinTanDialogueEndEvidence.Evaluate(FinTsPinTanDialogueEndResponseBinding.Evaluate(foreign, reply));
        Verify(foreignEvidence.Issues == (FinTsPinTanDialogueEndIssue.BindingNeedsReview | FinTsPinTanDialogueEndIssue.EnvelopeIdentityMismatch) && foreignEvidence.CloseReported && foreignEvidence.Outcome == FinTsDialogueEndOutcome.NeedsReview, "A different dialogue/system candidate cannot inherit a successful closing response.");
        var reparsed = Response(vectors[0]); var repeated = FinTsPinTanDialogueEndEvidence.Evaluate(FinTsPinTanDialogueEndResponseBinding.Evaluate(basis, reparsed));
        Verify(repeated.HasMatchingEvidence && ReferenceEquals(repeated.Response, reparsed) && !ReferenceEquals(repeated.Response, reply), "Reparsed bytes create independent source evidence; comparison offers no replay consumption.");
        using (var closing = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-dialogue-end-v1.json")!))
        {
            foreach (var vector in closing.RootElement.GetProperty("vectors").EnumerateArray().Skip(6))
            {
                try { FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndTests.Context(vector)); Verify(false, "Review context must not create a candidate binding."); }
                catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext, "All unresolved closing contexts fail before candidate binding."); }
            }
        }
        try { ((IList<FinTsPinTanRequestSegmentBinding>)basis.Segments).Clear(); Verify(false, "Candidate mapping must be immutable."); }
        catch (NotSupportedException) { Verify(true, "Candidate segment mapping is read-only."); }
        try { ((IList<FinTsPinTanResponseSegmentBinding>)bound.References).Clear(); Verify(false, "Reference mapping must be immutable."); }
        catch (NotSupportedException) { Verify(true, "Response reference mapping is read-only."); }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            foreach (int operation in Enumerable.Range(0, 3))
            {
                try
                {
                    if (operation == 0) { FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(basis.Context, cancellation.Token); }
                    else if (operation == 1) { FinTsPinTanDialogueEndResponseBinding.Evaluate(basis, reply, cancellation.Token); }
                    else { FinTsPinTanDialogueEndEvidence.Evaluate(bound, cancellation.Token); }
                    Verify(false, "Cancelled comparison must throw.");
                }
                catch (OperationCanceledException) { Verify(true, "Cancellation publishes no candidate, binding or termination result."); }
            }
        }
        foreach (int operation in Enumerable.Range(0, 4))
        {
            try
            {
                if (operation == 0) { FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(null!); }
                else if (operation == 1) { FinTsPinTanDialogueEndResponseBinding.Evaluate(null!, reply); }
                else if (operation == 2) { FinTsPinTanDialogueEndResponseBinding.Evaluate(basis, null!); }
                else { FinTsPinTanDialogueEndEvidence.Evaluate(null!); }
                Verify(false, "Required null inputs must fail.");
            }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-USER", StringComparison.Ordinal), "Null diagnostics contain no identity values."); }
        }
        var concurrent = new FinTsPinTanDialogueEndEvidence[16];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = FinTsPinTanDialogueEndEvidence.Evaluate(bound));
        Verify(concurrent.All(e => e.Outcome == FinTsDialogueEndOutcome.ClosureReported && ReferenceEquals(e.Binding, bound)), "Concurrent comparison is pure and does not consume a response or close a session.");
        Console.WriteLine($"FinTS PIN/TAN closing response binding/termination verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum => (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0, (flags, value) => flags | Convert.ToInt32(Enum.Parse<T>(value.GetString()!))));
    private static FinTsResponse Response(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        return vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }
}
