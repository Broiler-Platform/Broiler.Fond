using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSynchronizationAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.synchronization-attempt-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            var clock = new Clock(); var attempt = New(clock);
            var request = Request(v); var closing = CloseRequest(v);
            var profile = Enum.Parse<FinTsSynchronizationProfile>(v.GetProperty("profile").GetString()!);
            var recovery = Recovery(v);
            var syncResponses = v.GetProperty("synchronizationResponses").EnumerateArray().Select(e => Data(e.GetString()!)).ToArray();
            var closeResponses = v.GetProperty("closingResponses").EnumerateArray().Select(e => Response(e.GetString()!)).ToArray();
            foreach (var step in v.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                FinTsSynchronizationAttemptResult? result = null; string transition;
                switch (step.GetProperty("action").GetString())
                {
                    case "start": transition = attempt.Start(request, profile, recovery).ToString(); break;
                    case "synchronization": result = attempt.AcceptSynchronization(syncResponses[step.GetProperty("index").GetInt32()]); transition = result.Transition.ToString(); break;
                    case "recordClose": transition = attempt.RecordClose(closing).ToString(); break;
                    case "close": result = attempt.AcceptClose(closeResponses[step.GetProperty("index").GetInt32()]); transition = result.Transition.ToString(); break;
                    case "cancel": attempt.Cancel(); transition = "Cancelled"; break;
                    case "stop": attempt.Stop(); transition = "Stopped"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                var snapshot = attempt.GetSnapshot();
                Verify(transition == step.GetProperty("expected").GetString() && snapshot.State.ToString() == step.GetProperty("state").GetString(), $"Independent synchronization transition/state match {v.GetProperty("name").GetString()}: {transition}/{snapshot.State}.");
                Verify((result?.SynchronizationEvidence is not null) == step.GetProperty("synchronizationEvidence").GetBoolean() &&
                    (result?.ClosingEvidence is not null) == step.GetProperty("closingEvidence").GetBoolean(), "Independent one-time evidence handoff expectations match.");
                Verify(snapshot.RequestsRecorded == step.GetProperty("requests").GetInt32() && snapshot.ResponsesAccepted == step.GetProperty("accepted").GetInt32(), "Independent request and accepted-response counts match.");
            }
            Verify(Released(attempt), "Every terminal trace releases retained private context.");
        }
        Verify(vectors.Length == 12, "All twelve synchronization-and-closing traces ran.");
        var sample = vectors[0]; var requestBase = Request(sample); var closeBase = CloseRequest(sample);
        string syncBytes = sample.GetProperty("synchronizationResponses")[0].GetString()!;
        string closeBytes = sample.GetProperty("closingResponses")[0].GetString()!;
        var syncBase = Data(syncBytes); var closeResponse = Response(closeBytes);
        const FinTsSynchronizationProfile Profile = FinTsSynchronizationProfile.PinTan2;
        FinTsSynchronizationAttempt Stage(int stage, TimeProvider? clock = null)
        {
            var a = New(clock);
            if (stage >= 1) { a.Start(requestBase, Profile); }
            if (stage >= 2) { a.AcceptSynchronization(syncBase); }
            if (stage >= 3) { a.RecordClose(closeBase); }
            return a;
        }
        var ready = Stage(0);
        Verify(ready.GetSnapshot().RemainingTime == TimeSpan.Zero && Released(ready), "Construction retains no request and starts no deadline.");
        Verify(ready.AcceptSynchronization(syncBase).Transition == FinTsSynchronizationTransition.WrongState && ready.RecordClose(closeBase) == FinTsSynchronizationTransition.WrongState && ready.AcceptClose(closeResponse).Transition == FinTsSynchronizationTransition.WrongState, "Responses and closing cannot start an attempt.");
        ready.Start(requestBase, Profile);
        Verify(ready.Start(Request(sample), FinTsSynchronizationProfile.Rah10) == FinTsSynchronizationTransition.WrongState && ready.RecordClose(closeBase) == FinTsSynchronizationTransition.WrongState && ready.AcceptClose(closeResponse).Transition == FinTsSynchronizationTransition.WrongState, "Pending synchronization cannot be replaced or skipped.");
        var handed = ready.AcceptSynchronization(syncBase);
        Verify(handed.Transition == FinTsSynchronizationTransition.CloseRequired && ReferenceEquals(handed.SynchronizationEvidence!.Request, requestBase) && ReferenceEquals(handed.SynchronizationEvidence.Response, syncBase) && handed.SynchronizationEvidence.Profile == Profile, "Synchronization comparison uses exact pinned request and profile, handing evidence to the caller.");
        Verify(Retained(ready).Single() is string && ready.AcceptSynchronization(Data(syncBytes)).SynchronizationEvidence is null && ready.AcceptClose(closeResponse).Transition == FinTsSynchronizationTransition.WrongState, "Only the reported dialogue remains between stages; synchronization replay has no handoff and close cannot be skipped.");
        ready.RecordClose(closeBase);
        Verify(ReferenceEquals(Retained(ready).Single(), closeBase) && ready.RecordClose(CloseRequest(sample)) == FinTsSynchronizationTransition.WrongState, "Only one exact close request remains pending and cannot be replaced.");
        var completed = ready.AcceptClose(closeResponse);
        Verify(completed.Transition == FinTsSynchronizationTransition.ReinitializationRequired && ReferenceEquals(completed.ClosingEvidence!.Request, closeBase) && ReferenceEquals(completed.ClosingEvidence.Response, closeResponse) && Released(ready), "Matching close returns exact evidence and clears retained references.");
        Verify(handed.SynchronizationEvidence!.MatchingReport!.SystemId is not null && completed.ClosingEvidence!.HasMatchingEvidence, "Caller-owned evidence survives cleanup without applying recovered values.");
        ready.Cancel(); ready.Stop();
        Verify(ready.GetSnapshot().State == FinTsSynchronizationState.ReinitializationRequired && ready.GetSnapshot().ResponsesAccepted == 2 && ready.GetSnapshot().RemainingTime == TimeSpan.Zero && ready.Start(requestBase, Profile) == FinTsSynchronizationTransition.Terminal && ready.AcceptSynchronization(syncBase).SynchronizationEvidence is null && ready.AcceptClose(Response(closeBytes)).ClosingEvidence is null, "Completion is immutable and both reparsed responses remain consumed within this instance.");
        foreach (var v in new[] { vectors[10], vectors[2] })
        {
            var a = New();
            var transition = a.Start(Request(v), v.GetProperty("name").GetString() == "missing-recovery-before-recording" ? Profile : FinTsSynchronizationProfile.Unspecified);
            Verify(transition == FinTsSynchronizationTransition.RejectedForReview && a.GetSnapshot().RequestsRecorded == 0 && Released(a), "Missing recovery or unknown profile stops before recording a request.");
        }
        foreach (var profile in new[] { FinTsSynchronizationProfile.Rah7, FinTsSynchronizationProfile.Rah9, (FinTsSynchronizationProfile)99 })
        {
            var a = New();
            Verify(a.Start(requestBase, profile) == FinTsSynchronizationTransition.RejectedForReview && a.GetSnapshot().State == FinTsSynchronizationState.NeedsReview && Released(a), "Prohibited or unresolved profile hypotheses cannot start this request.");
        }
        var recoveryVector = vectors[1]; var recoveryBase = Recovery(recoveryVector)!;
        var recoveryAttempt = New(); var recoveryRequest = Request(recoveryVector);
        recoveryAttempt.Start(recoveryRequest, Profile, recoveryBase);
        var recoveryHanded = recoveryAttempt.AcceptSynchronization(Data(recoveryVector.GetProperty("synchronizationResponses")[0].GetString()!));
        Verify(ReferenceEquals(recoveryHanded.SynchronizationEvidence!.RecoveryContext, recoveryBase) && Retained(recoveryAttempt).Single() is string, "Prior-dialogue context is pinned for comparison and released immediately after handoff."); recoveryAttempt.Stop();
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC:1'", "OTHER:1'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:1'", "SYNTHETIC:2'", StringComparison.Ordinal),
            s => s.Replace("HISYN:4:4:4", "HISYN:4:4:3", StringComparison.Ordinal),
            s => s.Replace("+1+SYNTHETIC:1'", "+1'", StringComparison.Ordinal),
        })
        {
            var a = Stage(1); var result = a.AcceptSynchronization(Data(syncBytes, edit));
            Verify(result.Transition == FinTsSynchronizationTransition.ContextMismatch && result.SynchronizationEvidence is null && a.GetSnapshot().ResponsesAccepted == 0 && a.GetSnapshot().State == FinTsSynchronizationState.AwaitingSynchronization, "Foreign synchronization leaves the pinned request pending without handing evidence out.");
            Verify(a.AcceptSynchronization(syncBase).Transition == FinTsSynchronizationTransition.CloseRequired && a.GetSnapshot().SynchronizationIssues == FinTsSynchronizationIssue.None, "A matching synchronization clears transient scope issues."); a.Stop();
        }
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC:2'", "OTHER:2'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:2'", "SYNTHETIC:3'", StringComparison.Ordinal),
            s => s.Replace("+2+SYNTHETIC:2'", "+2'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal),
        })
        {
            var a = Stage(3); var result = a.AcceptClose(Response(closeBytes, edit));
            Verify(result.Transition == FinTsSynchronizationTransition.ContextMismatch && result.ClosingEvidence is null && a.GetSnapshot().ResponsesAccepted == 1 && a.GetSnapshot().State == FinTsSynchronizationState.AwaitingCloseResponse, "Foreign close replies cannot consume or replace the pending close.");
            Verify(a.AcceptClose(closeResponse).Transition == FinTsSynchronizationTransition.ReinitializationRequired && a.GetSnapshot().ClosingIssues == FinTsDialogueEndIssue.None && Released(a), "The matching close still completes after foreign candidates.");
        }
        foreach (var wrongClose in new[] { MakeClose("OTHER", 2, 2), MakeClose("SYNTHETIC", 3, 2), MakeClose("SYNTHETIC", 2, 3) })
        {
            var a = Stage(2);
            Verify(a.RecordClose(wrongClose) == FinTsSynchronizationTransition.ContextMismatch && a.GetSnapshot().RequestsRecorded == 1 && a.GetSnapshot().State == FinTsSynchronizationState.AwaitingCloseRequest, "Closing must use the assigned dialogue and next independent counters 2/2.");
            Verify(a.RecordClose(closeBase) == FinTsSynchronizationTransition.CloseRequestRecorded && a.GetSnapshot().ClosingIssues == FinTsDialogueEndIssue.None, "Corrected close context can be recorded without another synchronization."); a.Stop();
        }
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC", "unbekannt", StringComparison.Ordinal),
            s => s.Replace("0020::PUBLIC-SYNC-REPLY", "0030::PUBLIC-SYNC-REPLY", StringComparison.Ordinal),
            s => s.Replace("0010::PUBLIC-REPLY", "9800::PUBLIC-REPLY", StringComparison.Ordinal),
        })
        {
            var a = Stage(1); var result = a.AcceptSynchronization(Data(syncBytes, edit));
            Verify(result.Transition == FinTsSynchronizationTransition.RejectedForReview && result.SynchronizationEvidence is not null && Released(a) && a.GetSnapshot().ResponsesAccepted == 0, "Bound synchronization review or abort evidence terminates without progressing to close.");
            Verify(a.RecordClose(closeBase) == FinTsSynchronizationTransition.Terminal && a.AcceptSynchronization(syncBase).SynchronizationEvidence is null, "Review and synchronization abort never cause another close or replace evidence.");
        }
        foreach (string code in new[] { "0020", "9050", "7777" })
        {
            var a = Stage(3); var result = a.AcceptClose(Response(closeBytes, s => s.Replace("0100", code, StringComparison.Ordinal)));
            Verify(result.Transition == FinTsSynchronizationTransition.RejectedForReview && result.ClosingEvidence is not null && a.GetSnapshot().State == FinTsSynchronizationState.NeedsReview && a.GetSnapshot().ResponsesAccepted == 1 && Released(a), "Bound incomplete or error closing evidence is returned once for review.");
            Verify(a.AcceptClose(closeResponse).ClosingEvidence is null, "A later clean close cannot replace terminal review.");
        }
        for (int stage = 0; stage <= 3; stage++)
            foreach (bool stop in new[] { false, true })
            {
                var a = Stage(stage); if (stop) { a.Stop(); } else { a.Cancel(); }
                Verify(a.GetSnapshot().State == (stop ? FinTsSynchronizationState.Stopped : FinTsSynchronizationState.Cancelled) && Released(a) && a.RecordClose(closeBase) == FinTsSynchronizationTransition.Terminal && a.AcceptClose(closeResponse).ClosingEvidence is null, "Local cancellation/stop is terminal at every stage without sending a close.");
            }
        for (int stage = 1; stage <= 3; stage++)
        {
            var clock = new Clock { Milliseconds = 20000 }; var a = Stage(stage, clock);
            clock.Milliseconds = 29999;
            Verify(a.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(1), "Every stage shares the deadline anchored at initial recording.");
            clock.Milliseconds = 30000;
            Verify(a.GetSnapshot().State == FinTsSynchronizationState.TimedOut && Released(a), "Deadline equality expires each active stage and clears context.");
            clock = new Clock(); a = Stage(stage, clock); clock.Milliseconds = -1;
            Verify(a.GetSnapshot().State == FinTsSynchronizationState.ClockInvalid && Released(a), "Clock regression terminates every active stage.");
        }
        var deadlineClock = new Clock(); var deadline = Stage(1, deadlineClock); deadlineClock.Milliseconds = 9000;
        deadline.AcceptSynchronization(syncBase); deadlineClock.Milliseconds = 9999; deadline.RecordClose(closeBase); deadlineClock.Milliseconds = 10000;
        Verify(deadline.AcceptClose(closeResponse).ClosingEvidence is null && deadline.GetSnapshot().State == FinTsSynchronizationState.TimedOut && deadline.GetSnapshot().ResponsesAccepted == 1 && Released(deadline), "Progress does not restart the deadline and late close evidence is withheld.");
        foreach (int stage in new[] { 1, 3 })
        {
            var clock = new TriggerClock(); var a = Stage(stage, clock); clock.ReadsUntilExpiry = 2;
            var result = stage == 1 ? a.AcceptSynchronization(syncBase) : a.AcceptClose(closeResponse);
            Verify(result.Transition == FinTsSynchronizationTransition.Terminal && result.SynchronizationEvidence is null && result.ClosingEvidence is null && a.GetSnapshot().State == FinTsSynchronizationState.TimedOut && Released(a), "Expiry during either comparison prevents evidence handoff.");
        }
        foreach (Action action in new Action[] { () => New().Start(null!, Profile), () => New().AcceptSynchronization(null!), () => New().RecordClose(null!), () => New().AcceptClose(null!) })
        {
            try { action(); Verify(false, "Null lifecycle input must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null input is rejected."); }
        }
        foreach (Action action in new Action[] { () => new FinTsSynchronizationAttempt(TimeSpan.Zero), () => new FinTsSynchronizationAttempt(TimeSpan.FromMinutes(16)), () => New(new InvalidClock()) })
        {
            try { action(); Verify(false, "Invalid policy must fail."); }
            catch (ArgumentException) { Verify(true, "Invalid lifetime/frequency is rejected."); }
        }
        Verify(new FinTsSynchronizationAttempt(TimeSpan.FromMinutes(15)).GetSnapshot().State == FinTsSynchronizationState.Ready, "Maximum bounded lifetime is accepted.");
        var racing = Stage(0); var transitions = new FinTsSynchronizationTransition[32];
        Parallel.For(0, 32, i => transitions[i] = racing.Start(requestBase, Profile));
        Verify(transitions.Count(t => t == FinTsSynchronizationTransition.RequestRecorded) == 1, "Concurrent starts record one request.");
        var results = new FinTsSynchronizationAttemptResult[32];
        Parallel.For(0, 32, i => results[i] = racing.AcceptSynchronization(syncBase));
        Verify(results.Count(r => r.SynchronizationEvidence is not null) == 1 && racing.GetSnapshot().ResponsesAccepted == 1, "Concurrent synchronization replies produce one handoff.");
        Parallel.For(0, 32, i => transitions[i] = racing.RecordClose(closeBase));
        Verify(transitions.Count(t => t == FinTsSynchronizationTransition.CloseRequestRecorded) == 1 && racing.GetSnapshot().RequestsRecorded == 2, "Concurrent close recording pins one request.");
        Parallel.For(0, 32, i => results[i] = racing.AcceptClose(closeResponse));
        Verify(results.Count(r => r.ClosingEvidence is not null) == 1 && racing.GetSnapshot().ResponsesAccepted == 2 && Released(racing), "Concurrent close replies produce one terminal handoff.");
        for (int i = 0; i < 16; i++)
        {
            var a = Stage(3); FinTsSynchronizationAttemptResult? result = null;
            Parallel.Invoke(() => a.Cancel(), () => result = a.AcceptClose(closeResponse));
            Verify(a.GetSnapshot().State is FinTsSynchronizationState.Cancelled or FinTsSynchronizationState.ReinitializationRequired &&
                (result!.ClosingEvidence is not null) == (a.GetSnapshot().State == FinTsSynchronizationState.ReinitializationRequired) && Released(a), "Cancel/close races yield one immutable terminal outcome.");
        }
        Verify(typeof(FinTsSynchronizationSnapshot).GetProperties().All(p => p.PropertyType.IsValueType) && new object[] { handed, completed, racing, racing.GetSnapshot() }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default diagnostics and snapshots expose no source identifiers or recovered values.");
        Console.WriteLine($"FinTS synchronization-and-closing attempt verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static FinTsSynchronizationAttempt New(TimeProvider? clock = null) => new(TimeSpan.FromSeconds(10), clock ?? new Clock());
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock must not be used.");
    }
    private sealed class TriggerClock : TimeProvider
    {
        internal int ReadsUntilExpiry { get; set; } = int.MaxValue;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => --ReadsUntilExpiry <= 0 ? 10000 : 0;
    }
    private sealed class InvalidClock : TimeProvider { public override long TimestampFrequency => 0; }
    private static object[] Retained(FinTsSynchronizationAttempt attempt) => typeof(FinTsSynchronizationAttempt).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => !f.IsInitOnly && !f.FieldType.IsValueType).Select(f => f.GetValue(attempt)).OfType<object>().ToArray();
    private static bool Released(FinTsSynchronizationAttempt attempt) => Retained(attempt).Length == 0;
    private static FinTsUnsignedSynchronizationRequest Request(JsonElement v) => FinTsUnsignedSynchronizationRequest.Parse(Frame(v.GetProperty("requestBase64").GetString()!));
    private static FinTsUnsignedDialogueEndRequest CloseRequest(JsonElement v) => FinTsUnsignedDialogueEndRequest.Parse(Frame(v.GetProperty("closeRequestBase64").GetString()!), 2);
    private static FinTsUnsignedDialogueEndRequest MakeClose(string dialogue, int client, int bank) => FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(FinTsUnsignedDialogueEndWriter.Encode(dialogue, client)), bank);
    private static FinTsSynchronizationRecoveryContext? Recovery(JsonElement v) => v.GetProperty("previousDialogueId").GetString() is string prior ? new(prior, v.GetProperty("lastSubmittedMessageNumber").GetInt32()) : null;
    private static FinTsSynchronizationDataSet Data(string bytes, Func<string, string>? edit = null) => FinTsSynchronizationDataSet.Parse(Response(bytes, edit));
    private static FinTsResponse Response(string bytes, Func<string, string>? edit = null) => FinTsResponse.Parse(Frame(bytes, edit));
    private static FinTsMessageFrame Frame(string bytes, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(bytes));
        if (edit is not null) { wire = edit(wire); }
        wire = wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
}
