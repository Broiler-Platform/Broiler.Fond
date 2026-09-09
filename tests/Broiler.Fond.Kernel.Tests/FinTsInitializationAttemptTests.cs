using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsInitializationAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.initialization-attempt-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var traceClock = new Clock(); var attempt = New(traceClock);
            var request = Request(vector); string? user = vector.GetProperty("expectedUserId").GetString();
            var responses = vector.GetProperty("responses").EnumerateArray().Select(v => Parameters(v.GetString()!)).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                traceClock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition; bool evidence = false;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": transition = attempt.Start(request, user).ToString(); break;
                    case "response":
                        var result = attempt.AcceptResponse(responses[step.GetProperty("index").GetInt32()]);
                        transition = result.Transition.ToString(); evidence = result.Evidence is not null; break;
                    case "cancel": attempt.Cancel(); transition = "Cancelled"; break;
                    case "stop": attempt.Stop(); transition = "Stopped"; break;
                    case "snapshot": _ = attempt.GetSnapshot(); transition = "Snapshot"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                Verify(transition == step.GetProperty("expected").GetString(), $"Independent initialization transition differs for {vector.GetProperty("name").GetString()}: {transition}.");
                Verify(attempt.GetSnapshot().State.ToString() == step.GetProperty("state").GetString() && evidence == step.GetProperty("evidence").GetBoolean(), "Independent state and evidence-presence expectations match.");
            }
            Verify(Released(attempt), "Every terminal trace releases retained request and user-context references.");
        }
        Verify(vectors.Length == 8, "All eight initialization attempt traces ran.");
        var sample = vectors[0]; var requestBase = Request(sample);
        string responseWire = sample.GetProperty("responses")[0].GetString()!;
        var parameters = Parameters(responseWire); const string User = "PUBLIC-USER";
        var ready = New();
        Verify(ready.GetSnapshot().State == FinTsInitializationState.Ready && ready.GetSnapshot().RemainingTime == TimeSpan.Zero && ready.GetSnapshot().RequestsRecorded == 0, "No pending context or deadline exists before start.");
        Verify(ready.AcceptResponse(parameters).Transition == FinTsInitializationTransition.WrongState, "Responses cannot initialize attempts.");
        Verify(ready.Start(requestBase, User) == FinTsInitializationTransition.RequestRecorded && ready.GetSnapshot().RequestsRecorded == 1, "One valid request is recorded.");
        Verify(ready.Start(Request(sample), "OTHER") == FinTsInitializationTransition.WrongState, "A second start cannot replace pinned source or expected user identity.");
        var handed = ready.AcceptResponse(parameters);
        Verify(handed.Transition == FinTsInitializationTransition.ExecutionReported && handed.Evidence!.HasMatchingEvidence && ReferenceEquals(handed.Evidence.Request, requestBase) && ReferenceEquals(handed.Evidence.Parameters, parameters) && handed.Evidence.ExpectedUserId == User, "Comparison is recomputed internally against the exact pinned context.");
        Verify(ready.GetSnapshot().ResponsesAccepted == 1 && Released(ready) && handed.Evidence!.Parameters.Accounts.Count == 1, "Caller-owned evidence survives terminal reference cleanup.");
        var replay = ready.AcceptResponse(Parameters(responseWire));
        Verify(replay.Transition == FinTsInitializationTransition.Terminal && replay.Evidence is null, "Reparsed bytes receive no second evidence handoff.");
        ready.Cancel(); ready.Stop();
        Verify(ready.GetSnapshot().State == FinTsInitializationState.ExecutionReported && ready.Start(requestBase, User) == FinTsInitializationTransition.Terminal, "Execution is immutable after terminal cancellation, stop or restart calls.");
        var missing = New();
        Verify(missing.Start(requestBase) == FinTsInitializationTransition.RejectedForReview && missing.GetSnapshot().Issues == FinTsInitializationIssue.UserContextMissing && missing.GetSnapshot().RequestsRecorded == 0 && Released(missing), "Missing identified-user context fails before request retention or recording.");
        var invalid = New();
        foreach (string value in new[] { "", "PUBLIC-SECRET\n", " padded", "€", new string('x', 31) })
        {
            try { invalid.Start(requestBase, value); Verify(false, "Malformed user expectation must fail."); }
            catch (FinTsFormatException error)
            { Verify(error.Error == FinTsSyntaxError.InvalidInitialization && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal) && invalid.GetSnapshot().State == FinTsInitializationState.Ready && Released(invalid), "Malformed caller input returns fixed diagnostics without changing ready state."); }
        }
        Verify(invalid.Start(requestBase, User) == FinTsInitializationTransition.RequestRecorded, "Corrected malformed caller input can start an unused attempt."); invalid.Stop();
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC:1'", "OTHER:1'", StringComparison.Ordinal),
            s => s.Replace("+300+SYNTHETIC+1+", "+300+SYNTHETIC+2+", StringComparison.Ordinal).Replace("HNHBS:8:1+1'", "HNHBS:8:1+2'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "SYNTHETIC:2'", StringComparison.Ordinal),
            s => s.Replace("HIUPA:6:4:3", "HIUPA:6:4:2", StringComparison.Ordinal),
            s => s.Replace("+1+SYNTHETIC:1'", "+1'", StringComparison.Ordinal),
        })
        {
            var attempt = New(); attempt.Start(requestBase, User);
            var unrelated = attempt.AcceptResponse(Parameters(responseWire, edit));
            Verify(unrelated.Transition == FinTsInitializationTransition.ContextMismatch && unrelated.Evidence is null && attempt.GetSnapshot().State == FinTsInitializationState.AwaitingResponse && attempt.GetSnapshot().ResponsesAccepted == 0, "Foreign message/reference leaves the original request pending without a handoff.");
            Verify(attempt.AcceptResponse(parameters).Transition == FinTsInitializationTransition.ExecutionReported && attempt.GetSnapshot().Issues == FinTsInitializationIssue.None && Released(attempt), "A valid candidate clears transient scope issues and completes once.");
        }
        foreach (var edit in new (Func<string, string> Edit, FinTsInitializationIssue Issue)[]
        {
            (s => s.Replace("PUBLIC-USER", "OTHER-USER", StringComparison.Ordinal), FinTsInitializationIssue.UserMismatch),
            (s => s.Replace("PUBLIC-CUSTOMER", "OTHER-CUSTOMER", StringComparison.Ordinal), FinTsInitializationIssue.CustomerMismatch),
            (s => s.Replace("SYNTHETIC", "unbekannt", StringComparison.Ordinal), FinTsInitializationIssue.InvalidAssignedDialogue),
            (s => s.Replace("0020::PUBLIC-PREP-REPLY", "0030::PUBLIC-PREP-REPLY", StringComparison.Ordinal), FinTsInitializationIssue.StatusNeedsReview),
            (s => s.Replace("0010::PUBLIC-REPLY", "9050::PUBLIC-REPLY", StringComparison.Ordinal), FinTsInitializationIssue.ResponseNeedsReview),
        })
        {
            var attempt = New(); attempt.Start(requestBase, User);
            var result = attempt.AcceptResponse(Parameters(responseWire, edit.Edit));
            Verify(result.Transition == FinTsInitializationTransition.RejectedForReview && result.Evidence!.Issues.HasFlag(edit.Issue) && attempt.GetSnapshot().Issues.HasFlag(edit.Issue) && attempt.GetSnapshot().ResponsesAccepted == 0 && Released(attempt), "Bound invalid assignment, identity, status or error evidence is returned once for review.");
            Verify(attempt.AcceptResponse(parameters).Evidence is null && attempt.GetSnapshot().State == FinTsInitializationState.NeedsReview, "A later clean candidate cannot replace terminal review evidence.");
        }
        var clock = new Clock { Milliseconds = 20000 }; var timed = New(clock); timed.Start(requestBase, User); clock.Milliseconds = 29999;
        Verify(timed.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(1), "The absolute deadline starts at recording, not construction.");
        timed.AcceptResponse(Parameters(responseWire, s => s.Replace("SYNTHETIC:1'", "OTHER:1'", StringComparison.Ordinal))); clock.Milliseconds = 30000;
        var late = timed.AcceptResponse(parameters);
        Verify(late.Transition == FinTsInitializationTransition.Terminal && late.Evidence is null && timed.GetSnapshot().State == FinTsInitializationState.TimedOut && Released(timed), "Foreign candidates cannot extend the deadline; equality prevents handoff.");
        foreach (int read in new[] { 3, 4 })
        {
            var attempt = New(new AdvancingClock(read)); attempt.Start(requestBase, User);
            var result = attempt.AcceptResponse(parameters);
            Verify(result.Transition == FinTsInitializationTransition.Terminal && result.Evidence is null && attempt.GetSnapshot().State == FinTsInitializationState.TimedOut && attempt.GetSnapshot().ResponsesAccepted == 0 && Released(attempt), "Expiry during preflight or comparison prevents terminal handoff.");
        }
        var regressionClock = new Clock(); var regression = New(regressionClock); regression.Start(requestBase, User); regressionClock.Milliseconds = -1;
        Verify(regression.GetSnapshot().State == FinTsInitializationState.ClockInvalid && Released(regression), "Clock regression terminates and releases context.");
        foreach (bool beforeStart in new[] { false, true })
            foreach (bool stop in new[] { false, true })
            {
                var attempt = New(); if (!beforeStart) { attempt.Start(requestBase, User); }
                if (stop) { attempt.Stop(); } else { attempt.Cancel(); }
                Verify(attempt.AcceptResponse(parameters).Evidence is null && attempt.GetSnapshot().State == (stop ? FinTsInitializationState.Stopped : FinTsInitializationState.Cancelled) && Released(attempt), "Cancel and stop before/after start are terminal and release context.");
            }
        foreach (Action action in new Action[] { () => new FinTsInitializationAttempt(TimeSpan.Zero), () => new FinTsInitializationAttempt(TimeSpan.FromMinutes(16)), () => New(new InvalidClock()) })
        {
            try { action(); Verify(false, "Invalid lifetime/clock must fail."); }
            catch (ArgumentException) { Verify(true, "Invalid lifecycle policy is rejected."); }
        }
        Verify(new FinTsInitializationAttempt(TimeSpan.FromMinutes(15)).GetSnapshot().State == FinTsInitializationState.Ready, "The maximum allowed lifetime is accepted.");
        foreach (Action action in new Action[] { () => New().Start(null!, User), () => New().AcceptResponse(null!) })
        {
            try { action(); Verify(false, "Null input must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null lifecycle inputs are rejected."); }
        }
        var racing = New(); var starts = new FinTsInitializationTransition[32];
        Parallel.For(0, starts.Length, i => starts[i] = racing.Start(requestBase, User));
        Verify(starts.Count(t => t == FinTsInitializationTransition.RequestRecorded) == 1 && racing.GetSnapshot().RequestsRecorded == 1, "Concurrent starts pin one request.");
        var results = new FinTsInitializationAttemptResult[32];
        Parallel.For(0, results.Length, i => results[i] = racing.AcceptResponse(parameters));
        Verify(results.Count(r => r.Transition == FinTsInitializationTransition.ExecutionReported) == 1 && results.Count(r => r.Evidence is not null) == 1 && racing.GetSnapshot().ResponsesAccepted == 1 && Released(racing), "Concurrent responses produce one terminal handoff.");
        for (int i = 0; i < 16; i++)
        {
            var attempt = New(); attempt.Start(requestBase, User); FinTsInitializationAttemptResult? result = null;
            Parallel.Invoke(() => attempt.Cancel(), () => result = attempt.AcceptResponse(parameters));
            Verify(attempt.GetSnapshot().State is FinTsInitializationState.Cancelled or FinTsInitializationState.ExecutionReported &&
                (result!.Evidence is not null) == (attempt.GetSnapshot().State == FinTsInitializationState.ExecutionReported) && Released(attempt), "Cancellation/response races yield one immutable terminal outcome.");
        }
        Verify(new object[] { handed, handed.Evidence!, racing, racing.GetSnapshot() }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default lifecycle diagnostics exclude identity and parameter data.");
        Verify(typeof(FinTsInitializationSnapshot).GetProperties().All(p => p.PropertyType.IsValueType), "Snapshot properties expose only scalars, never source identifiers or references.");
        Console.WriteLine($"FinTS initialization attempt verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static FinTsInitializationAttempt New(TimeProvider? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock must not be used.");
    }
    private sealed class AdvancingClock(int expireAt) : TimeProvider
    {
        private int _reads;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => ++_reads >= expireAt ? 10000 : 0;
    }
    private sealed class InvalidClock : TimeProvider { public override long TimestampFrequency => 0; }
    private static bool Released(FinTsInitializationAttempt attempt) => typeof(FinTsInitializationAttempt).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => !f.IsInitOnly && !f.FieldType.IsValueType).All(f => f.GetValue(attempt) is null);
    private static FinTsUnsignedInitializationRequest Request(JsonElement vector) => FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)));
    private static FinTsParameterSet Parameters(string base64, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(base64));
        if (edit is not null) { wire = edit(wire); }
        wire = wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..];
        return FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire))));
    }
}
