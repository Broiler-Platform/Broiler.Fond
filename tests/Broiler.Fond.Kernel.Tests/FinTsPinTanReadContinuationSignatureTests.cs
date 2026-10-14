using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadContinuationSignatureTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-continuation-signature-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (continuation, header, selection, control) = Inputs(vector);
            byte[] requestBefore = continuation.Request.Frame.Syntax.CopyWireBytes(); byte[] previousBefore = continuation.Previous!.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanReadContinuationSignatureContext.Evaluate(continuation, header, selection, control);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanReadContinuationSignatureIssue.None,
                (flags, value) => flags | Enum.Parse<FinTsPinTanReadContinuationSignatureIssue>(value.GetString()!));
            Verify(result.Issues == expected && result.HasMatchingEvidence == (expected == FinTsPinTanReadContinuationSignatureIssue.None), "Independent continuation signature issues: " + vector.GetProperty("name").GetString() + " (" + result.Issues + ").");
            Verify(result.OperationEvidence.ToString() == vector.GetProperty("operationEvidence").GetString(), "Original operation requirements are qualified only on a complete signature-context match.");
            Verify(ReferenceEquals(result.Continuation, continuation) && ReferenceEquals(result.Request, continuation.Request) && ReferenceEquals(result.Header, header) && ReferenceEquals(result.Selection, selection) && result.ControlReference == control,
                "All raw caller context, header, selection and control observations retain exact identity and values.");
            Verify(requestBefore.SequenceEqual(result.Request.Frame.Syntax.CopyWireBytes()) && previousBefore.SequenceEqual(result.Continuation.Previous!.Response.Frame.Syntax.CopyWireBytes()), "Context comparison changes neither request nor predecessor response bytes.");
            var fields = vector.GetProperty("header");
            Verify(result.Header.ControlReference == fields.GetProperty("controlReference").GetString() && result.Header.SecurityReferenceNumber.ToString(CultureInfo.InvariantCulture) == fields.GetProperty("securityReferenceNumber").GetString() &&
                result.Header.KeyNumber == fields.GetProperty("keyNumber").GetInt32() && result.Header.KeyVersion == fields.GetProperty("keyVersion").GetInt32(), "Explicit header reference/key values are preserved without allocation or increment.");
            Verify(result.Header.SecurityDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) == fields.GetProperty("securityDate").GetString() &&
                result.Header.SecurityTime?.ToString("HHmmss", CultureInfo.InvariantCulture) == fields.GetProperty("securityTime").GetString(), "Supplied optional timestamps remain observations without freshness claims.");
            if (result.HasMatchingEvidence)
            {
                var origin = continuation.Origin.Signature;
                Verify(ReferenceEquals(result.MatchingOrigin, continuation.Origin) && ReferenceEquals(result.MatchingAdvertisement, origin.MatchingAdvertisement) &&
                    ReferenceEquals(result.MatchingProcedure, origin.MatchingProcedure) && ReferenceEquals(result.MatchingRequirements, origin.MatchingRequirements),
                    "Qualified requirements and procedures are the exact inherited returned objects, never a fallback or reparse.");
                Verify((result.MatchingProcedure is null) == (selection.ProfileVersion == 1), "One-step context invents no two-step procedure.");
            }
            else
            {
                Verify(result.MatchingOrigin is null && result.MatchingAdvertisement is null && result.MatchingProcedure is null && result.MatchingRequirements is null,
                    "Any continuation or header issue withholds every matching object.");
            }
        }
        Verify(vectors.Length == 69, "All sixty-nine independent continuation signature fixtures ran.");
        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanReadContinuationSignatureContext Context(string name)
        { var (continuation, header, selection, control) = Inputs(Named(name)); return FinTsPinTanReadContinuationSignatureContext.Evaluate(continuation, header, selection, control); }
        var basis = Context("balance-seven-second-page");
        var missing = Context("missing-pin-bounds-retained");
        Verify(missing.HasMatchingEvidence && missing.MatchingRequirements!.MinimumPinLength is null, "Unknown PIN bounds stay unknown; signature context proves no credential completeness.");
        Verify(Context("reported-n-does-not-waive-sca").OperationEvidence == FinTsPinTanOperationEvidence.TanReportedNotRequired, "Reported N remains an operation observation, not a TAN/SCA waiver.");
        Verify(Context("reused-control-is-not-replay-proof").HasMatchingEvidence && Context("maximum-security-reference").Header.SecurityReferenceNumber == 9999999999999999UL,
            "Control reuse and exact maximum security-reference observations create no replay gate or counter arithmetic.");
        var originalSelection = basis.Continuation.Origin.Signature.Selection;
        var equivalent = new FinTsPinTanSignatureSelection(originalSelection.ProfileVersion, originalSelection.SecurityFunction, originalSelection.TanSegmentVersion);
        Verify(FinTsPinTanReadContinuationSignatureContext.Evaluate(basis.Continuation, basis.Header, equivalent, basis.ControlReference).HasMatchingEvidence && !ReferenceEquals(equivalent, originalSelection),
            "Explicit selection values must match; an equivalent value object does not replace inherited procedure evidence.");
        using (var attempt = new FinTsPinTanReadAttempt(TimeSpan.FromMinutes(1)))
        {
            var previous = basis.Continuation.Previous!;
            attempt.Start(previous.Request); var handoff = attempt.AcceptResponse(previous.ReadData);
            var continuation = FinTsPinTanReadContinuationContext.Evaluate(handoff.Evidence!, basis.Request);
            var signature = FinTsPinTanReadContinuationSignatureContext.Evaluate(continuation, basis.Header, basis.Selection, basis.ControlReference);
            attempt.Dispose();
            Verify(signature.HasMatchingEvidence && ReferenceEquals(signature.Continuation.Previous, handoff.Evidence) && ReferenceEquals(signature.MatchingRequirements, basis.MatchingRequirements),
                "The actual partial handoff supplies exact retained capability/requirement provenance through signature comparison.");
            Verify(attempt.AcceptResponse(previous.ReadData).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanReadAttemptState.PartialReported,
                "Signature context does not reopen the completed first-read owner or repeat handoff.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadContinuationSignatureContext.Evaluate(basis.Continuation, basis.Header, basis.Selection, basis.ControlReference, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation returns no signature context."); }
        }
        foreach (int missingInput in Enumerable.Range(0, 4))
        {
            try
            {
                FinTsPinTanReadContinuationSignatureContext.Evaluate(missingInput == 0 ? null! : basis.Continuation, missingInput == 1 ? null! : basis.Header,
                    missingInput == 2 ? null! : basis.Selection, missingInput == 3 ? null! : basis.ControlReference);
                Verify(false, "Null required input must throw.");
            }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Null input uses fixed diagnostics without source fields."); }
        }
        foreach (string control in new[] { "", "0", " PAGE2-REF", "PAGE2-REF ", "123456789012345", "PUBLIC\nSECRET", "\u00a0", "\u0100" })
        {
            try { FinTsPinTanReadContinuationSignatureContext.Evaluate(basis.Continuation, basis.Header, basis.Selection, control); Verify(false, "Invalid control must throw."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext && !error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Invalid control fields fail with fixed diagnostics before qualification."); }
        }
        var results = new FinTsPinTanReadContinuationSignatureContext[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanReadContinuationSignatureContext.Evaluate(basis.Continuation, basis.Header, basis.Selection, basis.ControlReference));
        Verify(results.All(r => r.HasMatchingEvidence && ReferenceEquals(r.MatchingRequirements, basis.MatchingRequirements) && ReferenceEquals(r.Header, basis.Header)),
            "Repeated concurrent evaluation preserves exact immutable source evidence without consumption.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadContinuationSignatureContext.Evaluate(basis.Continuation, basis.Header, basis.Selection, basis.ControlReference).HasMatchingEvidence, "Continuation signature selection comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine($"FinTS balance-continuation PIN/TAN signature context passed ({vectors.Length} independent vectors, {count} checks).");
    }
    internal static (FinTsPinTanReadContinuationContext Continuation, FinTsPinTanSignatureHeader Header, FinTsPinTanSignatureSelection Selection, string Control) Inputs(JsonElement vector)
    {
        var (previous, request) = FinTsPinTanReadContinuationTests.Inputs(vector.GetProperty("continuation"));
        var continuation = FinTsPinTanReadContinuationContext.Evaluate(previous, request);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(vector.GetProperty("header").GetProperty("wireBase64").GetString()!)).Segments.Single());
        var selection = new FinTsPinTanSignatureSelection(vector.GetProperty("selectionProfile").GetInt32(), vector.GetProperty("selectionFunction").GetInt32(), vector.GetProperty("selectionVersion").GetInt32());
        return (continuation, header, selection, vector.GetProperty("controlReference").GetString()!);
    }
}
