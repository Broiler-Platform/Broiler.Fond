using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanInitializationProcedureTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-initialization-procedures-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var (binding, procedures, permissions) = Inputs(vector);
            byte[] original = binding.Response.Frame.Syntax.CopyWireBytes();
            var result = FinTsPinTanInitializationProcedureEvidence.Evaluate(binding, procedures, permissions);
            var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanInitializationProcedureIssue.None,
                (flags, issue) => flags | Enum.Parse<FinTsPinTanInitializationProcedureIssue>(issue.GetString()!));
            Verify(result.Issues == expected, "Independent procedure integration flags match: " + vector.GetProperty("name").GetString());
            Verify(result.HasMatchingEvidence == (expected == 0) && (result.MatchingAdvertisement is not null) == (expected == 0) &&
                (result.MatchingProcedure is not null) == (expected == 0 && binding.Request.ProfileVersion == 2), "Only fully qualified evidence exposes matching advertisement/selection; one-step has no two-step procedure.");
            Verify(ReferenceEquals(result.Initialization.Binding, binding) && ReferenceEquals(result.Initialization.Parameters, procedures.Source) &&
                ReferenceEquals(result.Procedures, procedures) && ReferenceEquals(result.Permissions, permissions), "All component sources remain the exact caller-owned objects.");
            Verify(original.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()), "Integration preserves all original response bytes.");
            Verify(!FinTsPinTanInitializationEvidence.Evaluate(binding, procedures.Source).HasMatchingEvidence, "The legacy initialization comparator remains conservative for procedure/permission responses.");
            if (result.HasMatchingEvidence)
            {
                Verify(result.MatchingAdvertisement!.Source.Version == binding.Request.SignatureEvidence.Request.Selection.TanSegmentVersion &&
                    procedures.Advertisements.Any(a => ReferenceEquals(a, result.MatchingAdvertisement)), "Matching advertisement uses the exact pinned version and original source object.");
                Verify(result.MatchingProcedure is null || result.MatchingAdvertisement.Procedures.Any(p => ReferenceEquals(p, result.MatchingProcedure)), "The selected returned procedure belongs to that exact advertisement.");
            }
            using var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), new Clock());
            attempt.Start(binding.Request);
            var handoff = attempt.AcceptProcedureResponse(procedures, permissions);
            bool foreign = (binding.Issues & (FinTsPinTanResponseBindingIssue.MessageMismatch | FinTsPinTanResponseBindingIssue.SegmentRoleMismatch)) != 0;
            Verify(handoff.Transition == (foreign ? FinTsInitializationTransition.ContextMismatch : result.HasMatchingEvidence ? FinTsInitializationTransition.ExecutionReported : FinTsInitializationTransition.RejectedForReview), "The explicit lifecycle path uses combined qualification and preserves foreign scope.");
            Verify(foreign ? handoff.ProcedureEvidence is null && attempt.GetSnapshot().ResponsesHandled == 0 :
                handoff.ProcedureEvidence!.Issues == expected && ReferenceEquals(handoff.Evidence, handoff.ProcedureEvidence.Initialization) && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null,
                "Scoped combined evidence is handed out once; pending sources are released on terminal outcomes.");
            if (!foreign) { Verify(attempt.GetSnapshot().ProcedureIssues == expected && attempt.AcceptResponse(procedures.Source).Evidence is null, "Scalar procedure diagnostics survive cleanup and the legacy entry point cannot replay the handoff."); }
        }
        Verify(vectors.Length == 28, "All twenty-eight independent procedure integration vectors ran.");
        var (basis, tan, allowed) = Inputs(vectors[0]); var (_, copiedTan, copiedAllowed) = Inputs(vectors[0]);
        foreach (bool swapTan in new[] { false, true })
        {
            var mixed = FinTsPinTanInitializationProcedureEvidence.Evaluate(basis, swapTan ? copiedTan : tan, swapTan ? allowed : copiedAllowed);
            Verify(mixed.Issues == (FinTsPinTanInitializationProcedureIssue.SourceMismatch | FinTsPinTanInitializationProcedureIssue.InitializationNeedsReview) && mixed.MatchingAdvertisement is null && mixed.MatchingProcedure is null,
                "Even identical reparsed bytes cannot substitute a different procedure or permission source tree.");
        }
        using (var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), new Clock()))
        {
            attempt.Start(basis.Request); var mixed = attempt.AcceptProcedureResponse(tan, copiedAllowed);
            Verify(mixed.Transition == FinTsInitializationTransition.RejectedForReview && mixed.ProcedureEvidence!.Issues.HasFlag(FinTsPinTanInitializationProcedureIssue.SourceMismatch) && Pending(attempt) is null, "Mixed permission sources cannot pass through lifecycle integration.");
        }
        using (var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), new Clock()))
        {
            var (_, foreignTan, foreignPermissions) = Inputs(vectors[10]); attempt.Start(basis.Request);
            Verify(attempt.AcceptProcedureResponse(foreignTan, foreignPermissions).Transition == FinTsInitializationTransition.ContextMismatch &&
                attempt.AcceptProcedureResponse(tan, allowed).Transition == FinTsInitializationTransition.ExecutionReported && attempt.GetSnapshot().ProcedureIssues == 0,
                "A foreign message does not replace the pending candidate or taint a later valid procedure handoff.");
        }
        foreach (int stage in new[] { 1, 2, 3 })
            foreach (string failure in new[] { "cancel", "expiry", "clock" })
            {
                var clock = new Clock(); using var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), clock); using var cancellation = new CancellationTokenSource();
                attempt.Start(basis.Request); clock.AfterReads = stage; clock.Action = () =>
                {
                    if (failure == "cancel") { cancellation.Cancel(); }
                    else if (failure == "expiry") { clock.Milliseconds = 10000; }
                    else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                };
                try { var result = attempt.AcceptProcedureResponse(tan, allowed, cancellation.Token); Verify(failure != "cancel" && result.Evidence is null && result.ProcedureEvidence is null, "Late expiry/clock failure withholds both component and procedure handoffs."); }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Late cancellation withholds combined evidence."); }
                Verify(Pending(attempt) is null && attempt.GetSnapshot().ResponsesHandled == 0, "Every failed processing boundary releases pending context without counting a response.");
            }
        var repeated = new FinTsPinTanInitializationProcedureEvidence[16];
        Parallel.For(0, repeated.Length, i => repeated[i] = FinTsPinTanInitializationProcedureEvidence.Evaluate(basis, tan, allowed));
        Verify(repeated.All(r => r.HasMatchingEvidence && ReferenceEquals(r.MatchingAdvertisement, tan.Advertisements[0])), "Concurrent pure comparison preserves the exact returned advertisement without activating it.");
        using (var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), new Clock()))
        {
            attempt.Start(basis.Request); var results = new FinTsPinTanInitializationAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptProcedureResponse(tan, allowed));
            Verify(results.Count(r => r.ProcedureEvidence is not null) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null, "Concurrent lifecycle submission hands out combined evidence only once.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsPinTanInitializationProcedureEvidence.Evaluate(basis, tan, allowed, cancellation.Token); Verify(false, "Cancelled comparison must fail."); }
            catch (OperationCanceledException) { Verify(true, "Comparison observes pre-cancellation."); }
        }
        foreach (int missing in new[] { 0, 1, 2 })
        {
            try { FinTsPinTanInitializationProcedureEvidence.Evaluate(missing == 0 ? null! : basis, missing == 1 ? null! : tan, missing == 2 ? null! : allowed); Verify(false, "Null required inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null procedure comparison inputs fail with fixed diagnostics."); }
        }
        foreach (bool missingTan in new[] { false, true })
        {
            using var attempt = new FinTsPinTanInitializationAttempt(TimeSpan.FromSeconds(10), new Clock());
            try { attempt.AcceptProcedureResponse(missingTan ? null! : tan, missingTan ? allowed : null!); Verify(false, "Null lifecycle inputs must fail."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsInitializationState.Ready, "Invalid arguments do not mutate an unused attempt."); }
        }
        Console.WriteLine($"FinTS assembled initialization procedure integration verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static object? Pending(FinTsPinTanInitializationAttempt attempt) => typeof(FinTsPinTanInitializationAttempt).GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(attempt);
    private static (FinTsPinTanResponseBinding, FinTsTanParameterSet, FinTsPermittedProcedureSet) Inputs(JsonElement vector)
    {
        var candidate = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context")));
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
        return (FinTsPinTanResponseBinding.Evaluate(candidate, response), FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
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
