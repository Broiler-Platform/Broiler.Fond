using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanInitializationAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-initialization-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var clock = new Clock(); using var attempt = New(clock); var candidate = Candidate(vector);
            var responses = vector.GetProperty("responses").EnumerateArray().Select(Parameters).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition; FinTsPinTanInitializationEvidence? evidence = null;
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
                Verify(snapshot.State == FinTsInitializationState.AwaitingResponse ? ReferenceEquals(Pending(attempt), candidate) && snapshot.RemainingTime == TimeSpan.FromMilliseconds(10000 - clock.Milliseconds) : Pending(attempt) is null && snapshot.RemainingTime == TimeSpan.Zero, "Active attempts pin the exact candidate; every terminal path releases metadata and deadline output.");
                if (evidence is not null) { Verify(ReferenceEquals(evidence.Request, candidate) && ReferenceEquals(evidence.Parameters, responses[step.GetProperty("index").GetInt32()]), "Returned evidence belongs to the pinned candidate and exact accepted parameter source."); }
            }
            Verify(Pending(attempt) is null, "Every independent terminal trace releases pending references.");
        }
        Verify(vectors.Length == 13, "All thirteen independent attempt traces ran.");
        var sample = vectors[0]; var request = Candidate(sample); var parameters = Parameters(sample.GetProperty("responses")[0]);
        using (var attempt = New())
        {
            Verify(attempt.GetSnapshot().State == FinTsInitializationState.Ready && attempt.GetSnapshot().RemainingTime == TimeSpan.Zero && attempt.AcceptResponse(parameters).Transition == FinTsInitializationTransition.WrongState, "Ready attempts have no active deadline and cannot accept responses.");
            Verify(attempt.Start(request) == FinTsInitializationTransition.RequestRecorded && attempt.Start(Candidate(sample)) == FinTsInitializationTransition.WrongState && ReferenceEquals(Pending(attempt), request), "A second candidate cannot replace the pinned context or restart its deadline.");
            var handed = attempt.AcceptResponse(parameters);
            Verify(handed.Evidence!.HasMatchingEvidence && Pending(attempt) is null && handed.Evidence.Parameters.Accounts.Count == 1, "Caller-owned evidence survives terminal request cleanup.");
            attempt.Cancel(); attempt.Stop(); attempt.Dispose();
            Verify(attempt.AcceptResponse(Parameters(sample.GetProperty("responses")[0])).Evidence is null && attempt.GetSnapshot().State == FinTsInitializationState.ExecutionReported, "Reparsed responses, cancellation and disposal cannot change completion or repeat handoff.");
        }
        using (var attempt = New())
        {
            attempt.Start(request);
            var foreign = Parameters(vectors[2].GetProperty("responses")[0]);
            Verify(attempt.AcceptResponse(foreign).Transition == FinTsInitializationTransition.ContextMismatch && attempt.GetSnapshot().BindingIssues.HasFlag(FinTsPinTanResponseBindingIssue.SegmentRoleMismatch) && attempt.GetSnapshot().ResponsesHandled == 0, "Foreign references retain only scalar mismatch diagnostics and do not consume the candidate.");
            attempt.AcceptResponse(parameters);
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanInitializationIssue.None && attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.None, "A subsequent valid response clears transient mismatch diagnostics.");
        }
        // Three active clock observations surround binding, semantic comparison and evidence handoff.
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
                    var result = attempt.AcceptResponse(parameters, cancellation.Token);
                    Verify(failure != "cancel" && result.Transition == FinTsInitializationTransition.Terminal && result.Evidence is null, "Expiry/clock failure withholds evidence at every processing boundary.");
                }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Cancellation withholds evidence at every processing boundary."); }
                var expected = failure == "expiry" ? FinTsInitializationState.TimedOut : failure == "cancel" ? FinTsInitializationState.Cancelled : FinTsInitializationState.ClockInvalid;
                Verify(attempt.GetSnapshot().State == expected && attempt.GetSnapshot().ResponsesHandled == 0 && Pending(attempt) is null, "Failed processing releases context without counting or retaining a response.");
            }
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New(); using var cancellation = new CancellationTokenSource(); if (!start) { attempt.Start(request); }
            cancellation.Cancel();
            try { if (start) { attempt.Start(request, cancellation.Token); } else { attempt.AcceptResponse(parameters, cancellation.Token); } Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(attempt.GetSnapshot().State == FinTsInitializationState.Cancelled && Pending(attempt) is null, "Pre-cancellation clears ready/active context and publishes nothing."); }
        }
        var failingStart = new Clock { AfterReads = 1, Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") };
        using (var attempt = New(failingStart))
        { Verify(attempt.Start(request) == FinTsInitializationTransition.Terminal && attempt.GetSnapshot().State == FinTsInitializationState.ClockInvalid && attempt.GetSnapshot().CandidatesRecorded == 0 && Pending(attempt) is null, "A failing start clock records no candidate and exposes no clock exception detail."); }
        var reentrantClock = new Clock();
        using (var attempt = New(reentrantClock))
        {
            attempt.Start(request); reentrantClock.AfterReads = 2; reentrantClock.Action = attempt.Stop;
            Verify(attempt.AcceptResponse(parameters).Evidence is null && attempt.GetSnapshot().State == FinTsInitializationState.Stopped && Pending(attempt) is null, "Reentrant abandonment at a clock boundary cannot resurrect the attempt or hand out evidence.");
        }
        foreach (TimeSpan invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try { using var attempt = new FinTsPinTanInitializationAttempt(invalid); Verify(false, "Invalid lifetimes must fail."); }
            catch (ArgumentOutOfRangeException) { Verify(true, "Lifetime bounds are enforced."); }
        }
        foreach (bool throws in new[] { false, true })
        {
            try { using var attempt = New(new Clock { Frequency = 0, ThrowFrequency = throws }); Verify(false, "Invalid clock must fail."); }
            catch (ArgumentException error) { Verify(error.InnerException is null && !error.ToString().Contains("PUBLIC-CLOCK-DETAIL", StringComparison.Ordinal), "Clock validation uses fixed diagnostics without forwarded exception contents."); }
        }
        using (var attempt = New())
        {
            attempt.Start(request); var results = new FinTsPinTanInitializationAttemptResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptResponse(parameters));
            Verify(results.Count(r => r.Evidence is not null) == 1 && results.Count(r => r.Transition == FinTsInitializationTransition.ExecutionReported) == 1 && attempt.GetSnapshot().ResponsesHandled == 1 && Pending(attempt) is null, "Concurrent response submissions hand out evidence exactly once and release the pending candidate.");
        }
        Verify(typeof(FinTsPinTanInitializationSnapshot).GetProperties().All(p => p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)), "Snapshots expose only scalar state/count/time/issue diagnostics.");
        foreach (bool start in new[] { false, true })
        {
            using var attempt = New();
            try { if (start) { attempt.Start(null!); } else { attempt.AcceptResponse(null!); } Verify(false, "Null required inputs must fail."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsInitializationState.Ready && Pending(attempt) is null, "Null input does not partially record or close an unused attempt."); }
        }
        Console.WriteLine($"FinTS assembled PIN/TAN initialization attempt verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static FinTsPinTanInitializationAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static object? Pending(FinTsPinTanInitializationAttempt attempt) => typeof(FinTsPinTanInitializationAttempt).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);
    private static FinTsPinTanRequestBinding Candidate(JsonElement vector) => FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context")));
    private static FinTsParameterSet Parameters(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!));
        return FinTsParameterSet.Parse(vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame));
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
