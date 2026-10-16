using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadLaterContinuationContextTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-later-continuation-context-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (previous, request) = Inputs(vector);
            byte[] priorBytes = previous.Response.Frame.Syntax.CopyWireBytes(); byte[] nextBytes = request.Frame.Syntax.CopyWireBytes();
            var context = FinTsPinTanReadContinuationContext.Evaluate(previous, request);
            Verify(context.Issues == Flags<FinTsPinTanReadContinuationIssue>(vector, "issues"), "Independent later-continuation issues: " + vector.GetProperty("name").GetString() + " (" + context.Issues + ").");
            Verify(context.RequestIssues == Flags<FinTsReadContextIssue>(vector, "requestIssues"), "Independent next-request qualification issues: " + vector.GetProperty("name").GetString() + " (" + context.RequestIssues + ").");
            Verify(context.HasMatchingEvidence == (context.Issues == FinTsPinTanReadContinuationIssue.None) && context.PageNumber == vector.GetProperty("pageNumber").GetInt32(),
                "The context qualifies the third-page candidate with matching evidence.");
            Verify(context.Previous is null && ReferenceEquals(context.PreviousContinuation, previous) && ReferenceEquals(context.Request, request) &&
                ReferenceEquals(context.Origin, previous.Request.Context.Continuation.Origin),
                "Continuation preserves the exact prior continuation evidence, next request and root capability context.");
            Verify(context.HasMatchingEvidence ? ReferenceEquals(context.MatchingOrigin, context.Origin) && ReferenceEquals(context.MatchingRequest, request) : context.MatchingOrigin is null && context.MatchingRequest is null,
                "All review issues withhold both qualified next request and origin context.");
            Verify(ReferenceEquals(context.Origin.Account, previous.Request.Context.Continuation.Origin.Account) &&
                ReferenceEquals(context.Origin.Capability, previous.Request.Context.Continuation.Origin.Capability) &&
                ReferenceEquals(context.Origin.NationalAdvertisement, previous.Request.Context.Continuation.Origin.NationalAdvertisement),
                "Account, capability and national-permission provenance are inherited without reselection or reconstruction.");
            Verify(priorBytes.SequenceEqual(previous.Response.Frame.Syntax.CopyWireBytes()) && nextBytes.SequenceEqual(request.Frame.Syntax.CopyWireBytes()),
                "Neither source response nor proposed request bytes are modified.");
            Verify(FinTsPinTanReadContinuationContext.Evaluate(previous, request).Issues == context.Issues, "Pure context comparison consumes no token or response.");
        }
        Verify(vectors.Length == 41, "All forty-one independent later-continuation fixtures ran.");
        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanReadContinuationContext Context(string name)
        { var (prior, next) = Inputs(Named(name)); return FinTsPinTanReadContinuationContext.Evaluate(prior, next); }
        var basis = Context("balance-seven-third-page");
        Verify(basis.PageNumber == 3, "PageNumber advances to 3 on third page.");
        Verify(basis.Request.Frame.MessageNumber == 4 && basis.Request.ExpectedBankMessageNumber == 4 &&
            basis.PreviousContinuation!.Request.MessageNumber == 3 && basis.PreviousContinuation.Outcome == FinTsReadOutcomeObservation.PartialReported,
            "Client and bank counters advance independently from the qualified second-page continuation partial result.");
        foreach (string name in new[] { "escaped-token", "token-whitespace-preserved", "maximum-token-length" })
        {
            var context = Context(name);
            Verify(context.HasMatchingEvidence && context.Request.Request.ContinuationToken == context.PreviousContinuation!.ReportedContinuationToken,
                "Opaque token bytes survive delimiters, whitespace and maximum length without normalization on later pages.");
        }
        var padded = Context("equivalent-empty-account-components");
        Verify(padded.HasMatchingEvidence && padded.Request.Request.Accounts[0].Source.Elements.Count == 6 &&
            padded.Origin.Signature.Request.Request.Accounts[0].Source.Elements.Count == 2,
            "Logical empty account components compare equally while exact source shapes remain distinct on later pages.");
        var options = Context("entry-count-preserved");
        Verify(options.HasMatchingEvidence && options.Request.Request.MaximumEntries is not null &&
            options.Request.Request.MaximumEntries == options.Origin.Signature.Request.Request.MaximumEntries,
            "Explicit entry-count options remain unchanged across later pages.");
        Verify(Context("review-token-cannot-qualify").PreviousContinuation!.ReportedContinuationToken is not null && Context("review-token-cannot-qualify").MatchingRequest is null,
            "A raw token retained on a foreign continuation response cannot qualify later continuation.");

        // Multi-page execution chain: Page 1 (attempt) -> Page 2 (continuation attempt) -> Page 3 (later continuation context)
        var basisSig = basis.PreviousContinuation!.Request.Context;
        using (var attempt1 = new FinTsPinTanReadAttempt(TimeSpan.FromSeconds(10)))
        using (var attempt2 = new FinTsPinTanReadContinuationAttempt(TimeSpan.FromSeconds(10)))
        {
            var page1Evidence = basisSig.Continuation.Previous!;
            attempt1.Start(page1Evidence.Request);
            var result1 = attempt1.AcceptResponse(page1Evidence.ReadData);
            Verify(result1.Transition == FinTsPinTanReadAttemptTransition.PartialReported && result1.Evidence is not null, "Page 1 attempt yields partial handoff.");

            var page2Context = FinTsPinTanReadContinuationContext.Evaluate(result1.Evidence!, basisSig.Request);
            Verify(page2Context.HasMatchingEvidence && page2Context.PageNumber == 2, "Page 2 context is qualified.");
            var page2Sig = FinTsPinTanReadContinuationSignatureContext.Evaluate(page2Context, basisSig.Header, basisSig.Selection, basisSig.ControlReference);
            var page2Binding = FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(page2Sig);
            attempt2.Start(page2Binding);
            var result2 = attempt2.AcceptResponse(basis.PreviousContinuation.ReadData);
            Verify(result2.Transition == FinTsPinTanReadContinuationAttemptTransition.PartialReported && result2.Evidence is not null, "Page 2 continuation attempt yields partial handoff.");

            var page3Context = FinTsPinTanReadContinuationContext.Evaluate(result2.Evidence!, basis.Request);
            Verify(page3Context.HasMatchingEvidence && page3Context.PageNumber == 3 &&
                ReferenceEquals(page3Context.PreviousContinuation, result2.Evidence) &&
                ReferenceEquals(page3Context.Origin, page1Evidence.Request.Context),
                "Page 3 continuation context chains correctly from page 2 continuation evidence all the way back to root origin.");
        }

        // Bounded page progression loop: pages 3 through 128 (within limit) and page 129 (exceeded)
        var currentEvidence = basis.PreviousContinuation;
        Verify(currentEvidence.PageNumber == 2, "Starting predecessor is page 2.");
        for (int page = 3; page <= FinTsReadContextEvidence.MaximumPages + 1; page++)
        {
            int clientMsg = page + 1; int bankMsg = page + 1;
            string token = $"TOKEN-{page}";
            var nextReq = MakeContinuationRequest(clientMsg, bankMsg, currentEvidence.ReportedContinuationToken!);
            var nextContext = FinTsPinTanReadContinuationContext.Evaluate(currentEvidence, nextReq);
            if (page <= FinTsReadContextEvidence.MaximumPages)
            {
                Verify(nextContext.HasMatchingEvidence && nextContext.PageNumber == page, $"Page {page} is within maximum page limit.");
                Verify(ReferenceEquals(nextContext.PreviousContinuation, currentEvidence) && nextContext.Previous is null, $"Page {page} links to predecessor.");
                Verify(ReferenceEquals(nextContext.Origin, basis.Origin), $"Page {page} preserves root origin.");

                var sig = FinTsPinTanReadContinuationSignatureContext.Evaluate(nextContext, basisSig.Header, basisSig.Selection, basisSig.ControlReference);
                var reqBinding = FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(sig);
                var response = MakeContinuationResponse(bankMsg, token);
                var respBinding = FinTsPinTanReadContinuationResponseBinding.Evaluate(reqBinding, response);
                var data = FinTsReadDataSet.Parse(response);
                currentEvidence = FinTsPinTanReadContinuationEvidence.Evaluate(respBinding, data);
                Verify(currentEvidence.HasMatchingEvidence && currentEvidence.PageNumber == page, $"Continuation evidence for page {page} is matching.");
            }
            else
            {
                Verify(nextContext.Issues == FinTsPinTanReadContinuationIssue.PageLimitExceeded && nextContext.PageNumber == 129,
                    "Page 129 triggers PageLimitExceeded issue and caps page number.");
                Verify(!nextContext.HasMatchingEvidence && nextContext.MatchingOrigin is null && nextContext.MatchingRequest is null,
                    "PageLimitExceeded issue withholds matching origin and matching request.");
            }
        }

        var independent = Inputs(vectors[0]);
        var mixed = FinTsPinTanReadContinuationEvidence.Evaluate(basis.PreviousContinuation.Binding, independent.Previous.ReadData);
        var rejected = FinTsPinTanReadContinuationContext.Evaluate(mixed, basis.Request);
        Verify(rejected.Issues == (FinTsPinTanReadContinuationIssue.PreviousNeedsReview | FinTsPinTanReadContinuationIssue.PartialRequired | FinTsPinTanReadContinuationIssue.TokenMismatch) &&
            rejected.MatchingOrigin is null, "A mixed continuation response tree cannot contribute qualified predecessor data or a token.");
        var independentContext = FinTsPinTanReadContinuationContext.Evaluate(independent.Previous, independent.Request);
        Verify(independentContext.HasMatchingEvidence && !ReferenceEquals(independentContext.Origin, basis.Origin),
            "An independently qualified continuation tree remains independent evidence; comparison offers no cross-tree replay guard.");

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadContinuationContext.Evaluate(basis.PreviousContinuation, basis.Request, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation publishes no later-continuation context."); }
        }
        foreach (bool missingPrevious in new[] { false, true })
        {
            try { FinTsPinTanReadContinuationContext.Evaluate(missingPrevious ? null! : basis.PreviousContinuation, missingPrevious ? basis.Request : null!); Verify(false, "Null required input must throw."); }
            catch (ArgumentNullException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Required-input diagnostics contain no account or token values."); }
        }
        var results = new FinTsPinTanReadContinuationContext[16];
        Parallel.For(0, results.Length, i => results[i] = FinTsPinTanReadContinuationContext.Evaluate(basis.PreviousContinuation, basis.Request));
        Verify(results.All(r => r.HasMatchingEvidence && ReferenceEquals(r.PreviousContinuation, basis.PreviousContinuation) && ReferenceEquals(r.Request, basis.Request)),
            "Concurrent comparisons preserve immutable provenance without consuming prior evidence.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanReadContinuationContext.Evaluate(basis.PreviousContinuation, basis.Request).HasMatchingEvidence, "Later-continuation comparison is culture independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Console.WriteLine($"FinTS later-page balance-continuation context passed ({vectors.Length} independent vectors, {count} checks).");
    }

    internal static FinTsReadRequestContext MakeContinuationRequest(int messageNumber, int expectedBankNumber, string token)
    {
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{messageNumber}'HKSAL:2:7+PUBLIC-IBAN:PUBLIC-BIC+N++{token}'HNHBS:3:1+{messageNumber}'";
        wire = wire.Replace("000000000000", wire.Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)), expectedBankNumber);
    }

    internal static FinTsResponse MakeContinuationResponse(int messageNumber, string token)
    {
        string inner = $"HIRMG:2:2+0010::PUBLIC-REPLY'HIRMS:3:2:3+3040::PUBLIC-REPLY:{token}'HISAL:4:7:3+PUBLIC-IBAN:PUBLIC-BIC+PUBLIC-ACCOUNT+EUR+C:1,:EUR:20260910'";
        string envelope = $"HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:PUBLIC-BANK:PUBLIC-USER:V:0:0+0'HNVSD:999:1+@{inner.Length}@{inner}'";
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{messageNumber}+SYNTHETIC:{messageNumber}'{envelope}HNHBS:5:1+{messageNumber}'";
        wire = wire.Replace("000000000000", wire.Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire))));
    }

    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum => (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0,
        (flags, value) => flags | Convert.ToInt32(Enum.Parse<T>(value.GetString()!), CultureInfo.InvariantCulture)));

    internal static (FinTsPinTanReadContinuationEvidence Previous, FinTsReadRequestContext Request) Inputs(JsonElement vector)
    {
        var (binding, data) = FinTsPinTanReadContinuationEvidenceTests.Inputs(vector.GetProperty("previous"));
        var previous = FinTsPinTanReadContinuationEvidence.Evaluate(binding, data);
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)),
            vector.GetProperty("expectedBankMessageNumber").GetInt32());
        return (previous, request);
    }
}
