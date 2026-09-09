using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanInitializationEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-initialization-evidence-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var binding = Binding(vector); var parameters = FinTsParameterSet.Parse(binding.Response);
            byte[] original = binding.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanInitializationEvidence.Evaluate(binding, parameters);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanInitializationIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsPinTanInitializationIssue>(value.GetString()!));
            var expectedParameters = vector.GetProperty("parameterIssues").EnumerateArray().Aggregate(FinTsInitializationIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsInitializationIssue>(value.GetString()!));
            Verify(result.Issues == expected && result.ParameterIssues == expectedParameters, "Independent initialization semantics match: " + vector.GetProperty("name").GetString() + " (" + result.Issues + "/" + result.ParameterIssues + ").");
            Verify(result.HasMatchingEvidence == (expected == FinTsPinTanInitializationIssue.None) && result.Outcome == (expected == FinTsPinTanInitializationIssue.None ? FinTsInitializationOutcome.ExecutionReported : FinTsInitializationOutcome.NeedsReview), "Every semantic issue withholds matching evidence and execution classification.");
            Verify(ReferenceEquals(result.Binding, binding) && ReferenceEquals(result.Request, binding.Request) && ReferenceEquals(result.Response, binding.Response) && ReferenceEquals(result.Parameters, parameters), "Semantic evidence retains exact supplied binding and parameter source instances.");
            Verify(result.BankVersionChanged == OptionalBool(vector.GetProperty("bankVersionChanged")) && result.UserVersionChanged == OptionalBool(vector.GetProperty("userVersionChanged")), "Untrusted version observations preserve absent, unchanged and changed values on all outcomes.");
            Verify(result.ReportedDialogueId == binding.ReportedDialogueId && original.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()), "Semantic comparison preserves reported dialogue and every original response byte.");
            Verify(FinTsPinTanInitializationEvidence.Evaluate(binding, parameters).Issues == result.Issues, "Pure comparison consumes no response or candidate metadata.");
            if (result.HasMatchingEvidence)
            {
                var unsigned = FinTsInitializationEvidence.Evaluate(binding.Request.SignatureEvidence.Request.Initialization!, parameters, binding.Request.ExpectedUserId);
                Verify(!unsigned.HasMatchingEvidence && unsigned.Issues.HasFlag(FinTsInitializationIssue.ProfileNeedsReview), "The unsigned comparator remains restricted and is not silently reused for wrapped signed references.");
            }
        }
        Verify(vectors.Length == 30, "All thirty independent assembled initialization fixtures ran.");
        var basis = Binding(vectors[0]); var basisParameters = FinTsParameterSet.Parse(basis.Response);
        var clone = Binding(vectors[0]); var cloneParameters = FinTsParameterSet.Parse(clone.Response);
        var crossed = FinTsPinTanInitializationEvidence.Evaluate(basis, cloneParameters);
        Verify(crossed.Issues == FinTsPinTanInitializationIssue.ParameterSourceMismatch && crossed.ParameterIssues == FinTsInitializationIssue.None && !crossed.HasMatchingEvidence && crossed.BankVersionChanged is null && crossed.UserVersionChanged is null, "Even byte-identical parameters from another response instance cannot be mixed into a bound result.");
        Verify(ReferenceEquals(crossed.Parameters, cloneParameters) && ReferenceEquals(crossed.Response, basis.Response), "Rejected source mixing remains inspectable without rewriting provenance.");
        Verify(FinTsPinTanInitializationEvidence.Evaluate(basis, FinTsParameterSet.Parse(basis.Response)).HasMatchingEvidence, "Reparsing parameters from the exact bound response retains valid provenance.");
        using (var otherDocument = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-request-binding-v1.json")!))
        {
            var sync = Binding(otherDocument.RootElement.GetProperty("vectors")[2]);
            var wrongKind = FinTsPinTanInitializationEvidence.Evaluate(sync, FinTsParameterSet.Parse(sync.Response));
            Verify(wrongKind.Issues == FinTsPinTanInitializationIssue.RequestKindMismatch && wrongKind.ParameterIssues == FinTsInitializationIssue.None && wrongKind.BankVersionChanged is null && wrongKind.UserVersionChanged is null, "Synchronization evidence cannot be interpreted as initialization or expose misleading parameter comparisons.");
            var both = FinTsPinTanInitializationEvidence.Evaluate(sync, basisParameters);
            Verify(both.Issues == (FinTsPinTanInitializationIssue.RequestKindMismatch | FinTsPinTanInitializationIssue.ParameterSourceMismatch), "Kind and source errors are independently preserved.");
            var foreign = Binding(otherDocument.RootElement.GetProperty("vectors")[22]);
            Verify(foreign.HasMatchingReferences && FinTsPinTanInitializationEvidence.Evaluate(foreign, FinTsParameterSet.Parse(foreign.Response)).ParameterIssues.HasFlag(FinTsInitializationIssue.UserMismatch), "Semantic comparison now rejects the foreign-user case that reference binding intentionally leaves unresolved.");
        }
        var signature = Binding(vectors[14]);
        Verify(signature.HasMatchingReferences && FinTsPinTanInitializationEvidence.Evaluate(signature, FinTsParameterSet.Parse(signature.Response)).Issues == FinTsPinTanInitializationIssue.StatusNeedsReview, "Signature success plus preparation success cannot stand in for identification execution.");
        var zero = Binding(vectors[3]); var zeroParameters = FinTsParameterSet.Parse(zero.Response);
        Verify(FinTsPinTanInitializationEvidence.Evaluate(zero, zeroParameters).HasMatchingEvidence && zeroParameters.User!.IsDialogueScoped, "UPD zero remains explicitly dialogue-scoped without creating reusable cache state.");
        var update = Binding(vectors[10]); var updateResult = FinTsPinTanInitializationEvidence.Evaluate(update, FinTsParameterSet.Parse(update.Response));
        Verify(updateResult.HasMatchingEvidence && updateResult.BankVersionChanged == true && updateResult.UserVersionChanged == true && updateResult.Response.HasUninterpretedCodes, "Scoped 3050 accepts returned update evidence without broadening generic reply-code meanings.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanInitializationEvidence.Evaluate(basis, basisParameters, cancellation.Token); Verify(false, "Cancelled semantic comparison must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation publishes no semantic result."); }
        }
        foreach (bool nullBinding in new[] { false, true })
        {
            try { FinTsPinTanInitializationEvidence.Evaluate(nullBinding ? null! : basis, nullBinding ? basisParameters : null!); Verify(false, "Required null inputs must fail."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-USER", StringComparison.Ordinal), "Null diagnostics do not expose caller identity."); }
        }
        var results = new FinTsPinTanInitializationEvidence[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanInitializationEvidence.Evaluate(basis, basisParameters));
        Verify(results.All(r => r.HasMatchingEvidence && ReferenceEquals(r.Binding, basis) && ReferenceEquals(r.Parameters, basisParameters)), "Concurrent pure evaluation has stable issues and exact source retention.");
        Console.WriteLine($"FinTS assembled PIN/TAN initialization semantics verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static bool? OptionalBool(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetBoolean();
    private static FinTsPinTanResponseBinding Binding(JsonElement vector)
    {
        var signature = FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context"));
        var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(signature);
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
        return FinTsPinTanResponseBinding.Evaluate(request, response);
    }
}
