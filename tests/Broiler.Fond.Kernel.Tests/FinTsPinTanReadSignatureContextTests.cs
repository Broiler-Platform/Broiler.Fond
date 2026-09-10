using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSignatureContextTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-signature-context-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var result = Evaluate(vector);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanReadSignatureIssue.None,
                (flags, issue) => flags | Enum.Parse<FinTsPinTanReadSignatureIssue>(issue.GetString()!));
            Verify(result.Issues == expected && result.HasMatchingEvidence == (expected == 0), "Independent first-read signature flags: " + vector.GetProperty("name").GetString());
            Verify(result.OperationEvidence.ToString() == vector.GetProperty("operationEvidence").GetString(), "TAN requirement observations are qualified only on a full context match.");
            Verify(result.Request.Frame.Syntax.CopyWireBytes().SequenceEqual(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)), "The unsigned read frame is retained byte-for-byte without signature insertion or renumbering.");
            Verify(result.Initialization.Procedures.Initialization.Response.Frame.Syntax.CopyWireBytes().SequenceEqual(Convert.FromBase64String(vector.GetProperty("initialization").GetProperty("responseBase64").GetString()!)), "Returned initialization source bytes remain unchanged.");
            if (result.HasMatchingEvidence)
            {
                Verify(ReferenceEquals(result.MatchingAdvertisement, result.Initialization.Procedures.MatchingAdvertisement) &&
                    ReferenceEquals(result.MatchingRequirements, result.Initialization.MatchingAdvertisement) &&
                    ReferenceEquals(result.MatchingProcedure, result.Initialization.Procedures.MatchingProcedure), "Matching observations are the exact returned advertisement/procedure/requirements objects.");
                Verify(!ReferenceEquals(result.MatchingAdvertisement, result.Initialization.Procedures.Initialization.Request.SignatureEvidence.MatchingAdvertisement), "Returned observations never fall back to the original outgoing signature's earlier advertisement.");
                Verify((result.MatchingProcedure is null) == (result.Selection.ProfileVersion == 1), "One-step context does not invent a two-step procedure.");
            }
            else
            {
                Verify(result.MatchingAdvertisement is null && result.MatchingProcedure is null && result.MatchingRequirements is null, "Any scope or requirement issue withholds all matching objects.");
            }
        }
        Verify(vectors.Length == 37, "All thirty-seven independently generated read-signature fixtures ran.");
        var basis = Evaluate(vectors[3]);
        var repeated = FinTsPinTanReadSignatureContext.Evaluate(basis.Initialization, basis.Request, basis.Header, basis.Selection, basis.ControlReference);
        Verify(ReferenceEquals(repeated.Initialization, basis.Initialization) && ReferenceEquals(repeated.Request, basis.Request) &&
            ReferenceEquals(repeated.Header, basis.Header) && ReferenceEquals(repeated.Selection, basis.Selection), "Comparison retains all exact caller-owned context objects.");
        Verify(repeated.HasMatchingEvidence && basis.HasMatchingEvidence, "Pure comparison is repeatable and is not a replay gate.");
        var noHipins = Evaluate(vectors[30]);
        Verify(noHipins.Initialization.Procedures.HasMatchingEvidence && !noHipins.HasMatchingEvidence, "A matching inner procedure result cannot bypass missing outer HIPINS requirements.");
        var noBounds = Evaluate(vectors[33]);
        Verify(noBounds.HasMatchingEvidence && noBounds.MatchingRequirements!.MinimumPinLength is null && noBounds.MatchingRequirements.MaximumPinLength is null &&
            noBounds.MatchingRequirements.MaximumTanLength is null, "Unknown bounds stay nullable in the returned signature context.");

        var originVector = vectors[3].GetProperty("initialization");
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(originVector);
        var (_, _, _, copiedHipins) = FinTsPinTanInitializationRequirementsTests.Inputs(originVector);
        var mixed = FinTsPinTanInitializationRequirementsEvidence.Evaluate(binding, tan, permissions, copiedHipins);
        var mixedContext = FinTsPinTanReadSignatureContext.Evaluate(mixed, basis.Request, basis.Header, basis.Selection, basis.ControlReference);
        Verify(mixed.Issues.HasFlag(FinTsPinTanInitializationRequirementsIssue.SourceMismatch) && mixedContext.Issues == FinTsPinTanReadSignatureIssue.InitializationNeedsReview &&
            mixedContext.OperationEvidence == FinTsPinTanOperationEvidence.Unknown && mixedContext.MatchingAdvertisement is null, "Identical-byte source mixing remains unqualified in the later signature context.");
        using (var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromMinutes(1)))
        {
            attempt.Start(binding.Request);
            var handoff = attempt.AcceptRequirementsResponse(tan, permissions, hipins);
            var context = FinTsPinTanReadSignatureContext.Evaluate(handoff.RequirementsEvidence!, basis.Request, basis.Header, basis.Selection, basis.ControlReference);
            Verify(handoff.Transition == FinTsInitializationTransition.ExecutionReported && context.HasMatchingEvidence && ReferenceEquals(context.Initialization, handoff.RequirementsEvidence), "The one-time assembled initialization handoff supplies exact later read context.");
            attempt.Dispose();
            Verify(context.HasMatchingEvidence && ReferenceEquals(context.MatchingRequirements, hipins.Advertisements.Single()) &&
                attempt.AcceptRequirementsResponse(tan, permissions, hipins).RequirementsEvidence is null && attempt.GetSnapshot().ResponsesHandled == 1,
                "Caller-owned context survives initialization cleanup without reopening or consuming its handoff again.");
        }
        var concurrent = new FinTsPinTanReadSignatureContext[16];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = FinTsPinTanReadSignatureContext.Evaluate(basis.Initialization, basis.Request, basis.Header, basis.Selection, basis.ControlReference));
        Verify(concurrent.All(c => c.HasMatchingEvidence && ReferenceEquals(c.MatchingAdvertisement, basis.MatchingAdvertisement)), "Concurrent pure comparison preserves source identity.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadSignatureContext.Evaluate(basis.Initialization, basis.Request, basis.Header, basis.Selection, basis.ControlReference).HasMatchingEvidence, "Selection comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadSignatureContext.Evaluate(basis.Initialization, basis.Request, basis.Header, basis.Selection, basis.ControlReference, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Pre-cancellation returns no context."); }
        }
        foreach (int missing in new[] { 0, 1, 2, 3, 4 })
        {
            try
            {
                FinTsPinTanReadSignatureContext.Evaluate(missing == 0 ? null! : basis.Initialization, missing == 1 ? null! : basis.Request,
                    missing == 2 ? null! : basis.Header, missing == 3 ? null! : basis.Selection, missing == 4 ? null! : basis.ControlReference);
                Verify(false, "Null required input must fail.");
            }
            catch (ArgumentNullException) { Verify(true, "Required context input is validated."); }
        }
        foreach (string control in new[] { "", "0", " NEXT-REF", "NEXT-REF ", "123456789012345", "PUBLIC\nSECRET", "\u0100" })
        {
            try { FinTsPinTanReadSignatureContext.Evaluate(basis.Initialization, basis.Request, basis.Header, basis.Selection, control); Verify(false, "Malformed control reference must fail."); }
            catch (FinTsFormatException error) { Verify(!error.ToString().Contains(control, StringComparison.Ordinal) || control.Length < 2, "Malformed control references fail with fixed diagnostics."); }
        }
        Console.WriteLine($"FinTS first-read PIN/TAN signature context verification passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static FinTsPinTanReadSignatureContext Evaluate(JsonElement vector)
    {
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
        var initialization = FinTsPinTanInitializationRequirementsEvidence.Evaluate(binding, tan, permissions, hipins);
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)), vector.GetProperty("expectedBankMessageNumber").GetInt32());
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(vector.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var selection = new FinTsPinTanSignatureSelection(vector.GetProperty("profileVersion").GetInt32(), vector.GetProperty("securityFunction").GetInt32(), vector.GetProperty("tanSegmentVersion").GetInt32());
        return FinTsPinTanReadSignatureContext.Evaluate(initialization, request, header, selection, vector.GetProperty("controlReference").GetString()!);
    }
}
