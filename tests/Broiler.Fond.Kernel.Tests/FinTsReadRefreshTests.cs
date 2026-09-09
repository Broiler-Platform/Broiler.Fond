using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadRefreshTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-refresh-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var clock = new TestClock();
            var attempt = new FinTsReadRefreshAttempt(TimeSpan.FromSeconds(10), vector.GetProperty("maximumPages").GetInt32(), clock);
            var capability = Capability(vector.GetProperty("parametersBase64").GetString()!);
            var requests = vector.GetProperty("requests").EnumerateArray().Select(e => Request(e.GetString()!)).ToArray();
            var responses = vector.GetProperty("responses").EnumerateArray().Select(e => Data(e.GetString()!)).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.SetMilliseconds(step.GetProperty("atMilliseconds").GetInt64());
                int index = step.GetProperty("index").GetInt32();
                string result;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": result = attempt.Start(requests[index], capability).ToString(); break;
                    case "continue": result = attempt.RecordContinuation(requests[index]).ToString(); break;
                    case "response": result = attempt.AcceptResponse(responses[index]).ToString(); break;
                    case "cancel": attempt.Cancel(); result = "Cancelled"; break;
                    case "stop": attempt.Stop(); result = "Stopped"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                Verify(result == step.GetProperty("expected").GetString(), $"Independent read-refresh transition differs in {vector.GetProperty("name").GetString()}: {result}.");
                Verify(attempt.GetSnapshot().State.ToString() == step.GetProperty("state").GetString(), "Independent read-refresh state matches.");
            }
            Verify(attempt.ContinuationEvidence is null && Released(attempt), "Terminal traces release retained request, parameter, page and dialogue references.");
        }
        Verify(vectors.Length == 8, "All eight independent read-refresh traces ran.");
        var sample = vectors[1];
        string parameters = sample.GetProperty("parametersBase64").GetString()!;
        string initialWire = sample.GetProperty("requests")[0].GetString()!;
        string nextWire = sample.GetProperty("requests")[1].GetString()!;
        string partialWire = sample.GetProperty("responses")[0].GetString()!;
        string finalWire = sample.GetProperty("responses")[1].GetString()!;
        var cap = Capability(parameters);
        var initial = Request(initialWire); var next = Request(nextWire);
        var partial = Data(partialWire); var final = Data(finalWire);
        FinTsReadRefreshAttempt New(TestClock? clock = null, int maximum = 16) => new(TimeSpan.FromSeconds(10), maximum, clock ?? new TestClock());
        var ready = New();
        Verify(ready.GetSnapshot().State == FinTsReadRefreshState.Ready && ready.GetSnapshot().RemainingTime == TimeSpan.Zero && ready.GetSnapshot().MaximumPages == 16, "Ready has no running deadline or pending request.");
        Verify(ready.AcceptResponse(partial) == FinTsReadRefreshTransition.WrongState && ready.RecordContinuation(next) == FinTsReadRefreshTransition.WrongState, "Responses and continuation cannot initialize an attempt.");
        Verify(ready.Start(initial, cap) == FinTsReadRefreshTransition.RequestRecorded && ready.GetSnapshot().RequestsRecorded == 1 && ready.GetSnapshot().PagesAccepted == 0, "Start records one request without accepting data.");
        Verify(ready.Start(next, Capability(parameters)) == FinTsReadRefreshTransition.WrongState && ready.RecordContinuation(next) == FinTsReadRefreshTransition.WrongState, "Neither start nor continuation replaces an in-flight request.");
        Verify(ready.AcceptResponse(partial) == FinTsReadRefreshTransition.PartialAccepted && ready.GetSnapshot().PagesAccepted == 1 && ReferenceEquals(ready.ContinuationEvidence!.Response, partial), "A partial response exposes the exact accepted source once.");
        Verify(ready.AcceptResponse(Data(partialWire)) == FinTsReadRefreshTransition.ReplayRejected, "Reparsing accepted bytes does not bypass counter replay rejection.");
        Verify(ready.RecordContinuation(initial) == FinTsReadRefreshTransition.ReplayRejected, "Consumed client/bank counters cannot create another request.");
        foreach (var altered in new[]
        {
            Request(nextWire, s => s.Replace("PUBLIC?+PAGE", "OTHER", StringComparison.Ordinal)),
            Request(nextWire, s => s.Replace("PUBLIC-IBAN", "OTHER-IBAN", StringComparison.Ordinal)),
            Request(nextWire, s => s.Replace("+N++", "+N+1+", StringComparison.Ordinal)),
            Request(nextWire, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal)),
            Request(nextWire, s => Numbered(s, 4)),
        })
        {
            Verify(ready.RecordContinuation(altered) == FinTsReadRefreshTransition.ContextMismatch && ready.GetSnapshot().State == FinTsReadRefreshState.WaitingToContinue && ready.GetSnapshot().RequestsRecorded == 1, "Bad continuation context consumes no slot or accepted page.");
        }
        Verify(ready.RecordContinuation(next) == FinTsReadRefreshTransition.RequestRecorded && ready.ContinuationEvidence is null && ready.GetSnapshot().LastIssues == FinTsReadContextIssue.None, "Valid explicit continuation records once and clears transient review issues.");
        Verify(ready.AcceptResponse(partial) == FinTsReadRefreshTransition.ReplayRejected && ready.GetSnapshot().State == FinTsReadRefreshState.AwaitingResponse, "An old page cannot consume the new pending request.");
        Verify(ready.AcceptResponse(final) == FinTsReadRefreshTransition.ExecutionReported && ready.GetSnapshot().PagesAccepted == 2 && ready.GetSnapshot().RequestsRecorded == 2, "Final execution is only a report and retains scalar counts.");
        ready.Cancel(); ready.Stop();
        Verify(ready.Start(initial, cap) == FinTsReadRefreshTransition.Terminal && ready.GetSnapshot().State == FinTsReadRefreshState.ExecutionReported && Released(ready), "Terminal execution cannot be replaced or restarted.");
        foreach (var bad in new[] { Request(nextWire), Request(initialWire, s => s.Replace("+N'", "+J'", StringComparison.Ordinal)), Request(initialWire, s => s.Replace("PUBLIC-IBAN", "OTHER-IBAN", StringComparison.Ordinal)) })
        {
            var attempt = New();
            Verify(attempt.Start(bad, cap) == FinTsReadRefreshTransition.RejectedForReview && attempt.GetSnapshot().RequestsRecorded == 0 && attempt.GetSnapshot().LastIssues != FinTsReadContextIssue.None && Released(attempt), "Invalid initial scope terminates without retaining source data or recording a request.");
        }
        var foreignCap = New();
        Verify(foreignCap.Start(initial, Capability(parameters, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal))) == FinTsReadRefreshTransition.RejectedForReview, "Foreign parameter dialogue cannot start an attempt.");
        foreach (var bad in new[] { Data(finalWire), Data(partialWire, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal)), Data(partialWire, s => s.Replace("HISAL:4:8:2", "HISAL:4:8:1", StringComparison.Ordinal)) })
        {
            var attempt = New(); attempt.Start(initial, cap);
            Verify(attempt.AcceptResponse(bad) == FinTsReadRefreshTransition.ContextMismatch && attempt.GetSnapshot().PagesAccepted == 0 && attempt.GetSnapshot().State == FinTsReadRefreshState.AwaitingResponse, "Wrong message/dialogue/reference leaves the pending request intact.");
            Verify(attempt.AcceptResponse(partial) == FinTsReadRefreshTransition.PartialAccepted, "A matching response can still advance after an unrelated candidate.");
        }
        foreach (string bad in new[] { "OTHER-IBAN", "PUBLIC-OTHER" })
        {
            var attempt = New(); attempt.Start(initial, cap);
            var mismatched = Data(partialWire, s => s.Replace("PUBLIC-IBAN", bad, StringComparison.Ordinal));
            Verify(attempt.AcceptResponse(mismatched) == FinTsReadRefreshTransition.RejectedForReview && attempt.GetSnapshot().LastIssues.HasFlag(FinTsReadContextIssue.ResponseAccountMismatch) && attempt.GetSnapshot().PagesAccepted == 0 && Released(attempt), "Bound account mismatch terminates without accepting a page.");
        }
        var limit = New(maximum: 1); limit.Start(initial, cap);
        Verify(limit.AcceptResponse(partial) == FinTsReadRefreshTransition.Terminal && limit.GetSnapshot().State == FinTsReadRefreshState.PageLimitReached && limit.GetSnapshot().PagesAccepted == 1 && Released(limit), "Partial last allowed page records evidence count then releases it without further continuation.");
        var finishAtLimit = New(maximum: 1); finishAtLimit.Start(initial, cap);
        Verify(finishAtLimit.AcceptResponse(Data(finalWire, s => Numbered(s, 2))) == FinTsReadRefreshTransition.ExecutionReported, "Execution reported on the final permitted page is accepted.");
        foreach (bool execution in new[] { false, true })
        {
            var attempt = New();
            var last = Request(initialWire, s => Numbered(s, 9999));
            Verify(attempt.Start(last, cap) == FinTsReadRefreshTransition.RequestRecorded, "The last representable request can still be recorded.");
            attempt.AcceptResponse(Data(execution ? finalWire : partialWire, s => Numbered(s, 9999)));
            Verify(attempt.GetSnapshot().State == (execution ? FinTsReadRefreshState.ExecutionReported : FinTsReadRefreshState.CounterExhausted) && Released(attempt), "Last-counter partial data cannot wrap; final execution can terminate normally.");
        }
        foreach (bool waiting in new[] { false, true })
        {
            var clock = new TestClock(); var attempt = New(clock); clock.SetMilliseconds(20000);
            attempt.Start(initial, cap);
            if (waiting) { attempt.AcceptResponse(partial); }
            clock.SetMilliseconds(29999);
            Verify(attempt.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(1), "Absolute timeout starts at Start and applies while awaiting a response or continuation.");
            clock.SetMilliseconds(30000);
            Verify(attempt.ContinuationEvidence is null && attempt.GetSnapshot().State == FinTsReadRefreshState.TimedOut && Released(attempt), "Equality with the deadline expires and releases evidence even through the evidence getter.");
            attempt.Cancel(); Verify(attempt.GetSnapshot().State == FinTsReadRefreshState.TimedOut, "Cancellation cannot rewrite an expired outcome.");
        }
        var backwardsClock = new TestClock(); var backwards = New(backwardsClock); backwards.Start(initial, cap); backwardsClock.SetMilliseconds(-1);
        Verify(backwards.GetSnapshot().State == FinTsReadRefreshState.ClockInvalid && Released(backwards), "Clock regression terminates and releases raw state.");
        var duringResponse = new FinTsReadRefreshAttempt(TimeSpan.FromSeconds(10), clock: new AdvancingClock(3));
        duringResponse.Start(initial, cap);
        Verify(duringResponse.AcceptResponse(partial) == FinTsReadRefreshTransition.Terminal && duringResponse.GetSnapshot().State == FinTsReadRefreshState.TimedOut && duringResponse.GetSnapshot().PagesAccepted == 0 && Released(duringResponse), "Time spent validating a response cannot extend the absolute deadline.");
        var duringRequest = new FinTsReadRefreshAttempt(TimeSpan.FromSeconds(10), clock: new AdvancingClock(5));
        duringRequest.Start(initial, cap); duringRequest.AcceptResponse(partial);
        Verify(duringRequest.RecordContinuation(next) == FinTsReadRefreshTransition.Terminal && duringRequest.GetSnapshot().State == FinTsReadRefreshState.TimedOut && duringRequest.GetSnapshot().RequestsRecorded == 1 && Released(duringRequest), "A continuation cannot be recorded after its validation crosses the deadline.");
        var maximum = New(maximum: 128); maximum.Start(initial, cap); maximum.AcceptResponse(partial);
        for (int page = 2; page <= 128; page++)
        {
            maximum.RecordContinuation(Request(nextWire, s => Numbered(s, page + 1)));
            maximum.AcceptResponse(Data(partialWire, s => Numbered(s, page + 1)));
        }
        Verify(maximum.GetSnapshot().State == FinTsReadRefreshState.PageLimitReached && maximum.GetSnapshot().PagesAccepted == 128 && maximum.GetSnapshot().RequestsRecorded == 128 && Released(maximum), "The exact 128-page lifecycle ceiling is enforced without retaining a page chain.");
        foreach (bool stop in new[] { false, true })
        {
            var attempt = New(); attempt.Start(initial, cap); attempt.AcceptResponse(partial);
            if (stop) { attempt.Stop(); } else { attempt.Cancel(); }
            Verify(attempt.GetSnapshot().State == (stop ? FinTsReadRefreshState.Stopped : FinTsReadRefreshState.Cancelled) && Released(attempt), "Local abandonment while waiting releases source state.");
        }
        foreach (var action in new Action[] { () => new FinTsReadRefreshAttempt(TimeSpan.Zero), () => new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(16)), () => New(maximum: 0), () => New(maximum: 129), () => new FinTsReadRefreshAttempt(TimeSpan.FromSeconds(1), clock: new InvalidClock()) })
        {
            try { action(); Verify(false, "Invalid lifecycle policy must fail."); }
            catch (ArgumentException) { Verify(true, "Invalid lifecycle policy rejected."); }
        }
        var racing = New(); var starts = new FinTsReadRefreshTransition[32];
        Parallel.For(0, starts.Length, i => starts[i] = racing.Start(initial, cap));
        Verify(starts.Count(s => s == FinTsReadRefreshTransition.RequestRecorded) == 1 && racing.GetSnapshot().RequestsRecorded == 1, "Concurrent starts pin exactly one request.");
        var responsesRace = new FinTsReadRefreshTransition[32];
        Parallel.For(0, responsesRace.Length, i => responsesRace[i] = racing.AcceptResponse(partial));
        Verify(responsesRace.Count(s => s == FinTsReadRefreshTransition.PartialAccepted) == 1 && responsesRace.Count(s => s == FinTsReadRefreshTransition.ReplayRejected) == 31 && racing.GetSnapshot().PagesAccepted == 1, "Concurrent responses consume a partial page once.");
        var requestsRace = new FinTsReadRefreshTransition[32];
        Parallel.For(0, requestsRace.Length, i => requestsRace[i] = racing.RecordContinuation(next));
        Verify(requestsRace.Count(s => s == FinTsReadRefreshTransition.RequestRecorded) == 1 && racing.GetSnapshot().RequestsRecorded == 2, "Concurrent continuations record one pending request.");
        Parallel.For(0, responsesRace.Length, i => responsesRace[i] = racing.AcceptResponse(final));
        Verify(responsesRace.Count(s => s == FinTsReadRefreshTransition.ExecutionReported) == 1 && racing.GetSnapshot().PagesAccepted == 2 && Released(racing), "Concurrent final responses terminate once.");
        for (int i = 0; i < 16; i++)
        {
            var attempt = New(); attempt.Start(initial, cap);
            Parallel.Invoke(() => attempt.Cancel(), () => attempt.AcceptResponse(partial));
            Verify(attempt.GetSnapshot().State == FinTsReadRefreshState.Cancelled && attempt.GetSnapshot().PagesAccepted <= 1 && Released(attempt), "Concurrent cancellation and partial response cannot resurrect a terminal attempt.");
        }
        Verify(!racing.ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !racing.GetSnapshot().ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default lifecycle diagnostics contain no source account or token data.");
        Console.WriteLine($"FinTS read-refresh lifecycle verification passed ({count} checks).");
    }
    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Only monotonic time is allowed.");
        internal void SetMilliseconds(long milliseconds) => Interlocked.Exchange(ref _ticks, milliseconds * TimeSpan.TicksPerMillisecond);
    }
    private sealed class InvalidClock : TimeProvider { public override long TimestampFrequency => 0; }
    private sealed class AdvancingClock(int advanceOnRead) : TimeProvider
    {
        private int _reads;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Increment(ref _reads) >= advanceOnRead ? TimeSpan.FromSeconds(10).Ticks : 0;
    }
    private static bool Released(FinTsReadRefreshAttempt attempt) => typeof(FinTsReadRefreshAttempt).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => !f.IsInitOnly && !f.FieldType.IsValueType).All(f => f.GetValue(attempt) is null);
    private static FinTsReadCapabilityEvidence Capability(string wire, Func<string, string>? transform = null)
    {
        var parameters = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(wire, transform))));
        return FinTsReadCapabilityEvidence.Evaluate(parameters, parameters.Source.Accounts[0], FinTsReadOperation.Balance, 8);
    }
    private static FinTsReadRequestContext Request(string wire, Func<string, string>? transform = null)
    { var frame = Frame(wire, transform); return FinTsReadRequestContext.Parse(frame, frame.MessageNumber); }
    private static FinTsReadDataSet Data(string wire, Func<string, string>? transform = null) => FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(wire, transform)));
    private static FinTsMessageFrame Frame(string wire, Func<string, string>? transform)
    {
        string text = Encoding.Latin1.GetString(Convert.FromBase64String(wire));
        if (transform is not null) { text = transform(text); }
        int start = text.IndexOf('+') + 1;
        text = text[..start] + Encoding.Latin1.GetByteCount(text).ToString("D12", CultureInfo.InvariantCulture) + text[(start + 12)..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(text));
    }
    private static string Numbered(string wire, int number)
    {
        wire = Regex.Replace(wire, @"\+300\+SYNTHETIC\+\d+", "+300+SYNTHETIC+" + number.ToString(CultureInfo.InvariantCulture));
        wire = Regex.Replace(wire, @"\+SYNTHETIC:\d+", "+SYNTHETIC:" + number.ToString(CultureInfo.InvariantCulture));
        return Regex.Replace(wire, @"(HNHBS:\d+:1\+)\d+", m => m.Groups[1].Value + number.ToString(CultureInfo.InvariantCulture));
    }
}
