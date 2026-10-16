using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSynchronizationSessionDiagnosticTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }

        using var sessionsDoc = JsonDocument.Parse(
            Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        var vectors = sessionsDoc.RootElement.GetProperty("vectors");

        var cleanSyncVector = VectorNamed(vectors, "clean-assignment-session");
        var abortSyncVector = VectorNamed(vectors, "bank-abort-on-synchronization");
        var reviewSyncVector = VectorNamed(vectors, "scoped-review-on-synchronization");
        var abortCloseVector = VectorNamed(vectors, "bank-abort-on-closing");
        var reviewCloseVector = VectorNamed(vectors, "scoped-review-on-closing");
        var foreignSyncVector = VectorNamed(vectors, "foreign-scope-on-sync-keeps-candidate");
        var foreignCloseVector = VectorNamed(vectors, "foreign-scope-on-closing-keeps-candidate");

        var foreignCloseResponse = Response(foreignCloseVector.GetProperty("extraResponses")[0]);

        var escapedSyncVector = VectorNamed(vectors, "escaped-system-dialogue-session");
        var escapedSyncCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
            FinTsPinTanSignatureTrailerTests.Evidence(escapedSyncVector.GetProperty("syncCandidate")));
        var escapedSyncResp = Response(escapedSyncVector.GetProperty("syncResponse"));
        var escapedSyncEvidence = FinTsPinTanSynchronizationEvidence.Evaluate(
            FinTsPinTanResponseBinding.Evaluate(escapedSyncCand, escapedSyncResp),
            FinTsSynchronizationDataSet.Parse(escapedSyncResp),
            null);

        // 1. Bank abort on synchronization (9800)
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(abortSyncVector);
            Verify(attempt.StartSynchronization(syncCand) == FinTsPinTanSynchronizationSessionTransition.CandidateRecorded,
                "StartSynchronization transitions to CandidateRecorded.");
            Verify(attempt.GetSnapshot().RequestsRecorded == 1 && attempt.GetSnapshot().ResponsesHandled == 0 &&
                attempt.GetSnapshot().ClientMessageNumber == 1 && attempt.GetSnapshot().BankMessageNumber == 0,
                "AwaitingSynchronizationResponse has recorded 1 request, handled 0, client 1, bank 0.");

            var abortData = FinTsSynchronizationDataSet.Parse(Response(abortSyncVector.GetProperty("syncResponse")));
            var abortResult = attempt.AcceptSynchronizationResponse(abortData);
            Verify(abortResult.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported &&
                !abortResult.HasMatchingEvidence && abortResult.SynchronizationEvidence is not null,
                "Abort on synchronization produces AbortReported with HasMatchingEvidence false.");

            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanSynchronizationSessionState.Aborted &&
                snap.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                snap.RequestsRecorded == 1 && snap.ResponsesHandled == 1 &&
                snap.ClientMessageNumber == 1 && snap.BankMessageNumber == 1 &&
                snap.Issues.HasFlag(FinTsPinTanSynchronizationSessionIssue.AbortReported),
                "Synchronization abort sets ResponsesHandled to 1, BankMessageNumber to 1, and stage to Terminal.");
            Verify(abortResult.SynchronizationEvidence?.ReportedDialogueId is not null, "Synchronization abort hands out ReportedDialogueId in synchronization evidence.");
            Verify(attempt.DialogueId is null, "Synchronization abort releases session DialogueId on terminal transition.");

            // Terminal replay
            var replay = attempt.AcceptSynchronizationResponse(abortData);
            Verify(replay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().BankMessageNumber == 1,
                "Terminal replay after synchronization abort preserves ResponsesHandled == 1 and BankMessageNumber == 1.");
        }

        // 2. Scoped review on synchronization (!HasMatchingEvidence)
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(reviewSyncVector);
            attempt.StartSynchronization(syncCand);

            var reviewData = FinTsSynchronizationDataSet.Parse(Response(reviewSyncVector.GetProperty("syncResponse")));
            var reviewResult = attempt.AcceptSynchronizationResponse(reviewData);
            Verify(reviewResult.Transition == FinTsPinTanSynchronizationSessionTransition.RejectedForReview &&
                !reviewResult.HasMatchingEvidence && reviewResult.SynchronizationEvidence is not null,
                "Review on synchronization produces RejectedForReview with HasMatchingEvidence false.");

            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanSynchronizationSessionState.NeedsReview &&
                snap.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                snap.RequestsRecorded == 1 && snap.ResponsesHandled == 1 &&
                snap.ClientMessageNumber == 1 && snap.BankMessageNumber == 1 &&
                snap.Issues.HasFlag(FinTsPinTanSynchronizationSessionIssue.SynchronizationNeedsReview),
                "Synchronization review sets ResponsesHandled to 1, BankMessageNumber to 1, and state to NeedsReview.");
            Verify(reviewResult.SynchronizationEvidence?.ReportedDialogueId is not null, "Synchronization review hands out ReportedDialogueId in synchronization evidence.");
            Verify(attempt.DialogueId is null, "Synchronization review releases session DialogueId on terminal transition.");

            // Terminal replay
            var replay = attempt.AcceptSynchronizationResponse(reviewData);
            Verify(replay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                attempt.GetSnapshot().ResponsesHandled == 1,
                "Terminal replay after synchronization review preserves ResponsesHandled == 1.");
        }

        // 3. Bank abort on closing (9800)
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(abortCloseVector);
            attempt.StartSynchronization(syncCand);
            var syncResult = attempt.AcceptSynchronizationResponse(
                FinTsSynchronizationDataSet.Parse(Response(abortCloseVector.GetProperty("syncResponse"))));
            Verify(syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.CloseRequired &&
                syncResult.HasMatchingEvidence,
                "Clean synchronization produces CloseRequired with HasMatchingEvidence true.");

            var closeCand = ClosingCandidate(abortCloseVector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
            Verify(attempt.RecordClosingCandidate(closeCand) == FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded,
                "RecordClosingCandidate produces CloseCandidateRecorded.");
            Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1 &&
                attempt.GetSnapshot().ClientMessageNumber == 2 && attempt.GetSnapshot().BankMessageNumber == 1,
                "AwaitingCloseResponse has requests=2, handled=1, client=2, bank=1.");

            // Foreign closing response preserves handled count and message counters
            var foreignResult = attempt.AcceptClosingResponse(foreignCloseResponse);
            Verify(foreignResult.Transition == FinTsPinTanSynchronizationSessionTransition.ContextMismatch &&
                attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().BankMessageNumber == 1,
                "Foreign closing response preserves ResponsesHandled == 1 and BankMessageNumber == 1.");
            Verify(attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanSynchronizationSessionIssue.ClosingBindingNeedsReview),
                "Foreign closing response marks ClosingBindingNeedsReview issue.");

            // Bank abort response
            var abortCloseResp = Response(abortCloseVector.GetProperty("closingResponse"));
            var abortCloseResult = attempt.AcceptClosingResponse(abortCloseResp);
            Verify(abortCloseResult.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported &&
                !abortCloseResult.HasMatchingEvidence && abortCloseResult.ClosingEvidence is not null &&
                abortCloseResult.SynchronizationEvidence is not null,
                "Closing abort produces AbortReported with HasMatchingEvidence false and both evidences.");

            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanSynchronizationSessionState.Aborted &&
                snap.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                snap.RequestsRecorded == 2 && snap.ResponsesHandled == 2 &&
                snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                snap.Issues == FinTsPinTanSynchronizationSessionIssue.AbortReported,
                "Closing abort sets ResponsesHandled to 2, BankMessageNumber to 2, and clears foreign ClosingBindingNeedsReview.");

            // Terminal replay
            var replay = attempt.AcceptClosingResponse(abortCloseResp);
            Verify(replay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                attempt.GetSnapshot().ResponsesHandled == 2 && attempt.GetSnapshot().BankMessageNumber == 2,
                "Terminal replay after closing abort preserves ResponsesHandled == 2 and BankMessageNumber == 2.");
        }

        // 4. Scoped review on closing (ClosingStatusNeedsReview)
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(reviewCloseVector);
            attempt.StartSynchronization(syncCand);
            var syncResult = attempt.AcceptSynchronizationResponse(
                FinTsSynchronizationDataSet.Parse(Response(reviewCloseVector.GetProperty("syncResponse"))));

            var closeCand = ClosingCandidate(reviewCloseVector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
            attempt.RecordClosingCandidate(closeCand);

            var reviewCloseResp = Response(reviewCloseVector.GetProperty("closingResponse"));
            var reviewCloseResult = attempt.AcceptClosingResponse(reviewCloseResp);
            Verify(reviewCloseResult.Transition == FinTsPinTanSynchronizationSessionTransition.RejectedForReview &&
                !reviewCloseResult.HasMatchingEvidence && reviewCloseResult.ClosingEvidence is not null &&
                reviewCloseResult.SynchronizationEvidence is not null,
                "Closing review produces RejectedForReview with HasMatchingEvidence false and both evidences.");

            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanSynchronizationSessionState.NeedsReview &&
                snap.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                snap.RequestsRecorded == 2 && snap.ResponsesHandled == 2 &&
                snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                snap.Issues == FinTsPinTanSynchronizationSessionIssue.ClosingStatusNeedsReview,
                "Closing review sets ResponsesHandled to 2, BankMessageNumber to 2, and state to NeedsReview.");

            // Terminal replay
            var replay = attempt.AcceptClosingResponse(reviewCloseResp);
            Verify(replay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                attempt.GetSnapshot().ResponsesHandled == 2,
                "Terminal replay after closing review preserves ResponsesHandled == 2.");
        }

        // 5. Clean closing (ClosureReported -> ReinitializationRequired)
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(cleanSyncVector);
            attempt.StartSynchronization(syncCand);
            var syncResult = attempt.AcceptSynchronizationResponse(
                FinTsSynchronizationDataSet.Parse(Response(cleanSyncVector.GetProperty("syncResponse"))));
            Verify(attempt.DialogueId is not null, "Clean synchronization pins DialogueId during active AwaitingCloseRequest stage.");

            var closeCand = ClosingCandidate(cleanSyncVector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
            attempt.RecordClosingCandidate(closeCand);

            // Foreign response creates transient error
            attempt.AcceptClosingResponse(foreignCloseResponse);
            Verify(attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanSynchronizationSessionIssue.ClosingBindingNeedsReview),
                "Transient foreign response sets ClosingBindingNeedsReview.");

            // Clean closing response clears transient foreign error
            var cleanCloseResp = Response(cleanSyncVector.GetProperty("closingResponse"));
            var cleanCloseResult = attempt.AcceptClosingResponse(cleanCloseResp);
            Verify(cleanCloseResult.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired &&
                cleanCloseResult.HasMatchingEvidence && cleanCloseResult.ClosingEvidence is not null &&
                cleanCloseResult.SynchronizationEvidence is not null,
                "Clean closing produces ReinitializationRequired with HasMatchingEvidence true.");

            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanSynchronizationSessionState.ReinitializationRequired &&
                snap.Stage == FinTsPinTanSynchronizationSessionStage.Terminal &&
                snap.RequestsRecorded == 2 && snap.ResponsesHandled == 2 &&
                snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                snap.Issues == FinTsPinTanSynchronizationSessionIssue.None,
                "Clean closing sets ResponsesHandled to 2, BankMessageNumber to 2, and clears transient Issues to None.");
            Verify(attempt.DialogueId is null, "Clean closing releases session DialogueId on terminal transition.");

            // Terminal replay
            var replay = attempt.AcceptClosingResponse(cleanCloseResp);
            Verify(replay.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal &&
                attempt.GetSnapshot().ResponsesHandled == 2,
                "Terminal replay after clean closing preserves ResponsesHandled == 2.");
        }

        // 6. Transient Candidate Retry: Error clearing upon accepted closing candidate
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(cleanSyncVector);
            attempt.StartSynchronization(syncCand);
            var syncResult = attempt.AcceptSynchronizationResponse(
                FinTsSynchronizationDataSet.Parse(Response(cleanSyncVector.GetProperty("syncResponse"))));

            var validCloseCand = ClosingCandidate(cleanSyncVector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);

            // (a) Dialogue ID mismatch candidate
            var mismatchDialogueVector = VectorNamed(vectors, "closing-candidate-dialogue-mismatch");
            var mismatchDialogueCand = ClosingCandidate(mismatchDialogueVector.GetProperty("extraResponses")[0], escapedSyncEvidence);
            var mismatchRes = attempt.RecordClosingCandidate(mismatchDialogueCand);
            Verify(mismatchRes == FinTsPinTanSynchronizationSessionTransition.ContextMismatch,
                "Mismatched dialogue candidate returns ContextMismatch.");
            Verify(attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanSynchronizationSessionIssue.DialogueIdMismatch),
                "Issues contains DialogueIdMismatch after mismatch candidate.");

            // (b) Recording valid candidate clears transient DialogueIdMismatch
            var recordedRes = attempt.RecordClosingCandidate(validCloseCand);
            Verify(recordedRes == FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded,
                "Valid candidate records CloseCandidateRecorded.");
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanSynchronizationSessionIssue.None,
                "Accepted closing candidate clears transient candidate errors to None.");
            Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ClientMessageNumber == 2,
                "RequestsRecorded is 2, ClientMessageNumber is 2.");
        }

        // 7. Concurrency Atomicity across Review and Abort (16 parallel threads)
        {
            // (a) Concurrent synchronization abort
            {
                using var attempt = New();
                attempt.StartSynchronization(MakeSyncCand(abortSyncVector));
                var abortData = FinTsSynchronizationDataSet.Parse(Response(abortSyncVector.GetProperty("syncResponse")));

                var results = new FinTsPinTanSynchronizationSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptSynchronizationResponse(abortData);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported) == 1,
                    "Concurrent sync abort: exactly one thread gets AbortReported.");
                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal) == 15,
                    "Concurrent sync abort: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Aborted,
                    "Concurrent sync abort: ResponsesHandled incremented to exactly 1.");
            }

            // (b) Concurrent synchronization review
            {
                using var attempt = New();
                attempt.StartSynchronization(MakeSyncCand(reviewSyncVector));
                var reviewData = FinTsSynchronizationDataSet.Parse(Response(reviewSyncVector.GetProperty("syncResponse")));

                var results = new FinTsPinTanSynchronizationSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptSynchronizationResponse(reviewData);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.RejectedForReview) == 1,
                    "Concurrent sync review: exactly one thread gets RejectedForReview.");
                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal) == 15,
                    "Concurrent sync review: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.NeedsReview,
                    "Concurrent sync review: ResponsesHandled incremented to exactly 1.");
            }

            // (c) Concurrent closing abort
            {
                using var attempt = New();
                attempt.StartSynchronization(MakeSyncCand(abortCloseVector));
                var syncRes = attempt.AcceptSynchronizationResponse(
                    FinTsSynchronizationDataSet.Parse(Response(abortCloseVector.GetProperty("syncResponse"))));
                attempt.RecordClosingCandidate(ClosingCandidate(abortCloseVector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

                var closeResp = Response(abortCloseVector.GetProperty("closingResponse"));
                var results = new FinTsPinTanSynchronizationSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptClosingResponse(closeResp);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported) == 1,
                    "Concurrent closing abort: exactly one thread gets AbortReported.");
                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal) == 15,
                    "Concurrent closing abort: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2 && attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.Aborted,
                    "Concurrent closing abort: ResponsesHandled incremented to exactly 2.");
            }

            // (d) Concurrent closing review
            {
                using var attempt = New();
                attempt.StartSynchronization(MakeSyncCand(reviewCloseVector));
                var syncRes = attempt.AcceptSynchronizationResponse(
                    FinTsSynchronizationDataSet.Parse(Response(reviewCloseVector.GetProperty("syncResponse"))));
                attempt.RecordClosingCandidate(ClosingCandidate(reviewCloseVector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

                var closeResp = Response(reviewCloseVector.GetProperty("closingResponse"));
                var results = new FinTsPinTanSynchronizationSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptClosingResponse(closeResp);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.RejectedForReview) == 1,
                    "Concurrent closing review: exactly one thread gets RejectedForReview.");
                Verify(results.Count(r => r.Transition == FinTsPinTanSynchronizationSessionTransition.Terminal) == 15,
                    "Concurrent closing review: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2 && attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.NeedsReview,
                    "Concurrent closing review: ResponsesHandled incremented to exactly 2.");
            }
        }

        // 8. Progress Monotonicity Invariant: ResponsesHandled <= RequestsRecorded across all 24 vectors
        foreach (var vector in vectors.EnumerateArray())
        {
            using var attempt = New();
            var syncCand = MakeSyncCand(vector);
            FinTsSynchronizationRecoveryContext? recovery = null;
            if (vector.GetProperty("recovery").ValueKind != JsonValueKind.Null)
            {
                var recElem = vector.GetProperty("recovery");
                recovery = new FinTsSynchronizationRecoveryContext(
                    recElem.GetProperty("previousDialogueId").GetString()!,
                    recElem.GetProperty("lastSubmittedMessageNumber").GetInt32());
            }
            attempt.StartSynchronization(syncCand, recovery);
            var snap = attempt.GetSnapshot();
            Verify(snap.ResponsesHandled <= snap.RequestsRecorded,
                $"{vector.GetProperty("name").GetString()}: Monotonicity holds after start.");
        }

        Console.WriteLine($"FinTS synchronization session counter tracking and diagnostic lifecycle passed ({count} checks).");
    }

    private static FinTsPinTanSynchronizationSessionAttempt New() =>
        new(TimeSpan.FromSeconds(10), new Clock());

    private static JsonElement VectorNamed(JsonElement vectors, string name)
    {
        foreach (var v in vectors.EnumerateArray())
        {
            if (v.GetProperty("name").GetString() == name)
                return v;
        }
        throw new InvalidOperationException($"Vector named '{name}' not found.");
    }

    private static FinTsPinTanRequestBinding MakeSyncCand(JsonElement vector)
    {
        var syncCandElem = vector.GetProperty("syncCandidate");
        try
        {
            return FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                FinTsPinTanSignatureTrailerTests.Evidence(syncCandElem));
        }
        catch (FinTsFormatException)
        {
            // Fallback for traces testing malformed syncCandidate
            var standardSyncSample = vector.GetProperty("syncCandidate");
            return FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                FinTsPinTanSignatureTrailerTests.Evidence(standardSyncSample));
        }
    }

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
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 1000;
    }
}
