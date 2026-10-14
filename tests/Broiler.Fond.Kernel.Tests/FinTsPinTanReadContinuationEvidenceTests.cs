using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadContinuationEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-continuation-semantics-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            if (vector.TryGetProperty("parseError", out var parseError))
            {
                try { Inputs(vector); Verify(false, "Invalid response text must fail before semantic comparison."); }
                catch (FinTsFormatException error) { Verify(error.Error.ToString() == parseError.GetString(), "Control bytes and oversized parameters are rejected at the response schema boundary."); }
                continue;
            }
            var (binding, data) = Inputs(vector); byte[] before = binding.Response.Frame.Syntax.CopyWireBytes();
            byte[] requestBefore = binding.Request.Context.Request.Frame.Syntax.CopyWireBytes();
            byte[] previousBefore = binding.Request.Context.Continuation.Previous!.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanReadContinuationEvidence.Evaluate(binding, data);
            Verify(result.Issues == Flags<FinTsPinTanReadIssue>(vector, "issues"), "Independent outer read issues: " + vector.GetProperty("name").GetString() + " (" + result.Issues + ").");
            Verify(result.ReadIssues == Flags<FinTsReadContextIssue>(vector, "readIssues"), "Independent semantic read issues: " + vector.GetProperty("name").GetString() + " (" + result.ReadIssues + ").");
            Verify(result.PageNumber == 2, "Continuation semantics retain the qualified second-page scope.");
            Verify(result.Outcome.ToString() == vector.GetProperty("outcome").GetString() && result.HasMatchingEvidence == (result.Issues == FinTsPinTanReadIssue.None), "Qualified outcomes never escape review issues.");
            Verify(result.ReportedContinuationToken == vector.GetProperty("token").GetString(), "Raw same-source continuation observations preserve exact tokens even on review results.");
            Verify(ReferenceEquals(result.Binding, binding) && ReferenceEquals(result.Request, binding.Request) && ReferenceEquals(result.Response, binding.Response) && ReferenceEquals(result.ReadData, data), "Semantic evidence retains the exact binding, request, response and schema instances.");
            Verify(result.HasMatchingEvidence ? ReferenceEquals(result.MatchingReadData, data) : result.MatchingReadData is null, "Only fully matching evidence exposes qualified read data.");
            Verify(requestBefore.SequenceEqual(binding.Request.Context.Request.Frame.Syntax.CopyWireBytes()) &&
                previousBefore.SequenceEqual(binding.Request.Context.Continuation.Previous!.Response.Frame.Syntax.CopyWireBytes()) && before.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()) && data.Discovery.Select(r => r.Source).Concat(data.Balances.Select(r => r.Source)).All(s => binding.Response.BodySegments.Any(raw => ReferenceEquals(raw, s))), "Typed reports retain original segments and unchanged response wire.");
            Verify(FinTsPinTanReadContinuationEvidence.Evaluate(binding, data).Issues == result.Issues, "Repeated semantic comparison does not consume the response.");
        }
        Verify(vectors.Length == 74, "All seventy-four independent continuation-semantic fixtures ran.");
        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanReadContinuationEvidence Evidence(string name)
        { var (binding, data) = Inputs(Named(name)); return FinTsPinTanReadContinuationEvidence.Evaluate(binding, data); }
        var basis = Evidence("balance-seven");
        var predecessor = basis.Request.Context.Continuation.Previous!;
        var priorSourceMismatch = FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, predecessor.ReadData);
        Verify(priorSourceMismatch.Issues == FinTsPinTanReadIssue.ReadSourceMismatch && priorSourceMismatch.ReportedContinuationToken is null &&
            priorSourceMismatch.MatchingReadData is null, "First-page typed data and token cannot qualify as second-page response evidence.");
        var priorBinding = FinTsPinTanReadContinuationResponseBinding.Evaluate(basis.Request, predecessor.Response);
        Verify(FinTsPinTanReadContinuationEvidence.Evaluate(priorBinding, predecessor.ReadData).Issues == FinTsPinTanReadIssue.BindingNeedsReview,
            "Matching account data from the predecessor cannot override its message-2 binding mismatch.");
        var changed = Evidence("second-page-different-amount");
        Verify(changed.HasMatchingEvidence && changed.ReadData.Balances.Single().Booked.Amount.Value == 2 &&
            changed.Request.Context.Continuation.Previous!.ReadData.Balances.Single().Booked.Amount.Value == 1,
            "Page observations remain separate; the second page neither overwrites nor sums the predecessor balance.");
        var repeated = Evidence("repeated-token-observation");
        Verify(repeated.Outcome == FinTsReadOutcomeObservation.PartialReported && repeated.ReportedContinuationToken == repeated.Request.Context.Request.Request.ContinuationToken,
            "An opaque repeated token stays an observation; semantic matching does not prove pagination progress or authorize another request.");
        var zero = Evidence("real-zero-balance");
        Verify(zero.Outcome == FinTsReadOutcomeObservation.ExecutionReported && zero.ReadData.Balances.Single().Booked.Amount.Value == 0 && zero.ReadData.Balances.Single().Pending is null,
            "Real zero remains a reported value; absent optional balances remain absent.");
        var unavailable = Evidence("balance-unavailable");
        Verify(unavailable.Outcome == FinTsReadOutcomeObservation.UnavailableReported && unavailable.ReadData.Balances.Count == 0 && unavailable.Response.HasUninterpretedCodes,
            "Scoped 3010 supplies no invented balance and remains uninterpreted in the generic response vocabulary.");
        var optional = Evidence("separate-optional-values").ReadData.Balances.Single();
        Verify(optional.Booked.Amount.Value == 1 && optional.Pending!.Amount.Value == 2 && optional.Pending.SignedValue == -2 && optional.Available!.Value == 3 && optional.CreditLine is null && optional.BookingTimestamp is not null,
            "Optional and source-dated values remain separate without derived balances.");
        Verify(Evidence("execution-and-partial").ReportedContinuationToken == "P+:'?@ü" && Evidence("foreign-message-retains-token").MatchingReadData is null,
            "Opaque delimiters survive token decoding; foreign scope withholds qualified data.");
        var old = Evidence("old-data-reference");
        Verify(old.ReadIssues == FinTsReadContextIssue.None && old.Outcome == FinTsReadOutcomeObservation.NeedsReview && old.Binding.References.Single(r => r.ResponseSegment.Code == "HISAL").Target!.Role == FinTsPinTanRequestSegmentRole.SignatureHeader,
            "Valid payload/account evidence cannot override old unsigned references to HNSHK.");
        foreach (string name in new[] { "foreign-envelope-identity", "wrong-profile", "wrong-version", "security-success-not-read-success", "extra-security-success" })
        { Verify(Evidence(name).MatchingReadData is null, "Unresolved envelope/version/security observations cannot expose qualified read data."); }

        // Even a second schema parsed from byte-identical response bytes is a different source tree.
        var reparsed = Inputs(vectors[0]);
        var mismatch = FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, reparsed.Data);
        Verify(mismatch.Issues == FinTsPinTanReadIssue.ReadSourceMismatch && mismatch.ReadIssues == FinTsReadContextIssue.None && mismatch.MatchingReadData is null &&
            mismatch.ReportedContinuationToken is null && ReferenceEquals(mismatch.ReadData, reparsed.Data), "Mixed response trees are retained as raw inputs but rejected before semantic interpretation.");
        var foreignPartial = Inputs(Named("partial-balance"));
        Verify(FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, foreignPartial.Data).ReportedContinuationToken is null, "A foreign schema cannot supply a continuation token.");
        var sameSourceReparse = FinTsReadDataSet.Parse(basis.Response);
        Verify(FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, sameSourceReparse).HasMatchingEvidence, "A second typed view of the same exact response is admissible.");
        var badScope = Evidence("foreign-message");
        Verify(FinTsPinTanReadContinuationEvidence.Evaluate(badScope.Binding, basis.ReadData).Issues == (FinTsPinTanReadIssue.BindingNeedsReview | FinTsPinTanReadIssue.ReadSourceMismatch), "Source mismatch does not erase existing binding issues.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, basis.ReadData, cancellation.Token); Verify(false, "Pre-cancelled semantics must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation publishes no semantic result."); }
        }
        foreach (bool missingBinding in new[] { false, true })
        {
            try { FinTsPinTanReadContinuationEvidence.Evaluate(missingBinding ? null! : basis.Binding, missingBinding ? basis.ReadData : null!); Verify(false, "Null inputs must throw."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Fixed argument diagnostics contain no source values."); }
        }
        var results = new FinTsPinTanReadContinuationEvidence[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, basis.ReadData));
        Verify(results.All(r => r.HasMatchingEvidence && ReferenceEquals(r.ReadData, basis.ReadData)), "Concurrent evaluations retain immutable exact-source data without replay consumption.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadContinuationEvidence.Evaluate(basis.Binding, basis.ReadData).HasMatchingEvidence, "Semantic comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine($"FinTS assembled continuation response semantics passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum => (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0,
        (flags, value) => flags | Convert.ToInt32(Enum.Parse<T>(value.GetString()!), CultureInfo.InvariantCulture)));
    internal static (FinTsPinTanReadContinuationResponseBinding Binding, FinTsReadDataSet Data) Inputs(JsonElement vector)
    {
        var request = FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationPinTrailerTests.Context(vector.GetProperty("candidate")));
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
        return (FinTsPinTanReadContinuationResponseBinding.Evaluate(request, response), FinTsReadDataSet.Parse(response));
    }
}
