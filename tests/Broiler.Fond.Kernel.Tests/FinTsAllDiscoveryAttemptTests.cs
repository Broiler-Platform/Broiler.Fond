using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsAllDiscoveryAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.all-discovery-attempt-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var traceClock = new Clock(); var attempt = New(traceClock);
            var request = Request(vector); var parameters = Parameters(vector);
            var responses = vector.GetProperty("responses").EnumerateArray().Select(r => Data(r.GetString()!)).ToArray();
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                traceClock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string result; bool evidence = false;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": result = attempt.Start(request, parameters).ToString(); break;
                    case "response":
                        var received = attempt.AcceptResponse(responses[step.GetProperty("index").GetInt32()]);
                        result = received.Transition.ToString(); evidence = received.Evidence is not null; break;
                    case "cancel": attempt.Cancel(); result = "Cancelled"; break;
                    case "stop": attempt.Stop(); result = "Stopped"; break;
                    case "snapshot": _ = attempt.GetSnapshot(); result = "Snapshot"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                Verify(result == step.GetProperty("expected").GetString(), $"Independent all-discovery transition differs in {vector.GetProperty("name").GetString()}: {result}.");
                Verify(attempt.GetSnapshot().State.ToString() == step.GetProperty("state").GetString() && evidence == step.GetProperty("evidence").GetBoolean(), "Independent state and one-time evidence handoff match.");
            }
            Verify(Released(attempt), "All terminal traces release internally retained request and parameter references.");
        }
        Verify(vectors.Length == 8, "All eight all-discovery attempt traces ran.");
        var sample = vectors[0]; var requestBase = Request(sample); var parametersBase = Parameters(sample);
        string responseWire = sample.GetProperty("responses")[0].GetString()!;
        var responseBase = Data(responseWire);
        var ready = New();
        Verify(ready.GetSnapshot().State == FinTsAllDiscoveryState.Ready && ready.GetSnapshot().RemainingTime == TimeSpan.Zero && ready.GetSnapshot().RequestsRecorded == 0, "No request or deadline exists before start.");
        Verify(ready.AcceptResponse(responseBase).Transition == FinTsAllDiscoveryTransition.WrongState, "Responses cannot initialize discovery attempts.");
        Verify(ready.Start(requestBase, parametersBase) == FinTsAllDiscoveryTransition.RequestRecorded && ready.GetSnapshot().RequestsRecorded == 1, "Start records one fixed request.");
        Verify(ready.Start(Request(sample), Parameters(sample)) == FinTsAllDiscoveryTransition.WrongState, "Equivalent new objects cannot replace the pinned pending context.");
        var accepted = ready.AcceptResponse(responseBase);
        Verify(accepted.Transition == FinTsAllDiscoveryTransition.ExecutionReported && accepted.Evidence is { HasMatchingEvidence: true } && ReferenceEquals(accepted.Evidence.Request, requestBase) && ReferenceEquals(accepted.Evidence.Parameters, parametersBase) && ReferenceEquals(accepted.Evidence.Response, responseBase), "The internally recomputed comparison retains exactly the original scope.");
        Verify(ready.GetSnapshot().ResponsesAccepted == 1 && Released(ready) && accepted.Evidence!.Accounts.Count == 2, "Caller-owned evidence survives the attempt releasing its own references.");
        var replay = ready.AcceptResponse(Data(responseWire));
        Verify(replay.Transition == FinTsAllDiscoveryTransition.Terminal && replay.Evidence is null, "Reparsed bytes cannot obtain a second evidence handoff.");
        ready.Cancel(); ready.Stop();
        Verify(ready.GetSnapshot().State == FinTsAllDiscoveryState.ExecutionReported && ready.Start(requestBase, parametersBase) == FinTsAllDiscoveryTransition.Terminal, "Terminal execution cannot be overwritten or restarted.");
        var unknown = vectors[2]; var review = New(); review.Start(Request(unknown), Parameters(unknown));
        var reviewed = review.AcceptResponse(Data(unknown.GetProperty("responses")[0].GetString()!));
        Verify(reviewed.Transition == FinTsAllDiscoveryTransition.RejectedForReview && reviewed.Evidence!.Accounts[0].Candidates.Count == 2 && reviewed.Evidence.Accounts[0].MatchedAccount is null && review.GetSnapshot().ResponsesAccepted == 0 && Released(review), "Ambiguity remains available to the caller for review without counting a clean response.");
        foreach (var wrong in new[]
        {
            Data(responseWire, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal)),
            Data(responseWire, s => s.Replace("HISPA:4:1:2", "HISPA:4:1:1", StringComparison.Ordinal)),
            Data(responseWire, s => Numbered(s, 3)),
        })
        {
            var attempt = New(); attempt.Start(requestBase, parametersBase);
            var rejected = attempt.AcceptResponse(wrong);
            Verify(rejected.Transition == FinTsAllDiscoveryTransition.ContextMismatch && rejected.Evidence is null && attempt.GetSnapshot().State == FinTsAllDiscoveryState.AwaitingResponse && attempt.GetSnapshot().ResponsesAccepted == 0, "Foreign message/reference does not consume or hand off the pending response.");
            Verify(attempt.AcceptResponse(responseBase).Transition == FinTsAllDiscoveryTransition.ExecutionReported && attempt.GetSnapshot().ResponseIssues == FinTsReadContextIssue.None, "A valid response can complete after foreign candidates; transient issues are cleared.");
        }
        foreach (var request in new[]
        {
            Request(sample, s => s.Replace("HKSPA:2:1'", "HKSPA:2:1+PUBLIC-001:00:280:PUBLIC-BANK'", StringComparison.Ordinal)),
            Request(sample, s => s.Replace("HKSPA:2:1'", "HKSAL:2:8+PUBLIC-IBAN:PUBLIC-BIC+J'", StringComparison.Ordinal)),
        })
        {
            var attempt = New();
            Verify(attempt.Start(request, parametersBase) == FinTsAllDiscoveryTransition.RejectedForReview && attempt.GetSnapshot().Issues.HasFlag(FinTsAllDiscoveryIssue.UnsupportedRequest) && attempt.GetSnapshot().RequestsRecorded == 0 && Released(attempt), "Unsupported initial request fails before retention or recording.");
        }
        foreach (var parameters in new[] { Parameters(sample, s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal)), Parameters(sample, s => s.Replace("HISPAS:7:1+1+1+0", "HISPAS:7:1+0+1+0", StringComparison.Ordinal)) })
        {
            var attempt = New();
            Verify(attempt.Start(requestBase, parameters) == FinTsAllDiscoveryTransition.RejectedForReview && Released(attempt), "Foreign or unusable parameters cannot be pinned.");
        }
        var pinned = New(); pinned.Start(requestBase, parametersBase);
        var changedParametersResponse = Data(responseWire, s => s.Replace("HNHBS:5:1+2'", "HISPAS:5:1+1+1+0+J:J:N'HNHBS:6:1+2'", StringComparison.Ordinal));
        Verify(pinned.AcceptResponse(changedParametersResponse).Transition == FinTsAllDiscoveryTransition.RejectedForReview && pinned.GetSnapshot().ResponseIssues.HasFlag(FinTsReadContextIssue.UnexpectedReport) && Released(pinned), "An incoming advertisement cannot replace the selected parameters.");
        var hugeParameters = ManyParameters(); var huge = ManyResponse(9); var budget = New(); budget.Start(requestBase, hugeParameters);
        budget.AcceptResponse(ManyResponse(9, reference: 1));
        var budgetResult = budget.AcceptResponse(huge);
        Verify(budgetResult.Transition == FinTsAllDiscoveryTransition.RejectedForReview && budgetResult.Evidence is null && budget.GetSnapshot().Failure == FinTsSyntaxError.LimitExceeded && budget.GetSnapshot().ResponseIssues == FinTsReadContextIssue.None && budget.GetSnapshot().ResponsesAccepted == 0 && Released(budget), "Candidate-budget failure is terminal with current fixed diagnostics and no partial evidence.");
        var foreignBudget = New(); foreignBudget.Start(requestBase, hugeParameters);
        var foreignHuge = ManyResponse(9, reference: 1);
        Verify(foreignBudget.AcceptResponse(foreignHuge).Transition == FinTsAllDiscoveryTransition.ContextMismatch && foreignBudget.GetSnapshot().Failure is null && foreignBudget.GetSnapshot().State == FinTsAllDiscoveryState.AwaitingResponse, "A foreign reference is rejected before candidate expansion can exhaust the pending request's budget.");
        var boundedReview = foreignBudget.AcceptResponse(ManyResponse(8));
        Verify(boundedReview.Transition == FinTsAllDiscoveryTransition.RejectedForReview && boundedReview.Evidence!.Accounts.Sum(a => a.Candidates.Count) == 4096 && Released(foreignBudget), "The exact candidate budget can be handed out once as a review result.");
        var clock = new Clock { Milliseconds = 20000 }; var timed = New(clock); timed.Start(requestBase, parametersBase); clock.Milliseconds = 29999;
        Verify(timed.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(1), "Deadline starts at request recording, not constructor creation.");
        clock.Milliseconds = 30000;
        var late = timed.AcceptResponse(responseBase);
        Verify(late.Transition == FinTsAllDiscoveryTransition.Terminal && late.Evidence is null && timed.GetSnapshot().State == FinTsAllDiscoveryState.TimedOut && Released(timed), "Equality with the deadline prevents evidence handoff.");
        foreach (int read in new[] { 3, 4 })
        {
            var attempt = New(new AdvancingClock(read)); attempt.Start(requestBase, parametersBase);
            var result = attempt.AcceptResponse(responseBase);
            Verify(result.Transition == FinTsAllDiscoveryTransition.Terminal && result.Evidence is null && attempt.GetSnapshot().State == FinTsAllDiscoveryState.TimedOut && attempt.GetSnapshot().ResponsesAccepted == 0 && Released(attempt), "Expiry during preflight or account matching blocks terminal evidence handoff.");
        }
        var regressionClock = new Clock(); var regression = New(regressionClock); regression.Start(requestBase, parametersBase); regressionClock.Milliseconds = -1;
        Verify(regression.GetSnapshot().State == FinTsAllDiscoveryState.ClockInvalid && Released(regression), "Monotonic clock regression terminates and releases context.");
        foreach (bool stop in new[] { false, true })
        {
            var attempt = New(); attempt.Start(requestBase, parametersBase);
            if (stop) { attempt.Stop(); } else { attempt.Cancel(); }
            var result = attempt.AcceptResponse(responseBase);
            Verify(result.Transition == FinTsAllDiscoveryTransition.Terminal && result.Evidence is null && attempt.GetSnapshot().State == (stop ? FinTsAllDiscoveryState.Stopped : FinTsAllDiscoveryState.Cancelled) && Released(attempt), "Cancelled/stopped attempts cannot hand off a later response.");
        }
        var last = New(); last.Start(Request(sample, s => Numbered(s, 9999), 9999), parametersBase);
        Verify(last.AcceptResponse(Data(responseWire, s => Numbered(s, 9999))).Transition == FinTsAllDiscoveryTransition.ExecutionReported, "The final representable message counters need no wrap for one-page discovery.");
        foreach (Action action in new Action[] { () => new FinTsAllAccountDiscoveryAttempt(TimeSpan.Zero), () => new FinTsAllAccountDiscoveryAttempt(TimeSpan.FromMinutes(16)), () => New(new InvalidClock()) })
        {
            try { action(); Verify(false, "Invalid attempt policy must fail."); }
            catch (ArgumentException) { Verify(true, "Invalid lifetime/clock rejected."); }
        }
        var racing = New(); var starts = new FinTsAllDiscoveryTransition[32];
        Parallel.For(0, starts.Length, i => starts[i] = racing.Start(requestBase, parametersBase));
        Verify(starts.Count(s => s == FinTsAllDiscoveryTransition.RequestRecorded) == 1 && racing.GetSnapshot().RequestsRecorded == 1, "Concurrent starts retain exactly one request.");
        var outcomes = new FinTsAllDiscoveryAttemptResult[32];
        Parallel.For(0, outcomes.Length, i => outcomes[i] = racing.AcceptResponse(responseBase));
        Verify(outcomes.Count(r => r.Transition == FinTsAllDiscoveryTransition.ExecutionReported) == 1 && outcomes.Count(r => r.Evidence is not null) == 1 && racing.GetSnapshot().ResponsesAccepted == 1 && Released(racing), "Concurrent responses perform one terminal handoff.");
        for (int i = 0; i < 16; i++)
        {
            var attempt = New(); attempt.Start(requestBase, parametersBase); FinTsAllDiscoveryAttemptResult? result = null;
            Parallel.Invoke(() => attempt.Cancel(), () => result = attempt.AcceptResponse(responseBase));
            Verify(attempt.GetSnapshot().State is FinTsAllDiscoveryState.Cancelled or FinTsAllDiscoveryState.ExecutionReported &&
                (result!.Evidence is not null) == (attempt.GetSnapshot().State == FinTsAllDiscoveryState.ExecutionReported) && Released(attempt), "Cancellation/response races yield one immutable outcome with evidence only if the response wins.");
        }
        Verify(new object[] { accepted, accepted.Evidence!, racing, racing.GetSnapshot() }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default attempt/result diagnostics exclude source identifiers and candidate data.");
        Console.WriteLine($"FinTS all-account discovery attempt verification passed ({count} checks).");
    }
    private static FinTsAllAccountDiscoveryAttempt New(TimeProvider? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
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
    private static bool Released(FinTsAllAccountDiscoveryAttempt attempt) => typeof(FinTsAllAccountDiscoveryAttempt).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => !f.IsInitOnly && !f.FieldType.IsValueType).All(f => f.GetValue(attempt) is null);
    private static FinTsReadRequestContext Request(JsonElement vector, Func<string, string>? transform = null, int expected = 2) => FinTsReadRequestContext.Parse(Frame(vector.GetProperty("requestBase64").GetString()!, transform), expected);
    private static FinTsReadParameterSet Parameters(JsonElement vector, Func<string, string>? transform = null) => FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(vector.GetProperty("parametersBase64").GetString()!, transform))));
    private static FinTsReadDataSet Data(string wire, Func<string, string>? transform = null) => FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(wire, transform)));
    private static FinTsMessageFrame Frame(string base64, Func<string, string>? transform = null)
    { string wire = Encoding.Latin1.GetString(Convert.FromBase64String(base64)); return ParseWire(transform is null ? wire : transform(wire)); }
    private static FinTsReadParameterSet ManyParameters()
    {
        var body = new List<string> { "HIRMG:2+0010::Synthetic", "HIBPA:3+1+280:PUBLIC-BANK+Bank+1+1+300", "HIUPA:4+PUBLIC-USER+1+1" };
        body.AddRange(Enumerable.Repeat("HIUPD:6+PUBLIC-001:00:280:PUBLIC-BANK+PUBLIC-IBAN+C+1+EUR+Owner++++HKSPA:1", 512));
        body.Add("HISPAS:1+1+1+0+N:J:N");
        return FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Build(body, 1))));
    }
    private static FinTsReadDataSet ManyResponse(int count, int reference = 2) => FinTsReadDataSet.Parse(FinTsResponse.Parse(Build([
        "HIRMG:2+0010::Synthetic", "HIRMS:2:2+0020::Synthetic", $"HISPA:1:{reference}+" + string.Join('+', Enumerable.Repeat("J:PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-001:00:280:PUBLIC-BANK", count))], 2)));
    private static FinTsMessageFrame Build(List<string> body, int number)
    {
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{number}+SYNTHETIC:{number}'";
        wire += string.Concat(body.Select((part, i) => { int colon = part.IndexOf(':'); return part[..colon] + ":" + (i + 2).ToString(CultureInfo.InvariantCulture) + part[colon..] + "'"; }));
        return ParseWire(wire + $"HNHBS:{body.Count + 2}:1+{number}'");
    }
    private static FinTsMessageFrame ParseWire(string wire)
    {
        int start = wire.IndexOf('+') + 1;
        wire = wire[..start] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[(start + 12)..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
    private static string Numbered(string wire, int number)
    {
        wire = Regex.Replace(wire, @"\+300\+SYNTHETIC\+\d+", "+300+SYNTHETIC+" + number.ToString(CultureInfo.InvariantCulture));
        wire = Regex.Replace(wire, @"\+SYNTHETIC:\d+", "+SYNTHETIC:" + number.ToString(CultureInfo.InvariantCulture));
        return Regex.Replace(wire, @"(HNHBS:\d+:1\+)\d+", m => m.Groups[1].Value + number.ToString(CultureInfo.InvariantCulture));
    }
}
