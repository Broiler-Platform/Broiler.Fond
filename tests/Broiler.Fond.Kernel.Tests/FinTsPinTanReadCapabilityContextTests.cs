using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadCapabilityContextTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-capability-context-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (signature, account, national) = Inputs(vector);
            var result = FinTsPinTanReadCapabilityContext.Evaluate(signature, account, national);
            string name = vector.GetProperty("name").GetString()!;
            var expected = Flags<FinTsPinTanReadCapabilityIssue>(vector, "issues");
            var expectedCapability = Flags<FinTsReadEvidenceIssue>(vector, "capabilityIssues");
            var expectedRequest = Flags<FinTsReadContextIssue>(vector, "requestIssues");
            if (expectedCapability != 0) { expectedRequest |= FinTsReadContextIssue.CapabilityNeedsReview; }
            Verify(signature.Initialization.HasMatchingEvidence == vector.GetProperty("initializationMatches").GetBoolean(), "Independent initialization integration: " + name);
            Verify(result.Issues == expected && result.HasMatchingEvidence == (expected == 0), $"Independent read capability flags: {name} ({result.Issues}).");
            Verify(result.Capability!.Issues == expectedCapability && result.RequestIssues == expectedRequest, $"Component flags retain exact failures: {name} ({result.Capability.Issues}; {result.RequestIssues}).");
            Verify(ReferenceEquals(result.Signature, signature) && ReferenceEquals(result.Account, account) && ReferenceEquals(result.NationalAdvertisement, national) &&
                ReferenceEquals(result.Capability.Source, signature.Initialization.ReadParameters), "Every selected object retains exact returned provenance.");
            Verify(signature.Initialization.Procedures.Initialization.Response.Frame.Syntax.CopyWireBytes().SequenceEqual(Convert.FromBase64String(vector.GetProperty("initialization").GetProperty("responseBase64").GetString()!)), "Read integration preserves original response bytes.");
            Verify(result.HasMatchingEvidence ? ReferenceEquals(result.MatchingAccount, account) && ReferenceEquals(result.MatchingAdvertisement, result.Capability.Candidates.Single()) &&
                result.OperationEvidence == signature.OperationEvidence : result.MatchingAccount is null && result.MatchingAdvertisement is null && result.OperationEvidence == FinTsPinTanOperationEvidence.Unknown,
                "Only the whole read context exposes qualified account, advertisement and TAN flag.");
            var initialization = signature.Initialization;
            var procedures = initialization.Procedures;
            Verify(!FinTsPinTanInitializationRequirementsEvidence.Evaluate(procedures.Initialization.Binding, procedures.Procedures, procedures.Permissions, initialization.PinTan).HasMatchingEvidence,
                "The original requirements API still leaves read advertisements unresolved.");
            Verify(FinTsReadCapabilityEvidence.Evaluate(initialization.ReadParameters!, account, result.Capability.Operation, result.Capability.Version).Issues.HasFlag(FinTsReadEvidenceIssue.ResponseNeedsReview),
                "The original generic capability comparator retains its conservative status vocabulary.");
        }
        Verify(vectors.Length == 43, "All forty-three independent integrated capability vectors ran.");
        var (basis, selected, nationalBasis) = Inputs(vectors[0]);
        var init = basis.Initialization; var p = init.Procedures; var reads = init.ReadParameters!;
        var legacy = FinTsPinTanInitializationRequirementsEvidence.Evaluate(p.Initialization.Binding, p.Procedures, p.Permissions, init.PinTan);
        var legacySignature = FinTsPinTanReadSignatureContext.Evaluate(legacy, basis.Request, basis.Header, basis.Selection, basis.ControlReference);
        var missingReads = FinTsPinTanReadCapabilityContext.Evaluate(legacySignature, selected);
        Verify(missingReads.Issues == (FinTsPinTanReadCapabilityIssue.SignatureNeedsReview | FinTsPinTanReadCapabilityIssue.MissingReadParameters) && missingReads.Capability is null,
            "A legacy requirements result cannot implicitly acquire read parameters from the account.");
        var badSignature = FinTsPinTanReadSignatureContext.Evaluate(init, basis.Request, basis.Header, basis.Selection, "OTHER-REF");
        var badSignatureContext = FinTsPinTanReadCapabilityContext.Evaluate(badSignature, selected);
        Verify(badSignatureContext.Capability!.HasMatchingEvidence && badSignatureContext.Issues == FinTsPinTanReadCapabilityIssue.SignatureNeedsReview && badSignatureContext.MatchingAccount is null,
            "Matching capability evidence cannot override an unresolved signature context.");
        var (_, copiedAccount, copiedNational) = Inputs(vectors[0]);
        var foreignAccount = FinTsPinTanReadCapabilityContext.Evaluate(basis, copiedAccount, nationalBasis);
        Verify(foreignAccount.Issues == FinTsPinTanReadCapabilityIssue.AccountSourceMismatch && foreignAccount.Capability is null && foreignAccount.MatchingAccount is null,
            "An account from identical reparsed bytes cannot substitute for the exact selected source.");
        var (nationalSignature, nationalAccount, _) = Inputs(vectors[6]);
        Verify(FinTsPinTanReadCapabilityContext.Evaluate(nationalSignature, nationalAccount, copiedNational).RequestIssues.HasFlag(FinTsReadContextIssue.NationalAccountNotAdvertised),
            "The explicit national-connection advertisement must belong to the exact returned read set.");
        foreach (bool sameResponse in new[] { false, true })
        {
            var source = sameResponse ? FinTsParameterSet.Parse(p.Initialization.Response) : Inputs(vectors[0]).Signature.Initialization.PinTan.Source;
            var copiedReads = FinTsReadParameterSet.Parse(source);
            var mixed = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(p.Initialization.Binding, p.Procedures, p.Permissions, init.PinTan, copiedReads);
            Verify(mixed.Issues.HasFlag(FinTsPinTanInitializationRequirementsIssue.SourceMismatch) && mixed.MatchingAdvertisement is null && ReferenceEquals(mixed.ReadParameters, copiedReads),
                "Separate parameter trees cannot supply read schemas, even on the same response object.");
            using var attempt = New(); attempt.Start(p.Initialization.Request);
            var handoff = attempt.AcceptReadParametersResponse(p.Procedures, p.Permissions, init.PinTan, copiedReads);
            Verify(handoff.Transition == FinTsInitializationTransition.RejectedForReview && handoff.RequirementsEvidence!.Issues.HasFlag(FinTsPinTanInitializationRequirementsIssue.SourceMismatch) && Pending(attempt) is null,
                "Mixed read schemas terminate the initialization attempt for review and release pending metadata.");
        }
        using (var attempt = New())
        {
            attempt.Start(p.Initialization.Request);
            var foreign = Inputs(vectors[32]).Signature.Initialization;
            Verify(attempt.AcceptReadParametersResponse(foreign.Procedures.Procedures, foreign.Procedures.Permissions, foreign.PinTan, foreign.ReadParameters!).Transition == FinTsInitializationTransition.ContextMismatch && Pending(attempt) is not null,
                "Old read-advertisement references leave the original candidate pending.");
            var handoff = attempt.AcceptReadParametersResponse(p.Procedures, p.Permissions, init.PinTan, reads);
            Verify(handoff.Transition == FinTsInitializationTransition.ExecutionReported && ReferenceEquals(handoff.RequirementsEvidence!.ReadParameters, reads) && Pending(attempt) is null && attempt.GetSnapshot().RequirementsIssues == 0,
                "Later valid scope returns read schemas once and clears transient diagnostics.");
            var signature = FinTsPinTanReadSignatureContext.Evaluate(handoff.RequirementsEvidence!, basis.Request, basis.Header, basis.Selection, basis.ControlReference);
            Verify(FinTsPinTanReadCapabilityContext.Evaluate(signature, selected, nationalBasis).HasMatchingEvidence &&
                attempt.AcceptRequirementsResponse(p.Procedures, p.Permissions, init.PinTan).RequirementsEvidence is null &&
                attempt.AcceptProcedureResponse(p.Procedures, p.Permissions).ProcedureEvidence is null && attempt.AcceptResponse(reads.Source).Evidence is null && attempt.GetSnapshot().ResponsesHandled == 1,
                "The exact lifecycle handoff supports account matching and all older entry points reject replay.");
        }
        foreach (int stage in new[] { 1, 2, 3 })
            foreach (string failure in new[] { "cancel", "expiry", "clock" })
            {
                var clock = new Clock(); using var attempt = New(clock); using var cancellation = new CancellationTokenSource();
                attempt.Start(p.Initialization.Request); clock.AfterReads = stage; clock.Action = () =>
                {
                    if (failure == "cancel") { cancellation.Cancel(); }
                    else if (failure == "expiry") { clock.Milliseconds = 10000; }
                    else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                };
                try
                {
                    var handoff = attempt.AcceptReadParametersResponse(p.Procedures, p.Permissions, init.PinTan, reads, cancellation.Token);
                    Verify(failure != "cancel" && handoff.Evidence is null && handoff.ProcedureEvidence is null && handoff.RequirementsEvidence is null, "Late failure withholds every component and its read schemas.");
                }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Late cancellation yields no integrated handoff."); }
                Verify(Pending(attempt) is null && attempt.GetSnapshot().ResponsesHandled == 0, "Late failure releases pending metadata without consuming a response.");
            }
        var concurrent = new FinTsPinTanReadCapabilityContext[16];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = FinTsPinTanReadCapabilityContext.Evaluate(basis, selected, nationalBasis));
        Verify(concurrent.All(r => r.HasMatchingEvidence && ReferenceEquals(r.MatchingAccount, selected)), "Concurrent pure account comparisons retain source identity.");
        using (var attempt = New())
        {
            attempt.Start(p.Initialization.Request); var results = new FinTsPinTanInitializationAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptReadParametersResponse(p.Procedures, p.Permissions, init.PinTan, reads));
            Verify(results.Count(r => r.RequirementsEvidence is not null) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null,
                "Concurrent read-parameter submissions hand out one result and release the candidate.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanReadCapabilityContext.Evaluate(basis, selected, nationalBasis, cancellation.Token); Verify(false, "Cancelled account comparison must throw."); }
            catch (OperationCanceledException) { Verify(true, "Account comparison observes cancellation."); }
            try { FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(p.Initialization.Binding, p.Procedures, p.Permissions, init.PinTan, reads, cancellation.Token); Verify(false, "Cancelled integration must throw."); }
            catch (OperationCanceledException) { Verify(true, "Read-schema integration observes cancellation."); }
        }
        foreach (int missing in new[] { 0, 1, 2, 3, 4 })
        {
            try
            {
                FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(missing == 0 ? null! : p.Initialization.Binding, missing == 1 ? null! : p.Procedures,
                missing == 2 ? null! : p.Permissions, missing == 3 ? null! : init.PinTan, missing == 4 ? null! : reads); Verify(false, "Null integration inputs must fail.");
            }
            catch (ArgumentNullException) { Verify(true, "Integration requires every explicit component."); }
        }
        foreach (bool missingSignature in new[] { false, true })
        {
            try { FinTsPinTanReadCapabilityContext.Evaluate(missingSignature ? null! : basis, missingSignature ? selected : null!); Verify(false, "Null account context inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Account context requires signature and account."); }
        }
        foreach (int missing in new[] { 0, 1, 2, 3 })
        {
            using var attempt = New();
            try
            {
                attempt.AcceptReadParametersResponse(missing == 0 ? null! : p.Procedures, missing == 1 ? null! : p.Permissions,
                missing == 2 ? null! : init.PinTan, missing == 3 ? null! : reads); Verify(false, "Null lifecycle input must fail.");
            }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsInitializationState.Ready, "Null read-schema lifecycle input leaves the attempt ready."); }
        }
        using (var attempt = New())
        {
            attempt.Start(p.Initialization.Request); attempt.AcceptRequirementsResponse(p.Procedures, p.Permissions, init.PinTan);
            Verify(attempt.AcceptReadParametersResponse(p.Procedures, p.Permissions, init.PinTan, reads).RequirementsEvidence is null && attempt.GetSnapshot().ResponsesHandled == 1,
                "A prior handoff through the older requirements API cannot be upgraded or replayed through the read-schema API.");
        }
        Console.WriteLine($"FinTS integrated first-read capability context verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static T Flags<T>(JsonElement vector, string property) where T : struct, Enum => (T)Enum.ToObject(typeof(T), vector.GetProperty(property).EnumerateArray().Aggregate(0,
        (flags, item) => flags | Convert.ToInt32(Enum.Parse<T>(item.GetString()!))));
    internal static (FinTsPinTanReadSignatureContext Signature, FinTsAccountParameters Account, FinTsReadAdvertisement? National) Inputs(JsonElement vector, string controlReference = "NEXT-REF")
    {
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
        var reads = FinTsReadParameterSet.Parse(hipins.Source);
        var initialization = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(binding, tan, permissions, hipins, reads);
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)), 2);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(vector.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var selection = binding.Request.SignatureEvidence.Request.Selection;
        var signature = FinTsPinTanReadSignatureContext.Evaluate(initialization, request, header, selection, controlReference);
        return (signature, reads.Source.Accounts[0], vector.GetProperty("nationalSelection").GetBoolean() ? reads.Advertisements.Single(a => a.Operation == FinTsReadOperation.SepaAccountDetails) : null);
    }
    private static FinTsPinTanInitializationAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static object? Pending(FinTsPinTanInitializationAttempt attempt) => typeof(FinTsPinTanInitializationAttempt).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attempt);
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
