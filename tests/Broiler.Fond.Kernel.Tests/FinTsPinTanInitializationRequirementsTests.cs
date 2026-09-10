using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanInitializationRequirementsTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-initialization-requirements-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (binding, procedures, permissions, pinTan) = Inputs(vector);
            byte[] original = binding.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanInitializationRequirementsEvidence.Evaluate(binding, procedures, permissions, pinTan);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanInitializationRequirementsIssue.None,
                (flags, issue) => flags | Enum.Parse<FinTsPinTanInitializationRequirementsIssue>(issue.GetString()!));
            Verify(result.Issues == expected && result.HasMatchingEvidence == (expected == 0), "Independent HIPINS integration flags match: " + vector.GetProperty("name").GetString());
            Verify((result.MatchingAdvertisement is not null) == (expected == 0) && result.GetOperationEvidence("HKSAL").ToString() == vector.GetProperty("operationEvidence").GetString(), "Qualified operation evidence is withheld on every unresolved combined result.");
            Verify(ReferenceEquals(result.PinTan, pinTan) && ReferenceEquals(result.Procedures.Procedures, procedures) && ReferenceEquals(result.Procedures.Permissions, permissions) &&
                ReferenceEquals(result.Procedures.Initialization.Binding, binding), "All component sources remain exact caller-owned objects.");
            Verify(original.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()), "HIPINS integration preserves all original response bytes.");
            if (pinTan.Advertisements.Count == 1)
            {
                var raw = pinTan.Advertisements[0];
                Verify(raw.MinimumPinLength == Optional(vector, "minimumPinLength") && raw.MaximumPinLength == Optional(vector, "maximumPinLength") && raw.MaximumTanLength == Optional(vector, "maximumTanLength"), "Raw bounds retain null, explicit zero and contradictory values without repair.");
            }
            if (result.HasMatchingEvidence)
            {
                Verify(ReferenceEquals(result.MatchingAdvertisement, pinTan.Advertisements.Single()) && result.GetOperationEvidence("HKZZZ") == FinTsPinTanOperationEvidence.Unlisted,
                    "Matching advertisement retains exact source and absent operations remain unlisted.");
            }
            bool hasHipins = binding.Response.UninterpretedSegments.Any(s => s.Code == "HIPINS");
            Verify(FinTsPinTanInitializationProcedureEvidence.Evaluate(binding, procedures, permissions).HasMatchingEvidence == !hasHipins,
                "The older procedure-only entry point still leaves HIPINS unresolved, while missing HIPINS requires explicit full-path review.");
            using var attempt = New(); attempt.Start(binding.Request);
            var handoff = attempt.AcceptRequirementsResponse(procedures, permissions, pinTan);
            bool foreign = (binding.Issues & (FinTsPinTanResponseBindingIssue.MessageMismatch | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch | FinTsPinTanResponseBindingIssue.MissingSegmentReference)) != 0;
            Verify(handoff.Transition == (foreign ? FinTsInitializationTransition.ContextMismatch : expected == 0 ? FinTsInitializationTransition.ExecutionReported : FinTsInitializationTransition.RejectedForReview), "Lifecycle transition uses complete requirements qualification and preserves foreign scope.");
            Verify(foreign ? handoff.RequirementsEvidence is null && attempt.GetSnapshot().ResponsesHandled == 0 : handoff.RequirementsEvidence!.Issues == expected &&
                ReferenceEquals(handoff.RequirementsEvidence.Procedures, handoff.ProcedureEvidence) && ReferenceEquals(handoff.ProcedureEvidence!.Initialization, handoff.Evidence) && Pending(attempt) is null,
                "All scoped component handoffs belong to one combined result and terminal cleanup releases metadata.");
            if (!foreign)
            {
                Verify(attempt.GetSnapshot().RequirementsIssues == expected && attempt.GetSnapshot().ResponsesHandled == 1 &&
                    attempt.AcceptProcedureResponse(procedures, permissions).ProcedureEvidence is null && attempt.AcceptResponse(procedures.Source).Evidence is null,
                    "Scalar diagnostics survive cleanup and neither older entry point can replay the combined handoff.");
            }
        }
        Verify(vectors.Length == 30, "All thirty independent HIPINS integration vectors ran.");
        var (basis, tan, allowed, hipins) = Inputs(vectors[0]); var (_, copiedTan, copiedAllowed, copiedHipins) = Inputs(vectors[0]);
        foreach (int source in new[] { 0, 1, 2, 3 })
        {
            var replacement = source == 3 ? FinTsPinTanParameterSet.Parse(FinTsParameterSet.Parse(basis.Response)) : copiedHipins;
            var mixed = FinTsPinTanInitializationRequirementsEvidence.Evaluate(basis, source == 0 ? copiedTan : tan, source == 1 ? copiedAllowed : allowed, source >= 2 ? replacement : hipins);
            Verify(mixed.Issues.HasFlag(FinTsPinTanInitializationRequirementsIssue.SourceMismatch) && mixed.MatchingAdvertisement is null && mixed.GetOperationEvidence("HKSAL") == FinTsPinTanOperationEvidence.Unknown,
                "Identical-byte copies and separate parameter trees cannot substitute for any pinned component source.");
        }
        using (var attempt = New())
        {
            attempt.Start(basis.Request); var result = attempt.AcceptRequirementsResponse(tan, allowed, copiedHipins);
            Verify(result.Transition == FinTsInitializationTransition.RejectedForReview && result.RequirementsEvidence!.Issues.HasFlag(FinTsPinTanInitializationRequirementsIssue.SourceMismatch) && Pending(attempt) is null,
                "Mixed HIPINS sources terminate for review through the lifecycle path.");
        }
        using (var attempt = New())
        {
            var (_, foreignTan, foreignAllowed, foreignHipins) = Inputs(vectors[19]); attempt.Start(basis.Request);
            Verify(attempt.AcceptRequirementsResponse(foreignTan, foreignAllowed, foreignHipins).Transition == FinTsInitializationTransition.ContextMismatch &&
                attempt.AcceptRequirementsResponse(tan, allowed, hipins).Transition == FinTsInitializationTransition.ExecutionReported && attempt.GetSnapshot().RequirementsIssues == 0,
                "Foreign HIPINS reference does not replace the candidate and later valid evidence clears transient diagnostics.");
        }
        foreach (int stage in new[] { 1, 2, 3 })
            foreach (string failure in new[] { "cancel", "expiry", "clock" })
            {
                var clock = new Clock(); using var attempt = New(clock); using var cancellation = new CancellationTokenSource();
                attempt.Start(basis.Request); clock.AfterReads = stage; clock.Action = () =>
                {
                    if (failure == "cancel") { cancellation.Cancel(); }
                    else if (failure == "expiry") { clock.Milliseconds = 10000; }
                    else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                };
                try
                {
                    var result = attempt.AcceptRequirementsResponse(tan, allowed, hipins, cancellation.Token);
                    Verify(failure != "cancel" && result.Evidence is null && result.ProcedureEvidence is null && result.RequirementsEvidence is null, "Late expiry/clock failure withholds all component handoffs.");
                }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Late cancellation withholds the combined HIPINS result."); }
                Verify(Pending(attempt) is null && attempt.GetSnapshot().ResponsesHandled == 0 && attempt.GetSnapshot().RequirementsIssues == 0, "Failed processing releases pending context and retains no unhanded requirements result.");
            }
        var concurrent = new FinTsPinTanInitializationRequirementsEvidence[16];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = FinTsPinTanInitializationRequirementsEvidence.Evaluate(basis, tan, allowed, hipins));
        Verify(concurrent.All(e => e.HasMatchingEvidence && ReferenceEquals(e.MatchingAdvertisement, hipins.Advertisements[0])), "Concurrent pure comparison preserves the original HIPINS advertisement.");
        using (var attempt = New())
        {
            attempt.Start(basis.Request); var results = new FinTsPinTanInitializationAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptRequirementsResponse(tan, allowed, hipins));
            Verify(results.Count(r => r.RequirementsEvidence is not null) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null, "Concurrent submissions hand out the complete requirements result exactly once.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanInitializationRequirementsEvidence.Evaluate(basis, tan, allowed, hipins, cancellation.Token); Verify(false, "Cancelled comparison must throw."); }
            catch (OperationCanceledException) { Verify(true, "Comparison observes pre-cancellation."); }
        }
        foreach (int missing in new[] { 0, 1, 2, 3 })
        {
            try { FinTsPinTanInitializationRequirementsEvidence.Evaluate(missing == 0 ? null! : basis, missing == 1 ? null! : tan, missing == 2 ? null! : allowed, missing == 3 ? null! : hipins); Verify(false, "Null inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Required comparison inputs are checked."); }
        }
        foreach (int missing in new[] { 0, 1, 2 })
        {
            using var attempt = New();
            try { attempt.AcceptRequirementsResponse(missing == 0 ? null! : tan, missing == 1 ? null! : allowed, missing == 2 ? null! : hipins); Verify(false, "Null lifecycle inputs must fail."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsInitializationState.Ready, "Null lifecycle input does not partially record state."); }
        }
        foreach (var sample in new[] { vectors[0], vectors[10] })
        {
            var (binding, procedures, permissions, pinTan) = Inputs(sample);
            var evidence = FinTsPinTanInitializationRequirementsEvidence.Evaluate(binding, procedures, permissions, pinTan);
            try { evidence.GetOperationEvidence("bad"); Verify(false, "Invalid operation syntax must fail even for review evidence."); }
            catch (FinTsFormatException) { Verify(true, "Operation lookup retains existing syntax validation."); }
        }
        Console.WriteLine($"FinTS assembled initialization HIPINS requirements verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static int? Optional(JsonElement vector, string property) => vector.GetProperty(property).ValueKind == JsonValueKind.Null ? null : vector.GetProperty(property).GetInt32();
    private static FinTsPinTanInitializationAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static object? Pending(FinTsPinTanInitializationAttempt attempt) => typeof(FinTsPinTanInitializationAttempt).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attempt);
    internal static (FinTsPinTanResponseBinding, FinTsTanParameterSet, FinTsPermittedProcedureSet, FinTsPinTanParameterSet) Inputs(JsonElement vector)
    {
        var candidate = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context")));
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
        var parameters = FinTsParameterSet.Parse(response);
        return (FinTsPinTanResponseBinding.Evaluate(candidate, response), FinTsTanParameterSet.Parse(parameters), FinTsPermittedProcedureSet.Parse(response), FinTsPinTanParameterSet.Parse(parameters));
    }
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
