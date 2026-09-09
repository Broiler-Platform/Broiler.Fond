using System.Text.Json;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Tests.Simulation;

namespace Broiler.Fond.Kernel.Tests;

internal static class SimulatorAndDiagnosticTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.FromHours(2));
    private const string Pin = "PUBLIC-SYNTHETIC-PIN-9073";
    private const string Tan = "PUBLIC-SYNTHETIC-TAN-8261";
    private const string Source = "PUBLIC-SYNTHETIC-ACCOUNT-6932";
    private const string BankText = "PUBLIC-BANK-TEXT\n\"pin\":\"" + Pin + "\",tan=" + Tan;

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); Verify(false, message); }
            catch (T error)
            {
                Verify(true, message);
                AssertSafe(error.ToString());
            }
        }
        void AssertSafe(string text) => Verify(
            new[] { Pin, Tan, Source, BankText, "123.45", "20.01" }.All(value => !text.Contains(value, StringComparison.Ordinal)),
            "Diagnostics/errors must exclude synthetic secrets, bank text and financial content.");

        LocalDiagnosticBuffer buffer = new(capacity: 2);
        buffer.Record(LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.Succeeded, Now);
        Verify(!buffer.IsEnabled && buffer.CreateSupportPreview().EventCount == 0, "Diagnostics must default to disabled.");
        buffer.SetEnabled(true);
        buffer.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, Now);
        DiagnosticSupportPreview original = buffer.CreateSupportPreview();
        buffer.Record(LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.Succeeded, Now);
        buffer.Record(LocalDiagnosticCode.ReadCompleted, LocalDiagnosticOutcome.PartialResult, Now);
        DiagnosticSupportPreview preview = buffer.CreateSupportPreview();
        using (JsonDocument json = JsonDocument.Parse(preview.Json))
        {
            JsonElement root = json.RootElement;
            Verify(root.GetProperty("schemaVersion").GetInt32() == 1 && root.EnumerateObject().Count() == 3, "Preview root must use the fixed schema.");
            JsonElement events = root.GetProperty("events");
            Verify(events.GetArrayLength() == 2 && preview.DroppedEventCount == 1 && root.GetProperty("droppedEvents").GetUInt64() == 1,
                "Overflow must evict the oldest event and disclose dropped count.");
            Verify(events[0].GetProperty("code").GetString() == "Authentication" && events[1].GetProperty("outcome").GetString() == "PartialResult",
                "Ring order and outcomes must survive eviction.");
            Verify(!preview.IncludesTimestamps && events.EnumerateArray().All(entry => entry.EnumerateObject().Select(p => p.Name).SequenceEqual(new[] { "code", "outcome" })),
                "Default preview event fields must be only code and outcome.");
        }
        using (JsonDocument json = JsonDocument.Parse(buffer.CreateSupportPreview(includeTimestamps: true).Json))
        {
            JsonElement entry = json.RootElement.GetProperty("events")[0];
            DateTimeOffset timestamp = entry.GetProperty("timestamp").GetDateTimeOffset();
            Verify(timestamp == Now && timestamp.Offset == TimeSpan.Zero && entry.EnumerateObject().Count() == 3,
                "Opt-in timestamps must preserve the instant in UTC.");
        }
        buffer.SetEnabled(false);
        Verify(buffer.CreateSupportPreview().EventCount == 0 && buffer.CreateSupportPreview().DroppedEventCount == 0,
            "Disabling must clear events and counters.");
        Verify(original.EventCount == 1 && original.Json.Contains("SessionStarted", StringComparison.Ordinal), "Created preview must stay immutable after clearing.");
        Throws<ArgumentOutOfRangeException>(() => _ = new LocalDiagnosticBuffer(0), "Zero diagnostic capacity must fail.");
        Throws<ArgumentOutOfRangeException>(() => _ = new LocalDiagnosticBuffer(1025), "Over-limit diagnostic capacity must fail.");
        Throws<ArgumentException>(() => buffer.Record((LocalDiagnosticCode)99, LocalDiagnosticOutcome.Pending, Now), "Unknown codes must fail even when disabled.");
        Throws<ArgumentException>(() => buffer.Record(LocalDiagnosticCode.Authentication, (LocalDiagnosticOutcome)99, Now), "Unknown outcomes must fail.");
        buffer.SetEnabled(true);
        Parallel.For(0, 2000, _ => buffer.Record(LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.Succeeded, Now));
        Verify(buffer.CreateSupportPreview().EventCount == 2 && buffer.CreateSupportPreview().DroppedEventCount == 1998, "Concurrent diagnostics must stay bounded without lost accounting.");
        buffer.Clear();
        Verify(buffer.IsEnabled && buffer.CreateSupportPreview().EventCount == 0 && buffer.CreateSupportPreview().DroppedEventCount == 0, "Clear must preserve opt-in while clearing contents.");

        SyntheticAccountFixture fixture = new(Now, Source);
        var identity = fixture.Identity;
        foreach (AccountRefreshOutcome outcome in new[] { AccountRefreshOutcome.Failed, AccountRefreshOutcome.Partial })
        {
            foreach (bool ambiguous in new[] { false, true })
            {
                var existing = fixture.Values(false)[0].Snapshots.Single();
                var duplicate = new Broiler.Fond.Kernel.Domain.Accounts.BalanceSnapshot(new(1000), new(1001),
                    existing.AccountId, existing.AccountIncarnationId, existing.AccountBindingId, existing.Kind, existing.Value,
                    existing.BankTimestamp, existing.RetrievedAt);
                AccountValueProjection projection = AccountValueProjection.Create(
                    [new(fixture.Bindings[0], AccountBalanceSupport.SupportedCurrentAccount, ambiguous ? [existing, duplicate] : [], outcome, isOffline: true)],
                    Now, TimeSpan.FromHours(1));
                BalanceFreshness expected = BalanceFreshness.Offline | (outcome == AccountRefreshOutcome.Failed ? BalanceFreshness.RefreshFailed : BalanceFreshness.RefreshPartial);
                Verify(projection.Rows[0].Freshness == expected && projection.Rows[0].Value is null,
                    "Missing or ambiguous snapshots must retain refresh/offline warnings without inventing source-time evidence.");
            }
        }
        SimulationStep Auth(SimulatedReplyKind reply) => new(SimulatedCommand.Authenticate, reply, BankText);
        SimulationStep Read(SimulatedReplyKind reply) => new(SimulatedCommand.ReadAccounts, reply, BankText);
        SimulationStep Continue(SimulatedReplyKind reply) => new(SimulatedCommand.ContinueChallenge, reply, BankText);
        LocalDiagnosticBuffer logs = new(enabled: true);
        ReadOnlySimulator Make(params SimulationStep[] steps) => new(steps, logs, fixture);
        SimulationResult Start(ReadOnlySimulator session, CancellationToken token = default)
        {
            char[] secret = Pin.ToCharArray();
            SimulationResult result = session.Start(secret, Now, token);
            Verify(secret.All(c => c == '\0'), "PIN buffer must be cleared after start.");
            AssertSafe(result.ToString()!);
            AssertSafe(logs.CreateSupportPreview().Json);
            return result;
        }
        SimulationResult Resume(ReadOnlySimulator session, SimulationChallenge challenge, DateTimeOffset? time = null, CancellationToken token = default)
        {
            char[] secret = Tan.ToCharArray();
            SimulationResult result = session.Continue(challenge, secret, time ?? Now.AddSeconds(30), token);
            Verify(secret.All(c => c == '\0'), "TAN buffer must be cleared after continuation.");
            AssertSafe(result.ToString()!);
            AssertSafe(logs.CreateSupportPreview(true).Json);
            return result;
        }

        foreach (bool partial in new[] { false, true })
        {
            ReadOnlySimulator session = Make(Auth(SimulatedReplyKind.Authenticated), Read(partial ? SimulatedReplyKind.Partial : SimulatedReplyKind.Success));
            SimulationResult result = Start(session);
            Verify(result.State == SimulationState.Completed && session.ConsumedSteps == 2 && result.Outcome == (partial ? LocalDiagnosticOutcome.PartialResult : LocalDiagnosticOutcome.Succeeded),
                "Success/partial script must finish exactly once with the correct outcome.");
            Verify(result.Rediscovery!.Count == 2 && result.Rediscovery.All(d => d.Action == AccountRediscoveryAction.ReuseBinding) &&
                result.Rediscovery.Select(d => d.ReusedBinding).SequenceEqual(fixture.Bindings), "Simulation must reuse the exact fixture account identities.");
            var total = result.Values!.Totals.Single();
            Verify(total.NetTotal!.Amount == (partial ? 123.45m : 103.44m) && total.IsPartial == partial && total.ExcludedCount == (partial ? 1 : 0),
                "Partial result must exclude the missing balance and retain exact totals.");
            Verify(!partial || result.Values.Rows[1].Value is null && result.Values.Rows[1].Freshness.HasFlag(BalanceFreshness.RefreshPartial),
                "Partial balance must expose missing value and partial-refresh warning.");
        }

        (SimulatedReplyKind Reply, SimulationState State, LocalDiagnosticOutcome Outcome)[] failures =
        [
            (SimulatedReplyKind.InvalidCredentials, SimulationState.Rejected, LocalDiagnosticOutcome.InvalidCredentials),
            (SimulatedReplyKind.AccessLocked, SimulationState.Rejected, LocalDiagnosticOutcome.AccessLocked),
            (SimulatedReplyKind.Timeout, SimulationState.TimedOut, LocalDiagnosticOutcome.TimedOut),
            (SimulatedReplyKind.Maintenance, SimulationState.Unavailable, LocalDiagnosticOutcome.TemporarilyUnavailable),
            (SimulatedReplyKind.TransportFailure, SimulationState.Unavailable, LocalDiagnosticOutcome.TemporarilyUnavailable),
            (SimulatedReplyKind.ChangedParameters, SimulationState.ReauthenticationRequired, LocalDiagnosticOutcome.ReauthenticationRequired),
            (SimulatedReplyKind.Malformed, SimulationState.InvalidResponse, LocalDiagnosticOutcome.MalformedResponse),
        ];
        foreach (var failure in failures)
        {
            foreach (bool atRead in new[] { false, true })
            {
                ReadOnlySimulator session = atRead ? Make(Auth(SimulatedReplyKind.Authenticated), Read(failure.Reply)) : Make(Auth(failure.Reply), Read(SimulatedReplyKind.Success));
                SimulationResult result = Start(session);
                Verify(result.State == failure.State && result.Outcome == failure.Outcome && result.Values is null && result.Rediscovery is null,
                    "Failure must preserve its category without fabricated account results.");
                Verify(session.ConsumedSteps == (atRead ? 2 : 1), "Failure must stop without retry or extra script consumption.");
                char[] repeatPin = Pin.ToCharArray();
                Throws<InvalidOperationException>(() => session.Start(repeatPin, Now), "Terminal sessions must reject restart.");
                Verify(repeatPin.All(c => c == '\0'), "Rejected restart must still clear the PIN.");
            }
        }

        ReadOnlySimulator challenged = Make(Auth(SimulatedReplyKind.ManualChallenge), Continue(SimulatedReplyKind.Authenticated), Read(SimulatedReplyKind.Success));
        SimulationResult pending = Start(challenged);
        Verify(pending.State == SimulationState.ChallengeRequired && pending.Challenge!.ExpiresAt == Now.AddMinutes(2) && pending.Values is null && challenged.ConsumedSteps == 1,
            "Manual challenge must wait without automatic polling or reading.");
        char[] foreignTan = Tan.ToCharArray();
        Throws<InvalidOperationException>(() => challenged.Continue(new SimulationChallenge(Now.AddMinutes(2)), foreignTan, Now), "Foreign challenge must fail session correlation.");
        Verify(foreignTan.All(c => c == '\0') && challenged.State == SimulationState.ChallengeRequired && challenged.ConsumedSteps == 1,
            "Foreign challenge must clear TAN without consuming the current challenge.");
        Verify(Resume(challenged, pending.Challenge!).State == SimulationState.Completed && challenged.ConsumedSteps == 3, "Manual continuation must complete the scripted read.");
        char[] repeatedTan = Tan.ToCharArray();
        Throws<InvalidOperationException>(() => challenged.Continue(pending.Challenge!, repeatedTan, Now), "Consumed challenge must reject replay.");
        Verify(repeatedTan.All(c => c == '\0'), "Rejected challenge replay must clear TAN.");

        foreach (var failure in failures)
        {
            ReadOnlySimulator session = Make(Auth(SimulatedReplyKind.ManualChallenge), Continue(failure.Reply), Read(SimulatedReplyKind.Success));
            SimulationChallenge challenge = Start(session).Challenge!;
            Verify(Resume(session, challenge).State == failure.State && session.ConsumedSteps == 2, "Continuation failure must stop before reading.");
        }
        ReadOnlySimulator expired = Make(Auth(SimulatedReplyKind.ManualChallenge), Continue(SimulatedReplyKind.Authenticated));
        SimulationChallenge expiring = Start(expired).Challenge!;
        Verify(Resume(expired, expiring, expiring.ExpiresAt).State == SimulationState.TimedOut && expired.ConsumedSteps == 1, "Exact challenge deadline must time out before continuation.");
        ReadOnlySimulator cancelled = Make(Auth(SimulatedReplyKind.Authenticated), Read(SimulatedReplyKind.Success));
        Verify(Start(cancelled, new CancellationToken(true)).State == SimulationState.Cancelled && cancelled.ConsumedSteps == 0, "Pre-cancelled session must not authenticate.");
        foreach (bool viaToken in new[] { false, true })
        {
            ReadOnlySimulator session = Make(Auth(SimulatedReplyKind.ManualChallenge), Continue(SimulatedReplyKind.Authenticated));
            SimulationChallenge challenge = Start(session).Challenge!;
            SimulationResult result = viaToken ? Resume(session, challenge, token: new CancellationToken(true)) : session.Cancel(Now);
            Verify(result.State == SimulationState.Cancelled && result.Values is null && session.ConsumedSteps == 1, "Pending challenge cancellation must stop before continuation.");
        }
        ReadOnlySimulator neverStarted = Make(Auth(SimulatedReplyKind.Authenticated));
        Verify(neverStarted.Cancel(Now).State == SimulationState.Cancelled && neverStarted.ConsumedSteps == 0, "Explicit cancellation before start must consume no steps.");
        Throws<InvalidOperationException>(() => neverStarted.Cancel(Now), "Terminal cancellation must not restart the session.");

        foreach (int length in new[] { 0, 129 })
        {
            ReadOnlySimulator session = Make(Auth(SimulatedReplyKind.Authenticated));
            char[] badPin = new string('p', length).ToCharArray();
            Verify(session.Start(badPin, Now).State == SimulationState.Rejected && session.ConsumedSteps == 0 && badPin.All(c => c == '\0'), "Invalid PIN length must clear input and consume no script.");
            session = Make(Auth(SimulatedReplyKind.ManualChallenge), Continue(SimulatedReplyKind.Authenticated));
            SimulationChallenge challenge = Start(session).Challenge!;
            char[] badTan = new string('t', length).ToCharArray();
            Verify(session.Continue(challenge, badTan, Now).State == SimulationState.Rejected && session.ConsumedSteps == 1 && badTan.All(c => c == '\0'), "Invalid TAN length must clear input and end the challenge.");
        }
        foreach (SimulationStep[] script in new[]
        {
            new[] { Read(SimulatedReplyKind.Success) },
            new[] { Auth(SimulatedReplyKind.Authenticated) },
            new[] { Auth(SimulatedReplyKind.Success) },
            new[] { Auth(SimulatedReplyKind.Authenticated), Read(SimulatedReplyKind.Authenticated) },
            new[] { Auth(SimulatedReplyKind.Authenticated), Read(SimulatedReplyKind.Success), Read(SimulatedReplyKind.Success) },
        })
        {
            Verify(Start(Make(script)).State == SimulationState.InvalidResponse, "Missing, misordered, extra or wrong-kind steps must fail closed.");
        }
        Throws<ArgumentException>(() => Make(), "Empty scripts must fail.");
        Throws<ArgumentException>(() => Make(Enumerable.Repeat(Auth(SimulatedReplyKind.Authenticated), 17).ToArray()), "Over-limit scripts must fail.");
        Throws<ArgumentException>(() => Make(new SimulationStep((SimulatedCommand)99, SimulatedReplyKind.Success)), "Unknown command must fail.");
        Throws<ArgumentException>(() => Make(Auth((SimulatedReplyKind)99)), "Unknown reply must fail.");
        Throws<ArgumentException>(() => Make(new SimulationStep(SimulatedCommand.Authenticate, SimulatedReplyKind.Malformed, new string('x', 4097))), "Over-limit raw text must fail.");
        AssertSafe(Auth(SimulatedReplyKind.Malformed).ToString());
        char[] edgePin = Pin.ToCharArray();
        Verify(Make(Auth(SimulatedReplyKind.ManualChallenge)).Start(edgePin, DateTimeOffset.MaxValue).State == SimulationState.TimedOut && edgePin.All(c => c == '\0'),
            "Unrepresentable challenge expiry must fail safely and clear PIN.");
        Verify(ReferenceEquals(identity, fixture.Identity) && fixture.Values(false)[1].Snapshots.Single().Value!.Amount == -20.01m,
            "Simulation must not mutate the fixture identity or overwrite cached balances after failure/partial results.");
        AssertSafe(logs.CreateSupportPreview().Json);
        Console.WriteLine($"Simulator and diagnostic verification: {count} checks completed.");
    }
}
