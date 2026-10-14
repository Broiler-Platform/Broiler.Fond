using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSynchronizationSessionAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(
            Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        // Standard clean synchronization evidence for candidate creation
        var standardSyncSample = vectors[0];
        var standardSyncCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
            FinTsPinTanSignatureTrailerTests.Evidence(standardSyncSample.GetProperty("syncCandidate")));
        var standardSyncResp = Response(standardSyncSample.GetProperty("syncResponse"));
        var standardSyncEvidence = FinTsPinTanSynchronizationEvidence.Evaluate(
            FinTsPinTanResponseBinding.Evaluate(standardSyncCand, standardSyncResp),
            FinTsSynchronizationDataSet.Parse(standardSyncResp),
            null);

        // Escaped synchronization evidence matching extra closing candidate dialogue
        var escapedSyncSample = vectors[3];
        var escapedSyncCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
            FinTsPinTanSignatureTrailerTests.Evidence(escapedSyncSample.GetProperty("syncCandidate")));
        var escapedSyncResp = Response(escapedSyncSample.GetProperty("syncResponse"));
        var escapedSyncEvidence = FinTsPinTanSynchronizationEvidence.Evaluate(
            FinTsPinTanResponseBinding.Evaluate(escapedSyncCand, escapedSyncResp),
            FinTsSynchronizationDataSet.Parse(escapedSyncResp),
            null);

        foreach (var vector in vectors)
        {
            var clock = new Clock();
            using var attempt = New(clock);

            var syncCandElem = vector.GetProperty("syncCandidate");
            FinTsPinTanRequestBinding syncCandidate;
            try
            {
                syncCandidate = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                    FinTsPinTanSignatureTrailerTests.Evidence(syncCandElem));
            }
            catch (FinTsFormatException)
            {
                // Fallback for tests exercising invalid candidate structures
                syncCandidate = standardSyncCand;
            }

            FinTsSynchronizationRecoveryContext? recovery = null;
            if (vector.GetProperty("recovery").ValueKind != JsonValueKind.Null)
            {
                var recElem = vector.GetProperty("recovery");
                recovery = new FinTsSynchronizationRecoveryContext(
                    recElem.GetProperty("previousDialogueId").GetString()!,
                    recElem.GetProperty("lastSubmittedMessageNumber").GetInt32());
            }

            var syncResponse = Response(vector.GetProperty("syncResponse"));
            var closingCandElem = vector.GetProperty("closingCandidate");
            var closingResponse = Response(vector.GetProperty("closingResponse"));
            var extraResponses = vector.GetProperty("extraResponses").EnumerateArray().ToArray();

            FinTsPinTanSynchronizationEvidence? acceptedSyncEvidence = null;

            string AcceptClosing(FinTsResponse response)
            {
                var result = attempt.AcceptClosingResponse(response);
                if (result.Transition is FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired or FinTsPinTanSynchronizationSessionTransition.AbortReported or FinTsPinTanSynchronizationSessionTransition.RejectedForReview)
                {
                    Verify(result.ClosingEvidence is not null && result.Stage == FinTsPinTanSynchronizationSessionStage.Terminal,
                        "Every processed terminal synchronization closing response hands evidence to its caller.");
                    Verify(result.SynchronizationEvidence is not null && ReferenceEquals(result.SynchronizationEvidence, acceptedSyncEvidence),
                        "Terminal closing preserves the exact caller-owned synchronization evidence.");
                }
                if (result.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal)
                {
                    Verify(result.SynchronizationEvidence is null && result.ClosingEvidence is null, "Terminal replay does not repeat synchronization evidence.");
                }
                return result.Transition.ToString();
            }

            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition;
                int? index = step.GetProperty("index").ValueKind == JsonValueKind.Null ? null : step.GetProperty("index").GetInt32();
                switch (step.GetProperty("action").GetString())
                {
                    case "startSync":
                        transition = attempt.StartSynchronization(syncCandidate, recovery).ToString();
                        break;

                    case "syncResponse":
                        if (index.HasValue && index.Value < extraResponses.Length && extraResponses[index.Value].TryGetProperty("wireBase64", out _))
                        {
                            var extraResp = Response(extraResponses[index.Value]);
                            var res = attempt.AcceptSynchronizationResponse(extraResp);
                            transition = res.Transition.ToString();
                            if (res.SynchronizationEvidence is not null)
                            {
                                acceptedSyncEvidence = res.SynchronizationEvidence;
                            }
                        }
                        else
                        {
                            var res = attempt.AcceptSynchronizationResponse(syncResponse);
                            transition = res.Transition.ToString();
                            if (res.SynchronizationEvidence is not null)
                            {
                                acceptedSyncEvidence = res.SynchronizationEvidence;
                            }
                        }
                        break;

                    case "recordClose":
                        if (index.HasValue && index.Value < extraResponses.Length && extraResponses[index.Value].TryGetProperty("requestBase64", out _))
                        {
                            // Dialogue mismatch candidate
                            var extraCloseCandElem = extraResponses[index.Value];
                            var extraCloseCand = ClosingCandidate(extraCloseCandElem, escapedSyncEvidence);
                            transition = attempt.RecordClosingCandidate(extraCloseCand).ToString();
                        }
                        else
                        {
                            var closeCand = ClosingCandidate(closingCandElem, acceptedSyncEvidence ?? standardSyncEvidence);
                            transition = attempt.RecordClosingCandidate(closeCand).ToString();
                        }
                        break;

                    case "closeResponse":
                        if (index.HasValue && index.Value < extraResponses.Length && extraResponses[index.Value].TryGetProperty("wireBase64", out _))
                        {
                            var extraCloseResp = Response(extraResponses[index.Value]);
                            transition = AcceptClosing(extraCloseResp);
                        }
                        else
                        {
                            transition = AcceptClosing(closingResponse);
                        }
                        break;

                    case "cancel":
                        attempt.Cancel();
                        transition = "Cancelled";
                        break;

                    case "stop":
                        attempt.Stop();
                        transition = "Stopped";
                        break;

                    case "dispose":
                        attempt.Dispose();
                        transition = "Stopped";
                        break;

                    case "snapshot":
                        _ = attempt.GetSnapshot();
                        transition = "Snapshot";
                        break;

                    default:
                        throw new InvalidOperationException("Unknown action: " + step.GetProperty("action").GetString());
                }

                var snapshot = attempt.GetSnapshot();
                Verify(transition == step.GetProperty("transition").GetString(),
                    $"Independent transition matches in {vector.GetProperty("name").GetString()}: {transition} vs {step.GetProperty("transition").GetString()}.");
                Verify(snapshot.State.ToString() == step.GetProperty("state").GetString(),
                    $"Independent state matches in {vector.GetProperty("name").GetString()}: {snapshot.State} vs {step.GetProperty("state").GetString()}.");
                Verify(snapshot.Stage.ToString() == step.GetProperty("stage").GetString(),
                    $"Independent stage matches in {vector.GetProperty("name").GetString()}: {snapshot.Stage} vs {step.GetProperty("stage").GetString()}.");
                Verify(snapshot.RequestsRecorded == step.GetProperty("requestsRecorded").GetInt32(),
                    "Requests recorded count matches in " + vector.GetProperty("name").GetString());
                Verify(snapshot.ResponsesHandled == step.GetProperty("responsesHandled").GetInt32(),
                    "Responses handled count matches in " + vector.GetProperty("name").GetString());

                // Monotonic deadline and metadata retention invariants
                if (snapshot.Stage == FinTsPinTanSynchronizationSessionStage.Terminal)
                {
                    FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                    Verify(snapshot.RemainingTime == TimeSpan.Zero, "Terminal state must report zero remaining time.");
                    Verify(PendingSync(attempt) is null && PendingClose(attempt) is null,
                        "Terminal state must immediately release all pending candidate metadata.");
                }
                else if (snapshot.State is FinTsPinTanSynchronizationSessionState.AwaitingSynchronizationResponse)
                {
                    Verify(ReferenceEquals(PendingSync(attempt), syncCandidate),
                        "AwaitingSynchronizationResponse must retain exact pending synchronization candidate.");
                    Verify(snapshot.RemainingTime > TimeSpan.Zero && snapshot.RemainingTime <= TimeSpan.FromSeconds(10),
                        "Monotonic remaining time reflects active deadline.");
                }
                else if (snapshot.State is FinTsPinTanSynchronizationSessionState.AwaitingCloseResponse)
                {
                    Verify(PendingClose(attempt) is not null,
                        "AwaitingCloseResponse must retain pending closing candidate.");
                    Verify(snapshot.RemainingTime > TimeSpan.Zero && snapshot.RemainingTime <= TimeSpan.FromSeconds(10),
                        "Monotonic remaining time reflects active deadline across stages.");
                }
            }

            if (attempt.GetSnapshot().Stage == FinTsPinTanSynchronizationSessionStage.Terminal)
            {
                Verify(PendingSync(attempt) is null && PendingClose(attempt) is null,
                    "All terminal traces leave pending candidate references cleared.");
            }
        }

        Verify(vectors.Length == 24, "All twenty-four independent synchronization session traces ran.");

        // 1. One-time evidence handoff and terminal idempotency
        {
            var vector = vectors[0];
            var syncCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("syncCandidate")));
            var syncResp = Response(vector.GetProperty("syncResponse"));
            var closingCandElem = vector.GetProperty("closingCandidate");
            var closingResp = Response(vector.GetProperty("closingResponse"));

            using var attempt = New();
            Verify(attempt.StartSynchronization(syncCand) == FinTsPinTanSynchronizationSessionTransition.CandidateRecorded,
                "StartSynchronization transitions to CandidateRecorded.");
            var syncResult = attempt.AcceptSynchronizationResponse(syncResp);
            Verify(syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.CloseRequired &&
                   syncResult.SynchronizationEvidence is not null &&
                   syncResult.SynchronizationEvidence.HasMatchingEvidence,
                   "AcceptSynchronizationResponse yields CloseRequired with matching evidence.");

            var closeCand = ClosingCandidate(closingCandElem, syncResult.SynchronizationEvidence!);
            Verify(attempt.RecordClosingCandidate(closeCand) == FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded,
                "RecordClosingCandidate transitions to CloseCandidateRecorded.");

            var closeResult = attempt.AcceptClosingResponse(closingResp);
            Verify(closeResult.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired &&
                   closeResult.SynchronizationEvidence is not null &&
                   closeResult.ClosingEvidence is not null &&
                   closeResult.ClosingEvidence.Outcome == FinTsDialogueEndOutcome.ClosureReported,
                   "AcceptClosingResponse yields ReinitializationRequired with caller-owned evidence handoff.");

            // Subsequent attempts after terminal return Terminal without repeating handoff
            var terminalReplay = attempt.AcceptClosingResponse(closingResp);
            Verify(terminalReplay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                   terminalReplay.ClosingEvidence is null,
                   "Terminal attempt rejects reparsed responses without repeating evidence handoff.");
            Verify(attempt.StartSynchronization(syncCand) == FinTsPinTanSynchronizationSessionTransition.Terminal,
                "StartSynchronization on terminal session returns Terminal.");
        }

        // 2. Pre-cancellation boundary
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            using var attempt = New();
            try
            {
                attempt.StartSynchronization(standardSyncCand, cancellationToken: cts.Token);
                Verify(false, "Pre-cancelled token must throw OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Cancelled,
                    "Pre-cancelled StartSynchronization marks state as Cancelled.");
                Verify(PendingSync(attempt) is null, "Pre-cancelled StartSynchronization does not retain candidate.");
            }
        }

        // 3. Stop before start
        {
            using var attempt = New();
            attempt.Stop();
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Stopped,
                "Stop before start marks state as Stopped.");
            Verify(attempt.StartSynchronization(standardSyncCand) == FinTsPinTanSynchronizationSessionTransition.Terminal,
                "StartSynchronization on stopped attempt returns Terminal.");
        }

        // 4. In-flight cancellation during synchronization
        {
            using var attempt = New();
            attempt.StartSynchronization(standardSyncCand);
            attempt.Cancel();
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Cancelled,
                "Cancel during synchronization marks state as Cancelled.");
            Verify(attempt.AcceptSynchronizationResponse(standardSyncResp).Transition == FinTsPinTanSynchronizationSessionTransition.Terminal,
                "AcceptSynchronizationResponse on cancelled attempt returns Terminal.");
        }

        // 5. In-flight cancellation during closing
        {
            using var attempt = New();
            attempt.StartSynchronization(standardSyncCand);
            var syncResult = attempt.AcceptSynchronizationResponse(standardSyncResp);
            var closeCand = ClosingCandidate(vectors[0].GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
            attempt.RecordClosingCandidate(closeCand);
            attempt.Cancel();
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Cancelled,
                "Cancel during closing marks state as Cancelled.");
            Verify(attempt.AcceptClosingResponse(Response(vectors[0].GetProperty("closingResponse"))).Transition ==
                   FinTsPinTanSynchronizationSessionTransition.Terminal,
                "AcceptClosingResponse on cancelled attempt returns Terminal.");
        }

        // 6. Dispose boundary
        {
            using var attempt = New();
            attempt.StartSynchronization(standardSyncCand);
            attempt.Dispose();
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Stopped,
                "Dispose transitions to Stopped.");
            Verify(PendingSync(attempt) is null, "Dispose zeros pending candidate.");
        }

        // 7. Clock regression boundary
        {
            var clock = new Clock { Milliseconds = 100 };
            using var attempt = New(clock);
            attempt.StartSynchronization(standardSyncCand);
            clock.Milliseconds = 50; // Clock regression
            _ = attempt.GetSnapshot();
            Verify(attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.ClockInvalid,
                "Clock regression transitions to ClockInvalid.");
        }

        // 8. Clock exception boundary
        {
            var clock = new Clock { ThrowTimestamp = true };
            using var attempt = New(clock);
            var trans = attempt.StartSynchronization(standardSyncCand);
            Verify(trans == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                   attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.ClockInvalid,
                "Clock exception on StartSynchronization transitions to ClockInvalid.");
        }

        // 9. Multithreaded concurrent closing response submission
        {
            var vector = vectors[0];
            var syncCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("syncCandidate")));
            var syncResp = Response(vector.GetProperty("syncResponse"));
            var closingCandElem = vector.GetProperty("closingCandidate");
            var closingResp = Response(vector.GetProperty("closingResponse"));

            using var attempt = New();
            attempt.StartSynchronization(syncCand);
            var syncRes = attempt.AcceptSynchronizationResponse(syncResp);
            var closeCand = ClosingCandidate(closingCandElem, syncRes.SynchronizationEvidence!);
            attempt.RecordClosingCandidate(closeCand);

            var results = new FinTsPinTanSynchronizationSessionResult[16];
            Parallel.For(0, results.Length, i =>
            {
                results[i] = attempt.AcceptClosingResponse(closingResp);
            });

            int handoffs = results.Count(r => r.ClosingEvidence is not null);
            int completions = results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired);
            Verify(handoffs == 1, $"Concurrent closing submissions produce exactly one evidence handoff (got {handoffs}).");
            Verify(completions == 1, $"Concurrent closing submissions produce exactly one ReinitializationRequired transition (got {completions}).");
            Verify(attempt.GetSnapshot().ResponsesHandled == 2, "Concurrent submissions result in exactly 2 handled responses.");
            Verify(PendingClose(attempt) is null, "Concurrent submissions release pending closing metadata.");
        }

        // 10. Constructor lifetime bounds validation
        {
            try
            {
                using var _ = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.Zero);
                Verify(false, "Zero lifetime must throw ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Zero lifetime throws ArgumentOutOfRangeException.");
            }

            try
            {
                using var _ = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(16));
                Verify(false, "Lifetime exceeding 15 minutes must throw ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Lifetime exceeding 15 minutes throws ArgumentOutOfRangeException.");
            }

            try
            {
                using var _ = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(10), new Clock { Frequency = 0 });
                Verify(false, "Non-positive clock frequency must throw ArgumentException.");
            }
            catch (ArgumentException)
            {
                Verify(true, "Non-positive clock frequency throws ArgumentException.");
            }
        }

        // 11. Null arguments validation
        {
            using var attempt = New();
            try { attempt.StartSynchronization(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(true, "Null StartSynchronization candidate throws."); }

            try { attempt.AcceptSynchronizationResponse((FinTsSynchronizationDataSet)null!); Verify(false, "Null data set must throw."); }
            catch (ArgumentNullException) { Verify(true, "Null AcceptSynchronizationResponse data set throws."); }

            try { attempt.AcceptSynchronizationResponse((FinTsResponse)null!); Verify(false, "Null response must throw."); }
            catch (ArgumentNullException) { Verify(true, "Null AcceptSynchronizationResponse response throws."); }

            try { attempt.RecordClosingCandidate(null!); Verify(false, "Null closing candidate must throw."); }
            catch (ArgumentNullException) { Verify(true, "Null RecordClosingCandidate candidate throws."); }

            try { attempt.AcceptClosingResponse(null!); Verify(false, "Null closing response must throw."); }
            catch (ArgumentNullException) { Verify(true, "Null AcceptClosingResponse response throws."); }
        }

        // 12. Culture invariance across cultures
        {
            var savedCulture = CultureInfo.CurrentCulture;
            try
            {
                foreach (var cultureName in new[] { "de-DE", "tr-TR", "en-US", "ar-SA" })
                {
                    CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                    using var attempt = New();
                    attempt.StartSynchronization(standardSyncCand);
                    var syncResult = attempt.AcceptSynchronizationResponse(standardSyncResp);
                    var closeCand = ClosingCandidate(vectors[0].GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
                    attempt.RecordClosingCandidate(closeCand);
                    var closeResult = attempt.AcceptClosingResponse(Response(vectors[0].GetProperty("closingResponse")));
                    Verify(closeResult.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired,
                        $"Culture invariance verified for {cultureName}.");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = savedCulture;
            }
        }

        // 13. Reflection: Snapshot diagnostics strictly scalar
        Verify(typeof(FinTsPinTanSynchronizationSessionSnapshot).GetProperties().All(p =>
            p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)),
            "FinTsPinTanSynchronizationSessionSnapshot exposes only scalar properties (enums, ints, TimeSpan). No secrets, IBANs, or references.");

        foreach (string ending in new[] { "Cancel", "Stop", "Dispose", "Timeout", "ClockInvalid", "TokenCancellation" })
        {
            for (int stage = 0; stage <= 3; stage++)
            {
                var clock = new Clock();
                using var attempt = New(clock);
                FinTsPinTanSynchronizationSessionResult? result = null;
                if (stage >= 1) { attempt.StartSynchronization(standardSyncCand); }
                if (stage >= 2)
                {
                    result = attempt.AcceptSynchronizationResponse(standardSyncResp);
                    Verify(result.Transition == FinTsPinTanSynchronizationSessionTransition.CloseRequired, "Ownership setup accepts synchronization.");
                }
                if (stage >= 3)
                {
                    Verify(attempt.RecordClosingCandidate(ClosingCandidate(standardSyncSample.GetProperty("closingCandidate"), result!.SynchronizationEvidence!)) == FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded,
                        "Ownership setup records synchronization closing.");
                }
                var before = attempt.GetSnapshot();
                switch (ending)
                {
                    case "Cancel": attempt.Cancel(); break;
                    case "Stop": attempt.Stop(); break;
                    case "Dispose": attempt.Dispose(); break;
                    case "Timeout":
                        if (stage == 0) { attempt.StartSynchronization(standardSyncCand); before = attempt.GetSnapshot(); }
                        clock.Milliseconds = 10_000;
                        break;
                    case "ClockInvalid":
                        if (stage == 0) { attempt.StartSynchronization(standardSyncCand); before = attempt.GetSnapshot(); }
                        clock.ThrowFrequency = true;
                        break;
                    default:
                        try
                        {
                            attempt.StartSynchronization(standardSyncCand, cancellationToken: new CancellationToken(true));
                            Verify(false, "Cancelled synchronization token must throw.");
                        }
                        catch (OperationCanceledException) { Verify(true, "Cancelled synchronization token terminates ownership."); }
                        break;
                }
                var after = attempt.GetSnapshot();
                var expectedState = ending switch
                {
                    "Cancel" or "TokenCancellation" => FinTsPinTanSynchronizationSessionState.Cancelled,
                    "Timeout" => FinTsPinTanSynchronizationSessionState.TimedOut,
                    "ClockInvalid" => FinTsPinTanSynchronizationSessionState.ClockInvalid,
                    _ => FinTsPinTanSynchronizationSessionState.Stopped,
                };
                Verify(after.State == expectedState && after.Stage == FinTsPinTanSynchronizationSessionStage.Terminal, "Synchronization cleanup reaches the expected terminal state.");
                FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                Verify(after.RequestsRecorded == before.RequestsRecorded && after.ResponsesHandled == before.ResponsesHandled,
                    "Synchronization cleanup preserves scalar progress counts.");
                Verify(result is null || result.SynchronizationEvidence!.HasMatchingEvidence, "Caller synchronization evidence survives cleanup.");
                attempt.Dispose();
                Verify(attempt.StartSynchronization(standardSyncCand) == FinTsPinTanSynchronizationSessionTransition.Terminal && attempt.GetSnapshot().State == expectedState,
                    "Synchronization cleanup is idempotent and prevents restart.");
            }
        }

        Console.WriteLine($"FinTS assembled PIN/TAN synchronization session attempt passed ({vectors.Length} independent traces, {count} checks).");
    }

    private static FinTsPinTanSynchronizationSessionAttempt New(Clock? clock = null) =>
        new(TimeSpan.FromSeconds(10), clock ?? new Clock());

    private static object? PendingSync(FinTsPinTanSynchronizationSessionAttempt attempt) =>
        typeof(FinTsPinTanSynchronizationSessionAttempt).GetField("_pendingSynchronization", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);

    private static object? PendingClose(FinTsPinTanSynchronizationSessionAttempt attempt) =>
        typeof(FinTsPinTanSynchronizationSessionAttempt).GetField("_pendingClosing", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);

    private static FinTsPinTanDialogueEndRequestBinding ClosingCandidate(JsonElement element, FinTsPinTanSynchronizationEvidence syncEvidence)
    {
        var unsignedClosing = FinTsUnsignedDialogueEndRequest.Parse(
            FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("requestBase64").GetString()!)),
            element.GetProperty("expectedBankMessageNumber").GetInt32());
        var closingHeader = FinTsPinTanSignatureHeader.Parse(
            FinTsSyntax.ParseSegments(Convert.FromBase64String(element.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var closingContext = FinTsPinTanDialogueEndContext.Evaluate(syncEvidence, unsignedClosing, closingHeader);
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(closingContext);
    }

    private static FinTsResponse Response(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(
            element.TryGetProperty("wireBase64", out var wireElem)
                ? wireElem.GetString()!
                : element.GetProperty("responseBase64").GetString()!));
        return element.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
    }

    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal long Frequency { get; set; } = 1000;
        internal bool ThrowFrequency { get; set; }
        internal bool ThrowTimestamp { get; set; }
        public override long TimestampFrequency => ThrowFrequency ? throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") : Frequency;
        public override long GetTimestamp() => ThrowTimestamp ? throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") : Milliseconds;
    }
}
