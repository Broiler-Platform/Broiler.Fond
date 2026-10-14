using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSynchronizationSessionClockTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        var vector = document.RootElement.GetProperty("vectors")[0];
        var candidate = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("syncCandidate")));
        var response = Response(vector.GetProperty("syncResponse"));
        var closingResponse = Response(vector.GetProperty("closingResponse"));

        // Four sub-TimeSpan-tick observations still reach an absolute one-tick deadline.
        var preciseClock = new Clock { Frequency = 40_000_000 };
        using (var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromTicks(1), preciseClock))
        {
            attempt.StartSynchronization(candidate);
            for (int i = 1; i <= 4; i++) { preciseClock.Timestamp = i; _ = attempt.GetSnapshot(); }
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.TimedOut && Released(attempt),
                "Frequent observations cannot discard fractional elapsed ticks or extend the absolute deadline.");
        }

        foreach (int phase in new[] { 0, 1, 2 })
            foreach (string boundary in new[] { "timestamp", "frequency" })
                foreach (string failure in new[] { "throw", "regression", "expiry", "cancel", "stop", "dispose", "cancel-throw", "stop-expiry" })
                {
                    var clock = new Clock(); using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromSeconds(10), clock);
                    attempt.StartSynchronization(candidate);
                    FinTsPinTanDialogueEndRequestBinding? closing = null;
                    if (phase > 0)
                    {
                        var sync = attempt.AcceptSynchronizationResponse(response).SynchronizationEvidence!;
                        closing = Closing(vector.GetProperty("closingCandidate"), sync);
                        if (phase == 2) { attempt.RecordClosingCandidate(closing); }
                    }
                    int handled = attempt.GetSnapshot().ResponsesHandled;
                    Action inject = () =>
                    {
                        if (failure is "cancel" or "cancel-throw") { attempt.Cancel(); }
                        if (failure is "stop" or "stop-expiry") { attempt.Stop(); }
                        if (failure == "dispose") { attempt.Dispose(); }
                        if (failure is "expiry" or "stop-expiry") { clock.Timestamp = 10000; }
                        if (failure == "regression")
                        {
                            if (boundary == "timestamp") { clock.Timestamp = -1; }
                            else { clock.Timestamp = 1; clock.Frequency = -1000; }
                        }
                        if (failure is "throw" or "cancel-throw") { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                    };
                    // Ensure negative elapsed conversion is observable at the frequency callback.
                    if (boundary == "frequency" && failure == "regression") { clock.Timestamp = 1; }
                    if (boundary == "timestamp") { clock.TimestampAction = inject; }
                    else { clock.FrequencyAction = inject; }
                    FinTsPinTanSynchronizationSessionResult? result = null;
                    FinTsPinTanSynchronizationSessionTransition transition;
                    if (phase == 0) { result = attempt.AcceptSynchronizationResponse(response); transition = result.Transition; }
                    else if (phase == 1) { transition = attempt.RecordClosingCandidate(closing!); }
                    else { result = attempt.AcceptClosingResponse(closingResponse); transition = result.Transition; }
                    var expected = failure switch
                    {
                        "cancel" or "cancel-throw" => FinTsPinTanSynchronizationSessionState.Cancelled,
                        "stop" or "stop-expiry" or "dispose" => FinTsPinTanSynchronizationSessionState.Stopped,
                        "expiry" => FinTsPinTanSynchronizationSessionState.TimedOut,
                        _ => FinTsPinTanSynchronizationSessionState.ClockInvalid,
                    };
                    Verify(transition == FinTsPinTanSynchronizationSessionTransition.Terminal && result?.ClosingEvidence is null && result?.SynchronizationEvidence is null,
                        "Clock failure or reentrant abandonment withholds evidence at every session phase.");
                    var snapshot = attempt.GetSnapshot();
                    Verify(snapshot.State == expected && snapshot.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                        snapshot.ResponsesHandled == handled && snapshot.RemainingTime == TimeSpan.Zero && Released(attempt),
                        "The original terminal outcome survives callbacks and pending metadata is released: " + phase + "/" + boundary + "/" + failure);
                }

        // Exercise all three observations surrounding response binding, semantics and publication.
        foreach (bool closingPhase in new[] { false, true })
            foreach (int observation in new[] { 1, 2, 3 })
            {
                var clock = new Clock(); using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromSeconds(10), clock);
                attempt.StartSynchronization(candidate);
                if (closingPhase)
                {
                    var sync = attempt.AcceptSynchronizationResponse(response).SynchronizationEvidence!;
                    attempt.RecordClosingCandidate(Closing(vector.GetProperty("closingCandidate"), sync));
                }
                int before = attempt.GetSnapshot().ResponsesHandled;
                clock.FrequencyReadsUntilAction = observation;
                clock.FrequencyAction = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL");
                var result = closingPhase ? attempt.AcceptClosingResponse(closingResponse) : attempt.AcceptSynchronizationResponse(response);
                Verify(result.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal && result.SynchronizationEvidence is null && result.ClosingEvidence is null &&
                    attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.ClockInvalid && attempt.GetSnapshot().ResponsesHandled == before && Released(attempt),
                    "Elapsed-time conversion failures never escape or publish evidence, including the final observation.");
            }
        Console.WriteLine($"FinTS synchronization session clock boundaries passed ({count} checks).");
    }

    private static bool Released(FinTsPinTanSynchronizationSessionAttempt attempt) =>
        new[] { "_pendingSynchronization", "_recovery", "_pendingClosing" }.All(name =>
            typeof(FinTsPinTanSynchronizationSessionAttempt).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt) is null);

    private static FinTsPinTanDialogueEndRequestBinding Closing(JsonElement element, FinTsPinTanSynchronizationEvidence evidence) =>
        FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.Evaluate(evidence,
            FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("requestBase64").GetString()!)), element.GetProperty("expectedBankMessageNumber").GetInt32()),
            FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(element.GetProperty("headerBase64").GetString()!)).Segments.Single())));

    private static FinTsResponse Response(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(element.TryGetProperty("wireBase64", out var wire) ? wire.GetString()! : element.GetProperty("responseBase64").GetString()!));
        return element.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }

    private sealed class Clock : TimeProvider
    {
        internal long Timestamp { get; set; }
        internal long Frequency { get; set; } = 1000;
        internal Action? TimestampAction { get; set; }
        internal Action? FrequencyAction { get; set; }
        internal int FrequencyReadsUntilAction { get; set; } = 1;
        public override long GetTimestamp()
        {
            var action = TimestampAction; TimestampAction = null; action?.Invoke(); return Timestamp;
        }
        public override long TimestampFrequency
        {
            get
            {
                if (FrequencyAction is not null && --FrequencyReadsUntilAction == 0)
                { var action = FrequencyAction; FrequencyAction = null; action(); }
                return Frequency;
            }
        }
    }
}
