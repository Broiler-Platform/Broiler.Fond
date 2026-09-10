using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSynchronizationAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var clock = new Clock(); using var attempt = New(clock); var candidate = Candidate(vector); var recovery = Recovery(vector);
            var responses = vector.GetProperty("responses").EnumerateArray().Select(Data).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition; FinTsPinTanSynchronizationEvidence? evidence = null;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": transition = attempt.Start(candidate, recovery).ToString(); break;
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
                Verify(snapshot.State == FinTsPinTanSynchronizationAttemptState.AwaitingResponse ? ReferenceEquals(Pending(attempt), candidate) && ReferenceEquals(RecoveryPending(attempt), recovery) && snapshot.RemainingTime == TimeSpan.FromMilliseconds(10000 - clock.Milliseconds) : (Pending(attempt) is null && RecoveryPending(attempt) is null) && snapshot.RemainingTime == TimeSpan.Zero, "Active attempts pin the exact candidate; every terminal path releases metadata and deadline output.");
                if (evidence is not null) { Verify(evidence.NextStep == (snapshot.State == FinTsPinTanSynchronizationAttemptState.CloseAndReinitializeRequired ? FinTsSynchronizationNextStep.CloseAndReinitializeRequired : FinTsSynchronizationNextStep.StopForReview), "Every scoped handoff either requires closing/reinitialization or stops for review."); Verify(ReferenceEquals(evidence.Request, candidate) && ReferenceEquals(evidence.Data, responses[step.GetProperty("index").GetInt32()]) && ReferenceEquals(evidence.RecoveryContext, recovery), "Returned evidence belongs to the pinned candidate and exact accepted synchronization source."); }
            }
            Verify((Pending(attempt) is null && RecoveryPending(attempt) is null), "Every independent terminal trace releases pending references.");
        }
        Verify(vectors.Length == 21, "All twenty-one independent attempt traces ran.");
        var sample = vectors[0]; var request = Candidate(sample); var data = Data(sample.GetProperty("responses")[0]);
        using (var attempt = New())
        {
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.Ready && attempt.GetSnapshot().RemainingTime == TimeSpan.Zero && attempt.AcceptResponse(data).Transition == FinTsPinTanSynchronizationAttemptTransition.WrongState, "Ready attempts have no active deadline and cannot accept responses.");
            Verify(attempt.Start(request) == FinTsPinTanSynchronizationAttemptTransition.CandidateRecorded && attempt.Start(Candidate(sample)) == FinTsPinTanSynchronizationAttemptTransition.WrongState && ReferenceEquals(Pending(attempt), request), "A second candidate cannot replace the pinned context or restart its deadline.");
            var handed = attempt.AcceptResponse(data);
            Verify(handed.Evidence!.HasMatchingEvidence && (Pending(attempt) is null && RecoveryPending(attempt) is null) && handed.Evidence.MatchingReport!.SystemId == "PUBLIC-ASSIGNED", "Caller-owned evidence survives terminal request cleanup.");
            attempt.Cancel(); attempt.Stop(); attempt.Dispose();
            Verify(attempt.AcceptResponse(Data(sample.GetProperty("responses")[0])).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.CloseAndReinitializeRequired, "Reparsed responses, cancellation and disposal cannot change completion or repeat handoff.");
        }
        using (var attempt = New())
        {
            attempt.Start(request);
            var foreign = Data(vectors[2].GetProperty("responses")[0]);
            Verify(attempt.AcceptResponse(foreign).Transition == FinTsPinTanSynchronizationAttemptTransition.ContextMismatch && attempt.GetSnapshot().BindingIssues.HasFlag(FinTsPinTanResponseBindingIssue.SegmentRoleMismatch) && attempt.GetSnapshot().ResponsesHandled == 0, "Foreign references retain only scalar mismatch diagnostics and do not consume the candidate.");
            attempt.AcceptResponse(data);
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanSynchronizationIssue.None && attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.None, "A subsequent valid response clears transient mismatch diagnostics.");
        }
        var recoverySample = vectors[14]; var recoveryRequest = Candidate(recoverySample); var recoveryContext = Recovery(recoverySample)!;
        var recoveryData = Data(recoverySample.GetProperty("responses")[0]);
        using (var attempt = New())
        {
            attempt.Start(recoveryRequest, recoveryContext);
            Verify(attempt.Start(Candidate(recoverySample), new FinTsSynchronizationRecoveryContext("PUBLIC-OTHER", 1)) == FinTsPinTanSynchronizationAttemptTransition.WrongState && ReferenceEquals(RecoveryPending(attempt), recoveryContext), "Another start cannot replace the original recovery context.");
            var result = attempt.AcceptResponse(recoveryData);
            Verify(result.Evidence!.MatchingReport!.LastMessageNumber == 9999 && ReferenceEquals(result.Evidence.RecoveryContext, recoveryContext) && RecoveryPending(attempt) is null, "The maximum reported counter remains caller-owned evidence with the exact recovery context, never applied by the attempt.");
        }
        foreach (int index in new[] { 15, 16 })
        {
            using var attempt = New(); attempt.Start(Candidate(vectors[index]), Recovery(vectors[index]));
            Verify(attempt.GetSnapshot().SynchronizationIssues == (index == 15 ? FinTsSynchronizationIssue.RecoveryContextMissing : FinTsSynchronizationIssue.RecoveryContextMismatch) && attempt.GetSnapshot().CandidatesRecorded == 0 && RecoveryPending(attempt) is null, "Invalid recovery input is rejected with precise scalar diagnostics before ownership.");
        }
        // Three active clock observations surround binding, semantic comparison and evidence handoff.
        foreach (int stage in new[] { 1, 2, 3 })
            foreach (string failure in new[] { "expiry", "cancel", "clock" })
            {
                var clock = new Clock(); using var attempt = New(clock); using var cancellation = new CancellationTokenSource(); attempt.Start(recoveryRequest, recoveryContext);
                clock.AfterReads = stage; clock.Action = () =>
                {
                    if (failure == "expiry") { clock.Milliseconds = 10000; }
                    else if (failure == "cancel") { cancellation.Cancel(); }
                    else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                };
                try
                {
                    var result = attempt.AcceptResponse(recoveryData, cancellation.Token);
                    Verify(failure != "cancel" && result.Transition == FinTsPinTanSynchronizationAttemptTransition.Terminal && result.Evidence is null, "Expiry/clock failure withholds evidence at every processing boundary.");
                }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Cancellation withholds evidence at every processing boundary."); }
                var expected = failure == "expiry" ? FinTsPinTanSynchronizationAttemptState.TimedOut : failure == "cancel" ? FinTsPinTanSynchronizationAttemptState.Cancelled : FinTsPinTanSynchronizationAttemptState.ClockInvalid;
                Verify(attempt.GetSnapshot().State == expected && attempt.GetSnapshot().ResponsesHandled == 0 && (Pending(attempt) is null && RecoveryPending(attempt) is null), "Failed processing releases context without counting or retaining a response.");
            }
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New(); using var cancellation = new CancellationTokenSource(); if (!start) { attempt.Start(request); }
            cancellation.Cancel();
            try { if (start) { attempt.Start(request, cancellationToken: cancellation.Token); } else { attempt.AcceptResponse(data, cancellation.Token); } Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.Cancelled && (Pending(attempt) is null && RecoveryPending(attempt) is null), "Pre-cancellation clears ready/active context and publishes nothing."); }
        }
        var failingStart = new Clock { AfterReads = 1, Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") };
        using (var attempt = New(failingStart))
        { Verify(attempt.Start(request) == FinTsPinTanSynchronizationAttemptTransition.Terminal && attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.ClockInvalid && attempt.GetSnapshot().CandidatesRecorded == 0 && (Pending(attempt) is null && RecoveryPending(attempt) is null), "A failing start clock records no candidate and exposes no clock exception detail."); }
        var reentrantClock = new Clock();
        using (var attempt = New(reentrantClock))
        {
            attempt.Start(request); reentrantClock.AfterReads = 2; reentrantClock.Action = attempt.Stop;
            Verify(attempt.AcceptResponse(data).Evidence is null && attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.Stopped && (Pending(attempt) is null && RecoveryPending(attempt) is null), "Reentrant abandonment at a clock boundary cannot resurrect the attempt or hand out evidence.");
        }
        foreach (TimeSpan invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try { using var attempt = new FinTsPinTanSynchronizationAttempt(invalid); Verify(false, "Invalid lifetimes must fail."); }
            catch (ArgumentOutOfRangeException) { Verify(true, "Lifetime bounds are enforced."); }
        }
        foreach (bool throws in new[] { false, true })
        {
            try { using var attempt = New(new Clock { Frequency = 0, ThrowFrequency = throws }); Verify(false, "Invalid clock must fail."); }
            catch (ArgumentException error) { Verify(error.InnerException is null && !error.ToString().Contains("PUBLIC-CLOCK-DETAIL", StringComparison.Ordinal), "Clock validation uses fixed diagnostics without forwarded exception contents."); }
        }
        using (var attempt = New())
        {
            attempt.Start(request); var results = new FinTsPinTanSynchronizationAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptResponse(data));
            Verify(results.Count(r => r.Evidence is not null) == 1 && results.Count(r => r.Transition == FinTsPinTanSynchronizationAttemptTransition.CloseAndReinitializeRequired) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && (Pending(attempt) is null && RecoveryPending(attempt) is null), "Concurrent response submissions hand out evidence exactly once and release the pending candidate.");
        }
        Verify(typeof(FinTsPinTanSynchronizationSnapshot).GetProperties().All(p => p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)), "Snapshots expose only scalar state/count/time/issue diagnostics.");
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New();
            try { if (start) { attempt.Start(null!); } else { attempt.AcceptResponse(null!); } Verify(false, "Null required inputs must fail."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationAttemptState.Ready && (Pending(attempt) is null && RecoveryPending(attempt) is null), "Null input does not partially record or close an unused attempt."); }
        }
        Console.WriteLine($"FinTS assembled PIN/TAN synchronization attempt verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static FinTsPinTanSynchronizationAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static object? Pending(FinTsPinTanSynchronizationAttempt attempt) => typeof(FinTsPinTanSynchronizationAttempt).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);
    private static object? RecoveryPending(FinTsPinTanSynchronizationAttempt attempt) => typeof(FinTsPinTanSynchronizationAttempt).GetField("_recovery", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);
    private static FinTsSynchronizationRecoveryContext? Recovery(JsonElement vector) => vector.GetProperty("previousDialogueId").ValueKind == JsonValueKind.Null ? null : new(vector.GetProperty("previousDialogueId").GetString()!, vector.GetProperty("lastSubmittedMessageNumber").GetInt32());
    private static FinTsPinTanRequestBinding Candidate(JsonElement vector) => FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context")));
    private static FinTsSynchronizationDataSet Data(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!));
        return FinTsSynchronizationDataSet.Parse(vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame));
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
