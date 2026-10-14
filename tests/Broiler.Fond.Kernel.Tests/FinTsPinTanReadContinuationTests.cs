using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadContinuationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-continuation-context-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (previous, request) = Inputs(vector);
            byte[] priorBytes = previous.Response.Frame.Syntax.CopyWireBytes(); byte[] nextBytes = request.Frame.Syntax.CopyWireBytes();
            var context = FinTsPinTanReadContinuationContext.Evaluate(previous, request);
            Verify(context.Issues == Flags<FinTsPinTanReadContinuationIssue>(vector, "issues"), "Independent continuation issues: " + vector.GetProperty("name").GetString() + " (" + context.Issues + ").");
            Verify(context.RequestIssues == Flags<FinTsReadContextIssue>(vector, "requestIssues"), "Independent next-request qualification issues: " + vector.GetProperty("name").GetString() + " (" + context.RequestIssues + ").");
            Verify(context.HasMatchingEvidence == (context.Issues == FinTsPinTanReadContinuationIssue.None) && context.PageNumber == 2, "The context qualifies only the immediate second-page candidate.");
            Verify(ReferenceEquals(context.Previous, previous) && ReferenceEquals(context.Request, request) && ReferenceEquals(context.Origin, previous.Request.Context), "Continuation preserves the exact prior evidence, next request and original capability context.");
            Verify(context.HasMatchingEvidence ? ReferenceEquals(context.MatchingOrigin, context.Origin) && ReferenceEquals(context.MatchingRequest, request) : context.MatchingOrigin is null && context.MatchingRequest is null,
                "All review issues withhold both qualified next request and origin context.");
            Verify(ReferenceEquals(context.Origin.Account, previous.Request.Context.Account) && ReferenceEquals(context.Origin.Capability, previous.Request.Context.Capability) && ReferenceEquals(context.Origin.NationalAdvertisement, previous.Request.Context.NationalAdvertisement),
                "Account, capability and national-permission provenance are inherited without reselection or reconstruction.");
            Verify(priorBytes.SequenceEqual(previous.Response.Frame.Syntax.CopyWireBytes()) && nextBytes.SequenceEqual(request.Frame.Syntax.CopyWireBytes()), "Neither source response nor proposed request bytes are modified.");
            Verify(FinTsPinTanReadContinuationContext.Evaluate(previous, request).Issues == context.Issues, "Pure context comparison consumes no token or response.");
        }
        Verify(vectors.Length == 41, "All forty-one independent continuation fixtures ran.");
        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanReadContinuationContext Context(string name)
        { var (prior, next) = Inputs(Named(name)); return FinTsPinTanReadContinuationContext.Evaluate(prior, next); }
        var basis = Context("balance-seven-second-page");
        Verify(basis.Request.Frame.MessageNumber == 3 && basis.Request.ExpectedBankMessageNumber == 3 && basis.Previous!.Request.MessageNumber == 2 && basis.Previous.Outcome == FinTsReadOutcomeObservation.PartialReported,
            "Client and bank counters advance independently from the qualified first-read partial result.");
        foreach (string name in new[] { "escaped-token", "token-whitespace-preserved", "maximum-token-length" })
        {
            var context = Context(name);
            Verify(context.HasMatchingEvidence && context.Request.Request.ContinuationToken == context.Previous!.ReportedContinuationToken, "Opaque token bytes survive delimiters, whitespace and maximum length without normalization.");
        }
        var padded = Context("equivalent-empty-account-components");
        Verify(padded.HasMatchingEvidence && padded.Request.Request.Accounts[0].Source.Elements.Count == 6 && padded.Origin.Signature.Request.Request.Accounts[0].Source.Elements.Count == 2,
            "Logical empty account components compare equally while exact source shapes remain distinct.");
        var options = Context("entry-count-preserved");
        Verify(options.HasMatchingEvidence && options.Request.Request.MaximumEntries is not null && options.Request.Request.MaximumEntries == options.Origin.Signature.Request.Request.MaximumEntries,
            "Explicit entry-count options remain unchanged across pages.");
        Verify(Context("review-token-cannot-qualify").Previous!.ReportedContinuationToken is not null && Context("review-token-cannot-qualify").MatchingRequest is null,
            "A raw token retained on a foreign response cannot qualify continuation.");

        using (var attempt = new FinTsPinTanReadAttempt(TimeSpan.FromSeconds(10)))
        {
            attempt.Start(basis.Previous!.Request); var result = attempt.AcceptResponse(basis.Previous.ReadData);
            var continuation = FinTsPinTanReadContinuationContext.Evaluate(result.Evidence!, basis.Request);
            Verify(result.Transition == FinTsPinTanReadAttemptTransition.PartialReported && continuation.HasMatchingEvidence && ReferenceEquals(continuation.Previous, result.Evidence),
                "The actual one-time partial handoff can supply exact continuation provenance.");
            attempt.Dispose();
            Verify(continuation.HasMatchingEvidence && attempt.AcceptResponse(basis.Previous.ReadData).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanReadAttemptState.PartialReported,
                "Context construction leaves the terminal first-read owner unchanged and does not repeat handoff.");
        }
        var firstSignature = basis.Origin.Signature;
        var wrongPath = FinTsPinTanReadSignatureContext.Evaluate(firstSignature.Initialization, basis.Request, firstSignature.Header, firstSignature.Selection, firstSignature.ControlReference);
        Verify(wrongPath.Issues == (FinTsPinTanReadSignatureIssue.CounterMismatch | FinTsPinTanReadSignatureIssue.ContinuationNeedsReview),
            "The first-read signing path remains restricted; continuation context alone cannot encode another signed request.");
        var independent = Inputs(vectors[0]);
        var mixed = FinTsPinTanReadEvidence.Evaluate(basis.Previous.Binding, independent.Previous.ReadData);
        var rejected = FinTsPinTanReadContinuationContext.Evaluate(mixed, basis.Request);
        Verify(rejected.Issues == (FinTsPinTanReadContinuationIssue.PreviousNeedsReview | FinTsPinTanReadContinuationIssue.PartialRequired | FinTsPinTanReadContinuationIssue.TokenMismatch) && rejected.MatchingOrigin is null,
            "A mixed response tree cannot contribute qualified predecessor data or a token.");
        var independentContext = FinTsPinTanReadContinuationContext.Evaluate(independent.Previous, independent.Request);
        Verify(independentContext.HasMatchingEvidence && !ReferenceEquals(independentContext.Origin, basis.Origin), "An independently qualified tree remains independent evidence; comparison offers no cross-tree replay guard.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadContinuationContext.Evaluate(basis.Previous!, basis.Request, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation publishes no continuation context."); }
        }
        foreach (bool missingPrevious in new[] { false, true })
        {
            try { FinTsPinTanReadContinuationContext.Evaluate(missingPrevious ? null! : basis.Previous!, missingPrevious ? basis.Request : null!); Verify(false, "Null required input must throw."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Required-input diagnostics contain no account or token values."); }
        }
        var results = new FinTsPinTanReadContinuationContext[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanReadContinuationContext.Evaluate(basis.Previous!, basis.Request));
        Verify(results.All(r => r.HasMatchingEvidence && ReferenceEquals(r.Previous, basis.Previous) && ReferenceEquals(r.Request, basis.Request)), "Concurrent comparisons preserve immutable provenance without consuming prior evidence.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadContinuationContext.Evaluate(basis.Previous, basis.Request).HasMatchingEvidence, "Continuation comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine($"FinTS assembled balance-continuation context passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum => (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0,
        (flags, value) => flags | Convert.ToInt32(Enum.Parse<T>(value.GetString()!), CultureInfo.InvariantCulture)));
    internal static (FinTsPinTanReadEvidence Previous, FinTsReadRequestContext Request) Inputs(JsonElement vector)
    {
        var (binding, data) = FinTsPinTanReadEvidenceTests.Inputs(vector.GetProperty("previous"));
        var previous = FinTsPinTanReadEvidence.Evaluate(binding, data);
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)),
            vector.GetProperty("expectedBankMessageNumber").GetInt32());
        return (previous, request);
    }
}
