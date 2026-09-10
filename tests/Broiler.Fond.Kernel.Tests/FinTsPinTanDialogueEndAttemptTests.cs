using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanDialogueEndAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-dialogue-end-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var clock = new Clock(); using var attempt = New(clock); var candidate = Candidate(vector);
            var responses = vector.GetProperty("responses").EnumerateArray().Select(Response).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition; FinTsPinTanDialogueEndEvidence? evidence = null;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": transition = attempt.Start(candidate).ToString(); break;
                    case "response":
                        var result = attempt.AcceptResponse(responses[step.GetProperty("index").GetInt32()]);
                        transition = result.Transition.ToString(); evidence = result.Evidence; break;
                    case "cancel": attempt.Cancel(); transition = "Cancelled"; break;
                    case "stop": attempt.Stop(); transition = "Stopped"; break;
                    case "dispose": attempt.Dispose(); transition = "Stopped"; break;
                    case "snapshot": _ = attempt.GetSnapshot(); transition = "Snapshot"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                var snapshot = attempt.GetSnapshot();
                Verify(transition == step.GetProperty("transition").GetString() && snapshot.State.ToString() == step.GetProperty("state").GetString(), "Independent attempt transition/state matches: " + vector.GetProperty("name").GetString());
                Verify((evidence is not null) == step.GetProperty("evidence").GetBoolean() && snapshot.CandidatesRecorded == step.GetProperty("candidates").GetInt32() && snapshot.ResponsesHandled == step.GetProperty("handled").GetInt32(), "Independent handoff and recorded/handled counts match, including review outcomes.");
                Verify(snapshot.State == FinTsPinTanDialogueEndAttemptState.AwaitingResponse ? ReferenceEquals(Pending(attempt), candidate) && snapshot.RemainingTime == TimeSpan.FromMilliseconds(10000 - clock.Milliseconds) : Pending(attempt) is null && snapshot.RemainingTime == TimeSpan.Zero, "Active attempts pin the exact candidate; every terminal path releases metadata and deadline output.");
                if (evidence is not null) { Verify(evidence.Outcome == (snapshot.State == FinTsPinTanDialogueEndAttemptState.ReinitializationRequired ? FinTsDialogueEndOutcome.ClosureReported : snapshot.State == FinTsPinTanDialogueEndAttemptState.Aborted ? FinTsDialogueEndOutcome.AbortReported : FinTsDialogueEndOutcome.NeedsReview), "Only qualified semantic outcomes select closure, abort or review state."); Verify(ReferenceEquals(evidence.Request, candidate) && ReferenceEquals(evidence.Response, responses[step.GetProperty("index").GetInt32()]), "Returned evidence belongs to the pinned candidate and exact accepted closing response."); }
            }
            Verify(Pending(attempt) is null, "Every independent terminal trace releases pending references.");
        }
        Verify(vectors.Length == 20, "All twenty independent attempt traces ran.");
        var sample = vectors[0]; var request = Candidate(sample); var response = Response(sample.GetProperty("responses")[0]);
        using (var attempt = New())
        {
            Verify(attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.Ready && attempt.GetSnapshot().RemainingTime == TimeSpan.Zero && attempt.AcceptResponse(response).Transition == FinTsPinTanDialogueEndAttemptTransition.WrongState, "Ready attempts have no active deadline and cannot accept responses.");
            Verify(attempt.Start(request) == FinTsPinTanDialogueEndAttemptTransition.CandidateRecorded && attempt.Start(Candidate(sample)) == FinTsPinTanDialogueEndAttemptTransition.WrongState && ReferenceEquals(Pending(attempt), request), "A second candidate cannot replace the pinned context or restart its deadline.");
            var handed = attempt.AcceptResponse(response);
            Verify(handed.Evidence!.HasMatchingEvidence && Pending(attempt) is null && handed.Evidence.Outcome == FinTsDialogueEndOutcome.ClosureReported, "Caller-owned evidence survives terminal request cleanup.");
            attempt.Cancel(); attempt.Stop(); attempt.Dispose();
            Verify(attempt.AcceptResponse(Response(sample.GetProperty("responses")[0])).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.ReinitializationRequired, "Reparsed responses, cancellation and disposal cannot change completion or repeat handoff.");
        }
        using (var attempt = New())
        {
            attempt.Start(request);
            var foreign = Response(vectors[2].GetProperty("responses")[0]);
            Verify(attempt.AcceptResponse(foreign).Transition == FinTsPinTanDialogueEndAttemptTransition.ContextMismatch && attempt.GetSnapshot().BindingIssues.HasFlag(FinTsPinTanResponseBindingIssue.UnknownSegmentReference) && attempt.GetSnapshot().ResponsesHandled == 0, "Foreign references retain only scalar mismatch diagnostics and do not consume the candidate.");
            attempt.AcceptResponse(response);
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanDialogueEndIssue.None && attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.None, "A subsequent valid response clears transient mismatch diagnostics.");
        }
        using (var attempt = New())
        {
            attempt.Start(request);
            var abort = Response(vectors[11].GetProperty("responses")[0]);
            var handed = attempt.AcceptResponse(abort); attempt.Cancel(); attempt.Dispose();
            Verify(handed.Transition == FinTsPinTanDialogueEndAttemptTransition.AbortReported && handed.Evidence!.AbortReported && attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.Aborted && attempt.Start(request) == FinTsPinTanDialogueEndAttemptTransition.Terminal && Pending(attempt) is null, "Reported abort is immutable, releases context and cannot schedule another close.");
        }
        // Carry actual one-time synchronization evidence into the separately bounded closing phase.
        var original = request.Context; var syncEvidence = original.Synchronization;
        using (var synchronization = new FinTsPinTanSynchronizationAttempt(TimeSpan.FromSeconds(10), new Clock()))
        using (var closing = New())
        {
            synchronization.Start(syncEvidence.Request, syncEvidence.RecoveryContext);
            var handoff = synchronization.AcceptResponse(syncEvidence.Data);
            var context = FinTsPinTanDialogueEndContext.Evaluate(handoff.Evidence!, original.Request, original.Header);
            var candidate = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(context);
            closing.Start(candidate); var result = closing.AcceptResponse(response);
            Verify(ReferenceEquals(result.Evidence!.Request.Context.Synchronization, handoff.Evidence) && result.Transition == FinTsPinTanDialogueEndAttemptTransition.ReinitializationRequired && Pending(closing) is null, "The real synchronization handoff survives closing cleanup as exact caller-owned evidence and still requires reinitialization.");
            Verify(synchronization.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.CloseAndReinitializeRequired && synchronization.AcceptResponse(syncEvidence.Data).Evidence is null, "The closing phase does not resurrect or mutate the finished synchronization attempt.");
        }
        // Three active clock observations surround binding, semantic comparison and evidence handoff.
        foreach (var incoming in new[] { response, Response(vectors[11].GetProperty("responses")[0]) })
            foreach (int stage in new[] { 1, 2, 3 })
                foreach (string failure in new[] { "expiry", "cancel", "clock" })
                {
                    var clock = new Clock(); using var attempt = New(clock); using var cancellation = new CancellationTokenSource(); attempt.Start(request);
                    clock.AfterReads = stage; clock.Action = () =>
                    {
                        if (failure == "expiry") { clock.Milliseconds = 10000; }
                        else if (failure == "cancel") { cancellation.Cancel(); }
                        else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                    };
                    try
                    {
                        var result = attempt.AcceptResponse(incoming, cancellation.Token);
                        Verify(failure != "cancel" && result.Transition == FinTsPinTanDialogueEndAttemptTransition.Terminal && result.Evidence is null, "Expiry/clock failure withholds evidence at every processing boundary.");
                    }
                    catch (OperationCanceledException) { Verify(failure == "cancel", "Cancellation withholds evidence at every processing boundary."); }
                    var expected = failure == "expiry" ? FinTsPinTanDialogueEndAttemptState.TimedOut : failure == "cancel" ? FinTsPinTanDialogueEndAttemptState.Cancelled : FinTsPinTanDialogueEndAttemptState.ClockInvalid;
                    Verify(attempt.GetSnapshot().State == expected && attempt.GetSnapshot().ResponsesHandled == 0 && Pending(attempt) is null, "Failed processing releases context without counting or retaining a response.");
                }
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New(); using var cancellation = new CancellationTokenSource(); if (!start) { attempt.Start(request); }
            cancellation.Cancel();
            try { if (start) { attempt.Start(request, cancellation.Token); } else { attempt.AcceptResponse(response, cancellation.Token); } Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.Cancelled && Pending(attempt) is null, "Pre-cancellation clears ready/active context and publishes nothing."); }
        }
        var failingStart = new Clock { AfterReads = 1, Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") };
        using (var attempt = New(failingStart))
        { Verify(attempt.Start(request) == FinTsPinTanDialogueEndAttemptTransition.Terminal && attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.ClockInvalid && attempt.GetSnapshot().CandidatesRecorded == 0 && Pending(attempt) is null, "A failing start clock records no candidate and exposes no clock exception detail."); }
        var reentrantClock = new Clock();
        using (var attempt = New(reentrantClock))
        {
            attempt.Start(request); reentrantClock.AfterReads = 2; reentrantClock.Action = attempt.Stop;
            Verify(attempt.AcceptResponse(response).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.Stopped && Pending(attempt) is null, "Reentrant abandonment at a clock boundary cannot resurrect the attempt or hand out evidence.");
        }
        foreach (TimeSpan invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try { using var attempt = new FinTsPinTanDialogueEndAttempt(invalid); Verify(false, "Invalid lifetimes must fail."); }
            catch (ArgumentOutOfRangeException) { Verify(true, "Lifetime bounds are enforced."); }
        }
        foreach (bool throws in new[] { false, true })
        {
            try { using var attempt = New(new Clock { Frequency = 0, ThrowFrequency = throws }); Verify(false, "Invalid clock must fail."); }
            catch (ArgumentException error) { Verify(error.InnerException is null && !error.ToString().Contains("PUBLIC-CLOCK-DETAIL", StringComparison.Ordinal), "Clock validation uses fixed diagnostics without forwarded exception contents."); }
        }
        using (var attempt = New())
        {
            attempt.Start(request); var results = new FinTsPinTanDialogueEndAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptResponse(response));
            Verify(results.Count(r => r.Evidence is not null) == 1 && results.Count(r => r.Transition == FinTsPinTanDialogueEndAttemptTransition.ReinitializationRequired) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null, "Concurrent response submissions hand out evidence exactly once and release the pending candidate.");
        }
        Verify(typeof(FinTsPinTanDialogueEndSnapshot).GetProperties().All(p => p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)), "Snapshots expose only scalar state/count/time/issue diagnostics.");
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New();
            try { if (start) { attempt.Start(null!); } else { attempt.AcceptResponse(null!); } Verify(false, "Null required inputs must fail."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanDialogueEndAttemptState.Ready && Pending(attempt) is null, "Null input does not partially record or close an unused attempt."); }
        }
        Console.WriteLine($"FinTS assembled PIN/TAN closing attempt verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static FinTsPinTanDialogueEndAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static object? Pending(FinTsPinTanDialogueEndAttempt attempt) => typeof(FinTsPinTanDialogueEndAttempt).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);
    private static FinTsPinTanDialogueEndRequestBinding Candidate(JsonElement vector) => FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndTests.Context(vector.GetProperty("context")));
    private static FinTsResponse Response(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!));
        return vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal long Frequency { get; set; } = 1000;
        internal bool ThrowFrequency { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => ThrowFrequency ? throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") : Frequency;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
