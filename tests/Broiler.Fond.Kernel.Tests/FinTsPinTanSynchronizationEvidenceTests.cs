using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSynchronizationEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-evidence-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var binding = Binding(vector); var data = FinTsSynchronizationDataSet.Parse(binding.Response); var recovery = Recovery(vector);
            byte[] original = binding.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanSynchronizationEvidence.Evaluate(binding, data, recovery);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanSynchronizationIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsPinTanSynchronizationIssue>(value.GetString()!));
            var details = vector.GetProperty("synchronizationIssues").EnumerateArray().Aggregate(FinTsSynchronizationIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsSynchronizationIssue>(value.GetString()!));
            Verify(result.Issues == expected && result.SynchronizationIssues == details, "Independent synchronization semantics match: " + vector.GetProperty("name").GetString() + " (" + result.Issues + "/" + result.SynchronizationIssues + ").");
            bool matching = expected == FinTsPinTanSynchronizationIssue.None;
            Verify(result.HasMatchingEvidence == matching && result.NextStep == (matching ? FinTsSynchronizationNextStep.CloseAndReinitializeRequired : FinTsSynchronizationNextStep.StopForReview), "Matching reports always require closing and reinitializing; unresolved reports require review.");
            Verify(matching ? ReferenceEquals(result.MatchingReport, data.Reports.Single()) : result.MatchingReport is null, "Only a unique completely matching report is selected, without copying or discarding candidates.");
            Verify(ReferenceEquals(result.Binding, binding) && ReferenceEquals(result.Data, data) && ReferenceEquals(result.Response, binding.Response) && ReferenceEquals(result.Request, binding.Request) && ReferenceEquals(result.RecoveryContext, recovery), "Exact binding, source reports and caller recovery context are retained.");
            Verify(data.Reports.Count == vector.GetProperty("reportCount").GetInt32() && result.ReportedDialogueId == binding.ReportedDialogueId && original.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()), "Every original report, dialogue and response byte remains unchanged on all outcomes.");
            Verify(FinTsPinTanSynchronizationEvidence.Evaluate(binding, data, recovery).Issues == result.Issues, "Semantic comparison consumes no response or recovered value.");
            if (matching)
            {
                var old = FinTsSynchronizationEvidence.Evaluate(binding.Request.SignatureEvidence.Request.Synchronization!, data,
                    binding.Request.ProfileVersion == 1 ? FinTsSynchronizationProfile.PinTan1 : FinTsSynchronizationProfile.PinTan2, recovery);
                Verify(!old.HasMatchingEvidence && old.Issues.HasFlag(FinTsSynchronizationIssue.EnvelopeNeedsReview), "The unsigned synchronization comparator keeps its envelope restrictions after shared report-check extraction.");
            }
        }
        Verify(vectors.Length == 33, "All thirty-three independent synchronization fixtures ran.");
        var basis = Binding(vectors[0]); var dataBasis = FinTsSynchronizationDataSet.Parse(basis.Response);
        var clone = Binding(vectors[0]); var foreignData = FinTsSynchronizationDataSet.Parse(clone.Response);
        var crossed = FinTsPinTanSynchronizationEvidence.Evaluate(basis, foreignData);
        Verify(crossed.Issues == FinTsPinTanSynchronizationIssue.ReportSourceMismatch && crossed.SynchronizationIssues == FinTsSynchronizationIssue.None && crossed.MatchingReport is null, "Byte-identical reports parsed from another response instance cannot be combined with a binding.");
        Verify(ReferenceEquals(crossed.Data, foreignData) && ReferenceEquals(crossed.Response, basis.Response), "Rejected source mixing remains inspectable without rewritten provenance.");
        Verify(FinTsPinTanSynchronizationEvidence.Evaluate(basis, FinTsSynchronizationDataSet.Parse(basis.Response)).HasMatchingEvidence, "Reparsing reports from the exact bound response retains provenance.");
        using (var initializationDocument = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-initialization-evidence-v1.json")!))
        {
            var initial = Binding(initializationDocument.RootElement.GetProperty("vectors")[0]);
            var wrong = FinTsPinTanSynchronizationEvidence.Evaluate(initial, FinTsSynchronizationDataSet.Parse(initial.Response));
            Verify(wrong.Issues == FinTsPinTanSynchronizationIssue.RequestKindMismatch && wrong.SynchronizationIssues == FinTsSynchronizationIssue.None && wrong.MatchingReport is null, "An initialization candidate cannot be interpreted as synchronization.");
            Verify(FinTsPinTanSynchronizationEvidence.Evaluate(initial, dataBasis).Issues == (FinTsPinTanSynchronizationIssue.RequestKindMismatch | FinTsPinTanSynchronizationIssue.ReportSourceMismatch), "Kind and source mismatches remain independent.");
        }
        var maximum = Binding(vectors[4]); var maximumResult = FinTsPinTanSynchronizationEvidence.Evaluate(maximum, FinTsSynchronizationDataSet.Parse(maximum.Response), Recovery(vectors[4]));
        Verify(maximumResult.MatchingReport!.LastMessageNumber == 9999 && maximumResult.NextStep == FinTsSynchronizationNextStep.CloseAndReinitializeRequired, "The exact upper counter is retained without incrementing, retrying or activating the dialogue.");
        var escaped = Binding(vectors[2]);
        Verify(FinTsPinTanSynchronizationEvidence.Evaluate(escaped, FinTsSynchronizationDataSet.Parse(escaped.Response)).MatchingReport!.SystemId == "PUBLIC+:'?@ü" && escaped.Request.SignatureEvidence.Header.SystemId == "0", "Assigned escaped system identity remains a separate report and does not overwrite the request's zero placeholder.");
        // Exercise the newly covered message-recovery candidate through the real local writer with synthetic PIN bytes.
        using (var pin = FinTsSessionCredential.CaptureAndClear("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1)))
        {
            byte[] output = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
            try { Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(maximum.Request.SignatureEvidence, pin, null, output, out int written) == FinTsPinTanRequestWriteResult.Written && written > 0, "Message recovery uses the same assembled candidate metadata and credential-aware envelope writer."); }
            finally { CryptographicOperations.ZeroMemory(output); }
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanSynchronizationEvidence.Evaluate(basis, dataBasis, cancellationToken: cancellation.Token); Verify(false, "Cancelled comparison must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation publishes no synchronization result."); }
        }
        foreach (bool nullBinding in new[] { false, true })
        {
            try { FinTsPinTanSynchronizationEvidence.Evaluate(nullBinding ? null! : basis, nullBinding ? dataBasis : null!); Verify(false, "Required null inputs must fail."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-USER", StringComparison.Ordinal), "Null diagnostics exclude caller identities."); }
        }
        var repeated = new FinTsPinTanSynchronizationEvidence[16];
        Parallel.For(0, repeated.Length, i => repeated[i] = FinTsPinTanSynchronizationEvidence.Evaluate(basis, dataBasis));
        Verify(repeated.All(r => r.HasMatchingEvidence && ReferenceEquals(r.MatchingReport, dataBasis.Reports[0]) && r.NextStep == FinTsSynchronizationNextStep.CloseAndReinitializeRequired), "Concurrent comparison is pure and never consumes a matching report or bypasses reinitialization.");
        Console.WriteLine($"FinTS assembled PIN/TAN synchronization semantics verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsSynchronizationRecoveryContext? Recovery(JsonElement vector) => vector.GetProperty("previousDialogueId").GetString() is { } previous
        ? new(previous, vector.GetProperty("lastSubmittedMessageNumber").GetInt32()) : null;
    private static FinTsPinTanResponseBinding Binding(JsonElement vector)
    {
        var signature = FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context"));
        var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(signature);
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
        return FinTsPinTanResponseBinding.Evaluate(request, response);
    }
}
