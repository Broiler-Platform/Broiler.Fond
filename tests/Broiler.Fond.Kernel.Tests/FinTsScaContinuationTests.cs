using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsScaContinuationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.sca-continuation-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        var vectors = corpus.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var initial = Initial(vector);
            var queries = Queries(vector);
            var clock = new TestClock();
            var model = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: clock);
            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.SetMilliseconds(step.GetProperty("atMilliseconds").GetInt32());
                int index = step.GetProperty("query").GetInt32();
                string action = step.GetProperty("action").GetString()!;
                string result;
                switch (action)
                {
                    case "start": result = model.Start(initial).ToString(); break;
                    case "continue": result = model.RecordUserContinuation().ToString(); break;
                    case "query": result = model.RecordStatusQuery(queries[index].Request, FinTsScaQueryTrigger.Automatic).ToString(); break;
                    case "response": result = model.AcceptStatusResponse(Status(initial, queries[index])).ToString(); break;
                    case "cancel": model.Cancel(); result = "Cancelled"; break;
                    case "stop": model.Stop(); result = "Stopped"; break;
                    case "snapshot": result = "Snapshot"; break;
                    default: throw new InvalidOperationException("Unknown fixture action.");
                }
                Verify(result == step.GetProperty("expected").GetString(), $"Independent SCA transition mismatch in {vector.GetProperty("name").GetString()}: {action} returned {result}.");
                Verify(model.GetSnapshot().State.ToString() == step.GetProperty("state").GetString(), "Independent SCA state must match after every event.");
            }
            Verify(model.CurrentChallenge is null, "Each terminal fixture must release the current challenge reference.");
            var finalState = model.GetSnapshot().State;
            model.Cancel(); model.Stop();
            Verify(model.GetSnapshot().State == finalState && model.Start(initial) == FinTsScaTransition.Terminal, "Terminal outcomes are immutable and cannot restart.");
        }

        var decoupled = Initial(vectors[1]);
        var queryPairs = Queries(vectors[1]);
        var app = Initial(vectors[0]);
        foreach (var action in new Action[]
        {
            () => new FinTsScaContinuation(TimeSpan.Zero), () => new FinTsScaContinuation(TimeSpan.FromMinutes(16)),
            () => new FinTsScaContinuation(TimeSpan.FromSeconds(1), -1), () => new FinTsScaContinuation(TimeSpan.FromSeconds(1), 129),
        })
        {
            try { action(); Verify(false, "Invalid local SCA bounds must fail."); }
            catch (ArgumentOutOfRangeException) { Verify(true, "Local SCA bounds are enforced."); }
        }
        var ready = new FinTsScaContinuation(TimeSpan.FromSeconds(10), clock: new TestClock());
        Verify(ready.GetSnapshot().State == FinTsScaState.Ready && ready.RecordUserContinuation() == FinTsScaTransition.WrongState &&
            ready.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Manual) == FinTsScaTransition.WrongState && ready.AcceptStatusResponse(decoupled) == FinTsScaTransition.WrongState,
            "An empty model cannot record or consume continuation work.");
        ready.Cancel();
        Verify(ready.Start(app) == FinTsScaTransition.Terminal, "Cancellation before start is terminal.");
        var stopped = new FinTsScaContinuation(TimeSpan.FromSeconds(10)); stopped.Stop();
        Verify(stopped.Start(app) == FinTsScaTransition.Terminal, "Stop before start is terminal.");
        var bad = Initial(vectors[1], responseTransform: s => s.Replace("+0030::", "+9000::", StringComparison.Ordinal));
        var rejected = new FinTsScaContinuation(TimeSpan.FromSeconds(10));
        Verify(rejected.Start(bad) == FinTsScaTransition.RejectedForReview && rejected.GetSnapshot().State == FinTsScaState.NeedsReview && rejected.CurrentChallenge is null,
            "Unresolved evidence stops the attempt without retaining a challenge.");
        var statusOnly = Status(decoupled, queryPairs[0]);
        Verify(new FinTsScaContinuation(TimeSpan.FromSeconds(10)).Start(statusOnly) == FinTsScaTransition.RejectedForReview, "A status response cannot create a new initial challenge.");

        foreach (bool bankZero in new[] { false, true })
        {
            var initial = bankZero ? Initial(vectors[1], responseTransform: s => s.Replace("10:2:5:N:J", "0:2:5:N:J", StringComparison.Ordinal)) : decoupled;
            var limited = new FinTsScaContinuation(TimeSpan.FromSeconds(10), maximumQueries: bankZero ? 128 : 0, clock: new TestClock());
            Verify(limited.Start(initial) == FinTsScaTransition.Terminal && limited.GetSnapshot().State == FinTsScaState.QueryLimitReached, "Either an explicit bank zero or local zero prohibits status queries.");
        }
        var limitClock = new TestClock();
        var limitModel = new FinTsScaContinuation(TimeSpan.FromSeconds(10), maximumQueries: 1, allowAutomaticQueries: true, clock: limitClock);
        limitModel.Start(decoupled); limitClock.SetMilliseconds(2000);
        Verify(limitModel.GetSnapshot().MaximumQueries == 1 && limitModel.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic) == FinTsScaTransition.QueryRecorded,
            "The effective query limit is the smaller local/advertised bound.");
        Verify(limitModel.AcceptStatusResponse(statusOnly) == FinTsScaTransition.Terminal && limitModel.GetSnapshot().State == FinTsScaState.QueryLimitReached, "A final permitted pending response terminates at the query limit.");

        var permissionClock = new TestClock();
        var permissions = new FinTsScaContinuation(TimeSpan.FromSeconds(10), clock: permissionClock);
        permissions.Start(decoupled); permissionClock.SetMilliseconds(2000);
        Verify(permissions.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic) == FinTsScaTransition.TriggerNotAllowed &&
            permissions.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Manual) == FinTsScaTransition.TriggerNotAllowed && permissions.GetSnapshot().QueriesRecorded == 0,
            "Automatic recording needs local opt-in; manual recording cannot override an explicit bank restriction.");
        foreach (string flags in new[] { "N:N", "J:J", "N:", ":" })
        {
            var initial = Initial(vectors[1], responseTransform: s => s.Replace("10:2:5:N:J", "10:2:5:" + flags, StringComparison.Ordinal));
            var clock = new TestClock();
            var model = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: clock);
            model.Start(initial); clock.SetMilliseconds(2000);
            bool manualAllowed = flags is "N:N" or "J:J";
            Verify(model.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Manual) == (manualAllowed ? FinTsScaTransition.QueryRecorded : FinTsScaTransition.TriggerNotAllowed),
                "Manual trigger rules preserve automated-only, manual-only and unknown permission distinctions.");
        }

        var clock2 = new TestClock();
        var bound = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: clock2);
        bound.Start(decoupled);
        Verify(bound.CurrentChallenge == decoupled.Challenge && bound.Start(app) == FinTsScaTransition.WrongState && bound.CurrentChallenge == decoupled.Challenge,
            "A second start cannot replace an active challenge.");
        clock2.SetMilliseconds(2000);
        string requestText = Encoding.Latin1.GetString(queryPairs[0].Request.Frame.Syntax.CopyWireBytes());
        foreach (string wire in new[]
        {
            requestText.Replace("PUBLIC-ORDER", "OTHER-ORDER", StringComparison.Ordinal), requestText.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal),
            requestText.Replace("+S++++", "+S+HKSAL+++", StringComparison.Ordinal), Numbered(requestText, 4), Numbered(requestText, 1),
            requestText.Replace("HNHBS:3:1", "ZTEST:3:1'HNHBS:4:1", StringComparison.Ordinal),
        })
        {
            var request = FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(Resize(wire)), 2,
                wire == Numbered(requestText, 1) ? 1 : wire == Numbered(requestText, 4) ? 4 : 2);
            var result = bound.RecordStatusQuery(request, FinTsScaQueryTrigger.Automatic);
            Verify(result is FinTsScaTransition.ContextMismatch or FinTsScaTransition.ReplayRejected && bound.GetSnapshot().QueriesRecorded == 0,
                "Wrong order, dialogue, operation, counters or extra request body must not reserve a query.");
        }
        Verify(bound.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic) == FinTsScaTransition.QueryRecorded, "A matching query can follow rejected candidates.");
        var clonedQuery = Queries(vectors[1])[0];
        Verify(bound.AcceptStatusResponse(Status(decoupled, clonedQuery)) == FinTsScaTransition.ContextMismatch && bound.GetSnapshot().State == FinTsScaState.AwaitingQueryResponse,
            "A newly parsed request instance cannot stand in for the reserved request.");
        Verify(bound.AcceptStatusResponse(Status(Initial(vectors[1]), queryPairs[0])) == FinTsScaTransition.ContextMismatch, "Procedure and permission evidence remain frozen to the initial instances.");
        Verify(bound.AcceptStatusResponse(statusOnly) == FinTsScaTransition.PendingAccepted && bound.GetSnapshot().QueryWait == TimeSpan.FromSeconds(5), "A bound pending response sets the next conservative wait from response receipt.");
        Verify(bound.AcceptStatusResponse(statusOnly) == FinTsScaTransition.ReplayRejected && bound.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic) == FinTsScaTransition.ReplayRejected,
            "Consumed message counters reject both request and response replay while the attempt remains active.");
        clock2.SetMilliseconds(7000); bound.RecordStatusQuery(queryPairs[1].Request, FinTsScaQueryTrigger.Automatic);
        var secondPending = Status(decoupled, queryPairs[1], s => s.Replace("0020", "3956", StringComparison.Ordinal));
        Verify(bound.AcceptStatusResponse(secondPending) == FinTsScaTransition.PendingAccepted && bound.GetSnapshot().RemainingTime == TimeSpan.FromSeconds(3), "Polling must not extend the absolute attempt deadline.");
        clock2.SetMilliseconds(10000);
        Verify(bound.GetSnapshot().State == FinTsScaState.TimedOut && bound.CurrentChallenge is null, "The absolute deadline expires even during a subsequent wait.");

        var regressionClock = new TestClock();
        var regression = new FinTsScaContinuation(TimeSpan.FromSeconds(10), clock: regressionClock);
        regression.Start(app); regressionClock.SetMilliseconds(5); _ = regression.GetSnapshot(); regressionClock.SetMilliseconds(4);
        Verify(regression.RecordUserContinuation() == FinTsScaTransition.Terminal && regression.GetSnapshot().State == FinTsScaState.ClockInvalid, "Regressing monotonic timestamps fail terminally.");
        var inflightClock = new TestClock();
        var inflight = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: inflightClock);
        inflight.Start(decoupled); inflightClock.SetMilliseconds(2000); inflight.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic);
        inflightClock.SetMilliseconds(10000);
        Verify(inflight.AcceptStatusResponse(statusOnly) == FinTsScaTransition.Terminal && inflight.GetSnapshot().State == FinTsScaState.TimedOut, "A response at the deadline cannot revive an in-flight attempt.");

        var exhausted = Initial(vectors[1], s => Numbered(s, 9999), s => Numbered(s, 9999), 9999);
        var exhaustedModel = new FinTsScaContinuation(TimeSpan.FromSeconds(10));
        Verify(exhaustedModel.Start(exhausted) == FinTsScaTransition.Terminal && exhaustedModel.GetSnapshot().State == FinTsScaState.CounterExhausted, "Initial exhausted counters cannot wrap to one.");
        var nearEnd = Initial(vectors[1], s => Numbered(s, 9998), s => Numbered(s, 9998), 9998);
        var finalQuery = (Request: FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(Resize(Numbered(requestText, 9999))), 2, 9999), Response: Resize(Numbered(Encoding.Latin1.GetString(queryPairs[0].Response), 9999)));
        var endClock = new TestClock();
        var endModel = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: endClock);
        endModel.Start(nearEnd); endClock.SetMilliseconds(2000); endModel.RecordStatusQuery(finalQuery.Request, FinTsScaQueryTrigger.Automatic);
        Verify(endModel.AcceptStatusResponse(Status(nearEnd, finalQuery)) == FinTsScaTransition.Terminal && endModel.GetSnapshot().State == FinTsScaState.CounterExhausted, "A pending response at the last message counter terminates without overflow.");

        var execution = Status(decoupled, queryPairs[1]);
        Verify(execution.Issues == FinTsTanContextIssue.None && execution.Observation == FinTsTanOutcomeObservation.ExecutionReported, "Scoped process-S execution is an observation, not proof of SCA success.");
        foreach (string status in new[] { "0020:1:State", "0020::State+3956::Still waiting", "0010::State" })
        {
            var result = Status(decoupled, queryPairs[1], s => s.Replace("0020::State", status, StringComparison.Ordinal));
            Verify(result.Observation == FinTsTanOutcomeObservation.NeedsReview, "Element-level, conflicting or receipt-only reports cannot establish the terminal execution observation.");
        }

        var concurrent = new FinTsScaContinuation(TimeSpan.FromSeconds(10), clock: new TestClock());
        var starts = new FinTsScaTransition[32];
        Parallel.For(0, starts.Length, i => starts[i] = concurrent.Start(app));
        Verify(starts.Count(r => r == FinTsScaTransition.Started) == 1, "Concurrent starts establish exactly one challenge.");
        var continuations = new FinTsScaTransition[32];
        Parallel.For(0, continuations.Length, i => continuations[i] = concurrent.RecordUserContinuation());
        Verify(continuations.Count(r => r == FinTsScaTransition.UserContinuationRecorded) == 1 && concurrent.CurrentChallenge is null, "Concurrent user continuations consume local intent once.");
        var raceClock = new TestClock();
        var racing = new FinTsScaContinuation(TimeSpan.FromSeconds(10), allowAutomaticQueries: true, clock: raceClock);
        racing.Start(decoupled); raceClock.SetMilliseconds(2000);
        var records = new FinTsScaTransition[32];
        Parallel.For(0, records.Length, i => records[i] = racing.RecordStatusQuery(queryPairs[0].Request, FinTsScaQueryTrigger.Automatic));
        Verify(records.Count(r => r == FinTsScaTransition.QueryRecorded) == 1 && racing.GetSnapshot().QueriesRecorded == 1, "Concurrent queries reserve one slot and consume one query budget.");
        var responses = new FinTsScaTransition[32];
        Parallel.For(0, responses.Length, i => responses[i] = racing.AcceptStatusResponse(statusOnly));
        Verify(responses.Count(r => r == FinTsScaTransition.PendingAccepted) == 1 && responses.Count(r => r == FinTsScaTransition.ReplayRejected) == 31, "Concurrent identical responses advance once and reject replay.");
        Verify(new object[] { racing, racing.GetSnapshot() }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default model diagnostics must exclude source bank data.");
        Console.WriteLine($"FinTS in-memory SCA continuation: {vectors.Length} independent traces and {count} checks completed.");
    }

    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("The continuation model must not use wall-clock time.");
        internal void SetMilliseconds(long value) => Interlocked.Exchange(ref _ticks, value * TimeSpan.TicksPerMillisecond);
    }
    private static FinTsTanChallengeEvidence Initial(JsonElement vector, Func<string, string>? requestTransform = null, Func<string, string>? responseTransform = null, int expected = 1)
    {
        byte[] requestBytes = Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!);
        byte[] responseBytes = Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!);
        if (requestTransform is not null) { requestBytes = Resize(requestTransform(Encoding.Latin1.GetString(requestBytes))); }
        if (responseTransform is not null) { responseBytes = Resize(responseTransform(Encoding.Latin1.GetString(responseBytes))); }
        var request = FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(requestBytes), 2, expected);
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(responseBytes));
        var parameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response));
        return FinTsTanChallengeEvidence.Evaluate(request, parameters, parameters.Advertisements[0].Procedures[0], FinTsPermittedProcedureSet.Parse(response), FinTsTanChallengeSet.Parse(response));
    }
    private static (FinTsTanRequestContext Request, byte[] Response)[] Queries(JsonElement vector) => vector.GetProperty("queries").EnumerateArray().Select((query, index) =>
        (FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(query.GetProperty("requestBase64").GetString()!)), 2, index + 2),
        Convert.FromBase64String(query.GetProperty("responseBase64").GetString()!))).ToArray();
    private static FinTsTanChallengeEvidence Status(FinTsTanChallengeEvidence initial, (FinTsTanRequestContext Request, byte[] Response) query, Func<string, string>? transform = null)
    {
        byte[] responseBytes = transform is null ? query.Response : Resize(transform(Encoding.Latin1.GetString(query.Response)));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(responseBytes));
        return FinTsTanChallengeEvidence.Evaluate(query.Request, initial.Parameters, initial.Procedure, initial.Permissions, FinTsTanChallengeSet.Parse(response));
    }
    private static byte[] Resize(string wire)
    {
        int sizeStart = wire.IndexOf('+') + 1;
        return Encoding.Latin1.GetBytes(wire[..sizeStart] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[(sizeStart + 12)..]);
    }
    private static string Numbered(string wire, int number)
    {
        wire = Regex.Replace(wire, @"\+300\+SYNTHETIC\+\d+", "+300+SYNTHETIC+" + number.ToString(CultureInfo.InvariantCulture));
        wire = Regex.Replace(wire, @"\+SYNTHETIC:\d+", "+SYNTHETIC:" + number.ToString(CultureInfo.InvariantCulture));
        return Regex.Replace(wire, @"(HNHBS:\d+:1\+)\d+", m => m.Groups[1].Value + number.ToString(CultureInfo.InvariantCulture));
    }
}
