using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionAccountingTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }

        using var basicDoc = Fixture("pin-tan-initialization-evidence-v1.json");
        using var proceduresDoc = Fixture("pin-tan-initialization-procedures-v1.json");
        using var requirementsDoc = Fixture("pin-tan-initialization-requirements-v1.json");
        using var sessionsDoc = Fixture("pin-tan-read-session-attempt-v1.json");
        using var continuationsDoc = Fixture("pin-tan-read-continuation-semantics-v1.json");

        var sessionVectors = sessionsDoc.RootElement.GetProperty("vectors");
        var continuationVectors = continuationsDoc.RootElement.GetProperty("vectors");
        var foreignResponse = AllAccountResponse(VectorNamed(sessionVectors, "foreign-operation-response-keeps-state").GetProperty("extraResponses")[0]);

        // Helpers for initialization inputs across 4 overloads
        var validInit = new[]
        {
            ParseInit(basicDoc.RootElement.GetProperty("vectors")[0]),
            ParseInit(proceduresDoc.RootElement.GetProperty("vectors")[0]),
            ParseInit(requirementsDoc.RootElement.GetProperty("vectors")[0]),
            ParseInit(VectorNamed(sessionVectors, "clean-all-account-discovery-session").GetProperty("initialization")),
        };
        var reviewInit = new[]
        {
            ParseInit(basicDoc.RootElement.GetProperty("vectors")[4]),
            ParseInit(proceduresDoc.RootElement.GetProperty("vectors")[5]),
            ParseInit(requirementsDoc.RootElement.GetProperty("vectors")[7]),
            ParseInit(requirementsDoc.RootElement.GetProperty("vectors")[7]),
        };

        // 1. Initialization Response Accounting across 4 overloads
        for (int mode = 0; mode < 4; mode++)
        {
            // (a) Matching initialization
            {
                var input = validInit[mode];
                using var attempt = New();
                Verify(attempt.GetSnapshot().ResponsesHandled == 0 && attempt.GetSnapshot().RequestsRecorded == 0,
                    $"Mode {mode}: Ready attempt starts with zero responses handled.");

                attempt.StartInitialization(input.Binding);
                Verify(attempt.GetSnapshot().ResponsesHandled == 0 && attempt.GetSnapshot().RequestsRecorded == 1,
                    $"Mode {mode}: Start records 1 request with 0 responses handled.");

                // Foreign response preserves accounting
                var foreignResult = AcceptInit(attempt, mode, foreignResponse.Source);
                Verify(foreignResult.Transition == FinTsPinTanReadSessionTransition.ContextMismatch &&
                    attempt.GetSnapshot().ResponsesHandled == 0 && attempt.GetSnapshot().BankMessageNumber == 0,
                    $"Mode {mode}: Foreign response does not advance handled responses or bank counter.");

                // Valid response opens dialogue
                var validResult = AcceptInit(attempt, mode, input);
                Verify(validResult.Transition == FinTsPinTanReadSessionTransition.DialogueOpen && validResult.HasMatchingEvidence,
                    $"Mode {mode}: Valid response opens dialogue.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 1 && snap.RequestsRecorded == 1 &&
                    snap.ClientMessageNumber == 1 && snap.BankMessageNumber == 1 &&
                    snap.State == FinTsPinTanReadSessionState.DialogueOpen && snap.Stage == FinTsPinTanReadSessionStage.Operation,
                    $"Mode {mode}: Matching initialization advances ResponsesHandled to 1 with sequential counters.");

                // Re-submitting in DialogueOpen returns WrongState without changing handled
                var wrongState = AcceptInit(attempt, mode, input);
                Verify(wrongState.Transition == FinTsPinTanReadSessionTransition.WrongState &&
                    attempt.GetSnapshot().ResponsesHandled == 1,
                    $"Mode {mode}: WrongState call does not change handled responses.");
            }

            // (b) Review initialization
            {
                var input = reviewInit[mode];
                using var attempt = New();
                attempt.StartInitialization(input.Binding);

                // Foreign response
                var foreignResult = AcceptInit(attempt, mode, foreignResponse.Source);
                Verify(foreignResult.Transition == FinTsPinTanReadSessionTransition.ContextMismatch &&
                    attempt.GetSnapshot().ResponsesHandled == 0,
                    $"Mode {mode} (review): Foreign response preserves zero handled count.");

                // Review response
                var reviewResult = AcceptInit(attempt, mode, input);
                Verify(reviewResult.Transition == FinTsPinTanReadSessionTransition.RejectedForReview && !reviewResult.HasMatchingEvidence,
                    $"Mode {mode}: Review response returns RejectedForReview.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 1 && snap.RequestsRecorded == 1 &&
                    snap.ClientMessageNumber == 1 && snap.BankMessageNumber == 1 &&
                    snap.State == FinTsPinTanReadSessionState.NeedsReview && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    $"Mode {mode}: Review initialization advances ResponsesHandled to 1 in terminal NeedsReview state.");

                // Re-submitting in terminal state returns Terminal without advancing handled
                var replayResult = AcceptInit(attempt, mode, input);
                Verify(replayResult.Transition == FinTsPinTanReadSessionTransition.Terminal &&
                    attempt.GetSnapshot().ResponsesHandled == 1,
                    $"Mode {mode}: Terminal replay does not change ResponsesHandled == 1.");

                // Disposing attempt preserves scalar snapshot
                attempt.Dispose();
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().State == FinTsPinTanReadSessionState.NeedsReview,
                    $"Mode {mode}: Disposed review attempt retains ResponsesHandled == 1.");
            }
        }

        // 2. All-Account Discovery Response Accounting
        {
            var vector = VectorNamed(sessionVectors, "session-all-account-review");
            var reviewAccountVector = vector.GetProperty("allAccount");
            var cleanSession = VectorNamed(sessionVectors, "clean-all-account-discovery-session");
            var cleanAccountVector = cleanSession.GetProperty("allAccount");

            // (a) Clean all-account discovery
            using (var attempt = OpenSession(cleanSession.GetProperty("initialization")))
            {
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().RequestsRecorded == 1,
                    "DialogueOpen starts with 1 request recorded and 1 response handled.");

                var candidate = Bind(AllAccountCandidate(cleanAccountVector), attempt);
                Verify(attempt.RecordAllAccountCandidate(candidate) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                    "Records all-account candidate.");
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1,
                    "Recording operational candidate advances RequestsRecorded to 2 while ResponsesHandled remains 1.");

                // Foreign response
                Verify(attempt.AcceptAllAccountResponse(foreignResponse).Transition == FinTsPinTanReadSessionTransition.ContextMismatch,
                    "Foreign all-account response produces ContextMismatch.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().BankMessageNumber == 1,
                    "Foreign all-account response preserves ResponsesHandled == 1 and BankMessageNumber == 1.");

                // Clean response
                var resp = AllAccountResponse(cleanAccountVector);
                var result = attempt.AcceptAllAccountResponse(resp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.ExecutionReported,
                    "Clean all-account response reports execution.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 2 && snap.RequestsRecorded == 2 &&
                    snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                    snap.State == FinTsPinTanReadSessionState.OperationCompleted && snap.Stage == FinTsPinTanReadSessionStage.Closing,
                    "Execution reported advances ResponsesHandled to 2 and bank counter to 2.");

                // Response replay rejected
                Verify(attempt.AcceptAllAccountResponse(resp).Transition == FinTsPinTanReadSessionTransition.ReplayRejected,
                    "Replayed all-account response produces ReplayRejected.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2,
                    "ReplayRejected does not advance ResponsesHandled.");
            }

            // (b) Review all-account discovery
            using (var attempt = OpenSession(vector.GetProperty("initialization")))
            {
                var candidate = Bind(AllAccountCandidate(reviewAccountVector), attempt);
                attempt.RecordAllAccountCandidate(candidate);
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1,
                    "Candidate recorded: 2 requests, 1 response handled.");

                var resp = AllAccountResponse(reviewAccountVector);
                var result = attempt.AcceptAllAccountResponse(resp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.RejectedForReview && !result.HasMatchingEvidence,
                    "Review all-account response returns RejectedForReview.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 2 && snap.RequestsRecorded == 2 &&
                    snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                    snap.State == FinTsPinTanReadSessionState.NeedsReview && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    "All-account review advances ResponsesHandled to 2 and BankMessageNumber to 2 in NeedsReview state.");

                // Terminal replay
                Verify(attempt.AcceptAllAccountResponse(resp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                    "Terminal replay produces Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2,
                    "Terminal replay preserves ResponsesHandled == 2.");
            }
        }

        // 3. Balance First-Read Response Accounting
        {
            var vector = VectorNamed(sessionVectors, "session-balance-read-review");
            var reviewBalVector = vector.GetProperty("balanceInitial");
            var cleanSession = VectorNamed(sessionVectors, "clean-balance-refresh-single-page-session");
            var cleanBalVector = cleanSession.GetProperty("balanceInitial");

            // (a) Clean single-page balance read
            using (var attempt = OpenSession(cleanSession.GetProperty("initialization")))
            {
                var candidate = Bind(BalanceFirstReadCandidate(cleanBalVector), attempt);
                attempt.RecordBalanceFirstReadCandidate(candidate);
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1,
                    "Balance candidate recorded: requests=2, handled=1.");

                // Foreign response
                Verify(attempt.AcceptBalanceResponse(foreignResponse).Transition == FinTsPinTanReadSessionTransition.ContextMismatch,
                    "Foreign balance response produces ContextMismatch.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1, "Foreign response does not increment handled.");

                // Clean response
                var resp = BalanceResponse(cleanBalVector);
                var result = attempt.AcceptBalanceResponse(resp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.ExecutionReported,
                    "Clean balance response reports execution.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 2 && snap.RequestsRecorded == 2 &&
                    snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                    snap.PagesAccepted == 1 && snap.BalancesReported == 1 &&
                    snap.State == FinTsPinTanReadSessionState.OperationCompleted,
                    "Clean balance response advances ResponsesHandled to 2 and BankMessageNumber to 2.");

                // Replay
                Verify(attempt.AcceptBalanceResponse(resp).Transition == FinTsPinTanReadSessionTransition.ReplayRejected,
                    "Duplicate balance response returns ReplayRejected.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2,
                    "Replay does not increment ResponsesHandled.");
            }

            // (b) Review balance first read
            using (var attempt = OpenSession(vector.GetProperty("initialization")))
            {
                var candidate = Bind(BalanceFirstReadCandidate(reviewBalVector), attempt);
                attempt.RecordBalanceFirstReadCandidate(candidate);
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1,
                    "Review balance candidate recorded: requests=2, handled=1.");

                var resp = BalanceResponse(reviewBalVector);
                var result = attempt.AcceptBalanceResponse(resp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.RejectedForReview && !result.HasMatchingEvidence,
                    "Balance response returns RejectedForReview.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 2 && snap.RequestsRecorded == 2 &&
                    snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                    snap.PagesAccepted == 0 && snap.BalancesReported == 0 &&
                    snap.State == FinTsPinTanReadSessionState.NeedsReview && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    "Balance review advances ResponsesHandled to 2 and BankMessageNumber to 2 in terminal NeedsReview.");

                // Terminal replay
                Verify(attempt.AcceptBalanceResponse(resp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                    "Terminal replay produces Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2,
                    "Terminal replay preserves ResponsesHandled == 2.");
            }
        }

        // 4. Balance Continuation Response Accounting
        {
            var continuationReviewVector = continuationVectors[17]; // review continuation
            var initialPageVector = continuationReviewVector.GetProperty("candidate").GetProperty("context").GetProperty("continuation").GetProperty("previous");

            using var attempt = OpenSession(initialPageVector.GetProperty("candidate").GetProperty("context").GetProperty("initialization"));
            attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(initialPageVector), attempt));
            var initialData = BalanceResponse(initialPageVector);
            var firstResult = attempt.AcceptBalanceResponse(initialData);
            Verify(firstResult.Transition == FinTsPinTanReadSessionTransition.PartialAccepted,
                "First page accepted as partial.");
            Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 2 &&
                attempt.GetSnapshot().PagesAccepted == 1 && attempt.GetSnapshot().BankMessageNumber == 2,
                "Partial page 1: requests=2, handled=2, bank=2.");

            // Record continuation candidate
            var contCandidate = Bind(BalanceContinuationCandidate(continuationReviewVector), attempt);
            Verify(attempt.RecordBalanceContinuationCandidate(contCandidate) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Continuation candidate recorded.");
            Verify(attempt.GetSnapshot().RequestsRecorded == 3 && attempt.GetSnapshot().ResponsesHandled == 2,
                "Continuation candidate recorded: requests=3, handled=2.");

            // Foreign response during continuation
            Verify(attempt.AcceptBalanceResponse(foreignResponse).Transition == FinTsPinTanReadSessionTransition.ContextMismatch,
                "Foreign response during continuation returns ContextMismatch.");
            Verify(attempt.GetSnapshot().ResponsesHandled == 2 && attempt.GetSnapshot().BankMessageNumber == 2,
                "Foreign response preserves handled=2 and bank=2.");

            // Review continuation response
            var contResp = BalanceResponse(continuationReviewVector);
            var contResult = attempt.AcceptBalanceResponse(contResp);
            Verify(contResult.Transition == FinTsPinTanReadSessionTransition.RejectedForReview && !contResult.HasMatchingEvidence,
                "Continuation review response returns RejectedForReview.");
            var snap = attempt.GetSnapshot();
            Verify(snap.ResponsesHandled == 3 && snap.RequestsRecorded == 3 &&
                snap.ClientMessageNumber == 3 && snap.BankMessageNumber == 3 &&
                snap.State == FinTsPinTanReadSessionState.NeedsReview && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                "Continuation review advances ResponsesHandled to 3 and BankMessageNumber to 3 in terminal NeedsReview.");

            // Terminal replay
            Verify(attempt.AcceptBalanceResponse(contResp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                "Terminal replay produces Terminal.");
            Verify(attempt.GetSnapshot().ResponsesHandled == 3,
                "Terminal replay preserves ResponsesHandled == 3.");
        }

        // 5. Closing Response Accounting: Clean (0100), Abort (9800), and Review (9050)
        {
            var cleanCloseSession = VectorNamed(sessionVectors, "clean-session-closing-without-operation");
            // (a) Clean closure
            using (var attempt = OpenSession(cleanCloseSession.GetProperty("initialization")))
            {
                var closing = ClosingCandidate(cleanCloseSession.GetProperty("closing"), attempt.InitializationEvidence!);
                Verify(attempt.RecordClosingCandidate(Bind(closing, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                    "Records closing candidate directly after init.");
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 1,
                    "Closing candidate recorded: requests=2, handled=1.");

                // Foreign closing response
                Verify(attempt.AcceptClosingResponse(foreignResponse.Source).Transition == FinTsPinTanReadSessionTransition.ContextMismatch,
                    "Foreign closing response produces ContextMismatch.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().BankMessageNumber == 1,
                    "Foreign closing response preserves handled=1, bank=1.");

                var closeResp = ClosingResponse(cleanCloseSession.GetProperty("closing"));
                var result = attempt.AcceptClosingResponse(closeResp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.Closed && result.ClosingEvidence is not null,
                    "Clean closing reports Closed.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 2 && snap.RequestsRecorded == 2 &&
                    snap.ClientMessageNumber == 2 && snap.BankMessageNumber == 2 &&
                    snap.State == FinTsPinTanReadSessionState.Closed && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    "Clean closure advances ResponsesHandled to 2 and bank counter to 2.");

                // Terminal replay
                Verify(attempt.AcceptClosingResponse(closeResp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                    "Closed terminal replay returns Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2,
                    "Closed terminal replay preserves ResponsesHandled == 2.");
            }

            // (b) Bank abort on closing
            var abortSession = VectorNamed(sessionVectors, "session-bank-abort-on-close");
            using (var attempt = OpenSession(abortSession.GetProperty("initialization")))
            {
                // Has allAccount operation, so closing is message 3
                attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(abortSession.GetProperty("allAccount")), attempt));
                attempt.AcceptAllAccountResponse(AllAccountResponse(abortSession.GetProperty("allAccount")));
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 2,
                    "Operation completed: requests=2, handled=2.");

                var closing = ClosingCandidate(abortSession.GetProperty("closing"), attempt.InitializationEvidence!);
                attempt.RecordClosingCandidate(Bind(closing, attempt));
                Verify(attempt.GetSnapshot().RequestsRecorded == 3 && attempt.GetSnapshot().ResponsesHandled == 2,
                    "Closing recorded: requests=3, handled=2.");

                var closeResp = ClosingResponse(abortSession.GetProperty("closing"));
                var result = attempt.AcceptClosingResponse(closeResp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.AbortReported && result.ClosingEvidence is not null,
                    "Abort closing reports AbortReported.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 3 && snap.RequestsRecorded == 3 &&
                    snap.ClientMessageNumber == 3 && snap.BankMessageNumber == 3 &&
                    snap.State == FinTsPinTanReadSessionState.Aborted && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    "Abort reported advances ResponsesHandled to 3 and bank counter to 3.");

                // Terminal replay
                Verify(attempt.AcceptClosingResponse(closeResp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                    "Aborted terminal replay returns Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 3,
                    "Aborted terminal replay preserves ResponsesHandled == 3.");
            }

            // (c) Review on closing
            var reviewCloseSession = VectorNamed(sessionVectors, "session-closing-review");
            using (var attempt = OpenSession(reviewCloseSession.GetProperty("initialization")))
            {
                attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(reviewCloseSession.GetProperty("allAccount")), attempt));
                attempt.AcceptAllAccountResponse(AllAccountResponse(reviewCloseSession.GetProperty("allAccount")));
                Verify(attempt.GetSnapshot().RequestsRecorded == 2 && attempt.GetSnapshot().ResponsesHandled == 2,
                    "Operation completed: requests=2, handled=2.");

                var closing = ClosingCandidate(reviewCloseSession.GetProperty("closing"), attempt.InitializationEvidence!);
                attempt.RecordClosingCandidate(Bind(closing, attempt));
                Verify(attempt.GetSnapshot().RequestsRecorded == 3 && attempt.GetSnapshot().ResponsesHandled == 2,
                    "Closing recorded: requests=3, handled=2.");

                var closeResp = ClosingResponse(reviewCloseSession.GetProperty("closing"));
                var result = attempt.AcceptClosingResponse(closeResp);
                Verify(result.Transition == FinTsPinTanReadSessionTransition.RejectedForReview && result.ClosingEvidence is not null,
                    "Review closing reports RejectedForReview.");
                var snap = attempt.GetSnapshot();
                Verify(snap.ResponsesHandled == 3 && snap.RequestsRecorded == 3 &&
                    snap.ClientMessageNumber == 3 && snap.BankMessageNumber == 3 &&
                    snap.State == FinTsPinTanReadSessionState.NeedsReview && snap.Stage == FinTsPinTanReadSessionStage.Terminal,
                    "Review closing advances ResponsesHandled to 3 and bank counter to 3.");

                // Terminal replay
                Verify(attempt.AcceptClosingResponse(closeResp).Transition == FinTsPinTanReadSessionTransition.Terminal,
                    "NeedsReview terminal replay returns Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 3,
                    "NeedsReview terminal replay preserves ResponsesHandled == 3.");
            }
        }

        // 6. Concurrent Review Submissions Atomicity
        {
            // (a) Concurrent initialization review
            {
                var input = reviewInit[3];
                using var attempt = New();
                attempt.StartInitialization(input.Binding);

                var results = new FinTsPinTanReadSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = AcceptInit(attempt, 3, input);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.RejectedForReview) == 1,
                    "Concurrent initialization review: exactly one thread gets RejectedForReview.");
                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.Terminal) == 15,
                    "Concurrent initialization review: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 1 && attempt.GetSnapshot().State == FinTsPinTanReadSessionState.NeedsReview,
                    "Concurrent initialization review: ResponsesHandled incremented to exactly 1.");
            }

            // (b) Concurrent operational review (all-account)
            {
                var vector = VectorNamed(sessionVectors, "session-all-account-review");
                using var attempt = OpenSession(vector.GetProperty("initialization"));
                attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(vector.GetProperty("allAccount")), attempt));

                var resp = AllAccountResponse(vector.GetProperty("allAccount"));
                var results = new FinTsPinTanReadSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptAllAccountResponse(resp);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.RejectedForReview) == 1,
                    "Concurrent all-account review: exactly one thread gets RejectedForReview.");
                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.Terminal) == 15,
                    "Concurrent all-account review: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 2 && attempt.GetSnapshot().State == FinTsPinTanReadSessionState.NeedsReview,
                    "Concurrent all-account review: ResponsesHandled incremented to exactly 2.");
            }

            // (c) Concurrent closing review
            {
                var vector = VectorNamed(sessionVectors, "session-closing-review");
                using var attempt = OpenSession(vector.GetProperty("initialization"));
                attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(vector.GetProperty("allAccount")), attempt));
                attempt.AcceptAllAccountResponse(AllAccountResponse(vector.GetProperty("allAccount")));
                attempt.RecordClosingCandidate(Bind(ClosingCandidate(vector.GetProperty("closing"), attempt.InitializationEvidence!), attempt));

                var closeResp = ClosingResponse(vector.GetProperty("closing"));
                var results = new FinTsPinTanReadSessionResult[16];
                Parallel.For(0, results.Length, i =>
                {
                    results[i] = attempt.AcceptClosingResponse(closeResp);
                });

                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.RejectedForReview) == 1,
                    "Concurrent closing review: exactly one thread gets RejectedForReview.");
                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.Terminal) == 15,
                    "Concurrent closing review: other 15 threads get Terminal.");
                Verify(attempt.GetSnapshot().ResponsesHandled == 3 && attempt.GetSnapshot().State == FinTsPinTanReadSessionState.NeedsReview,
                    "Concurrent closing review: ResponsesHandled incremented to exactly 3.");
            }
        }

        // 7. Progress Monotonicity Invariant across all stages: ResponsesHandled <= RequestsRecorded
        foreach (var vector in sessionVectors.EnumerateArray())
        {
            using var attempt = New();
            var initCand = FinTsPinTanRequestBinding.ForEnvelopeCandidate(
                FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("initialization").GetProperty("context")));

            attempt.StartInitialization(initCand);
            var s = attempt.GetSnapshot();
            Verify(s.ResponsesHandled <= s.RequestsRecorded && s.ResponsesHandled == 0 && s.RequestsRecorded == 1,
                $"{vector.GetProperty("name").GetString()}: Monotonicity holds after start.");

            // If it's a review init, test review termination
            if (vector.GetProperty("name").GetString() == "session-initialization-review")
            {
                var (b, p, perm, pt) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
                var res = attempt.AcceptInitializationResponse(p, perm, pt, FinTsReadParameterSet.Parse(pt.Source));
                s = attempt.GetSnapshot();
                Verify(s.ResponsesHandled <= s.RequestsRecorded && s.ResponsesHandled == 1 && s.RequestsRecorded == 1 &&
                    s.State == FinTsPinTanReadSessionState.NeedsReview,
                    $"{vector.GetProperty("name").GetString()}: Monotonicity holds at init review termination.");
            }
        }

        Console.WriteLine($"FinTS read-session response accounting across review and terminal outcomes passed ({count} checks).");
    }

    private static JsonElement VectorNamed(JsonElement vectors, string name)
    {
        foreach (var v in vectors.EnumerateArray())
        {
            if (v.GetProperty("name").GetString() == name)
                return v;
        }
        throw new InvalidOperationException($"Vector named '{name}' not found.");
    }

    private static FinTsPinTanReadSessionAttempt OpenSession(JsonElement initialization, int maximum = 16)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(initialization);
        var attempt = New(maximum);
        attempt.StartInitialization(binding.Request);
        if (attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source)).Transition != FinTsPinTanReadSessionTransition.DialogueOpen)
        { throw new InvalidOperationException("Accounting fixture must open a dialogue."); }
        return attempt;
    }

    private static FinTsPinTanReadSessionAttempt New(int maximum = 16) => new(TimeSpan.FromSeconds(10), maximum, new Clock());
    private static JsonDocument Fixture(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs." + name)!);

    private static (FinTsPinTanRequestBinding Binding, FinTsTanParameterSet Procedures, FinTsPermittedProcedureSet Permissions, FinTsPinTanParameterSet PinTan, FinTsReadParameterSet? Reads)
        ParseInit(JsonElement element)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(element);
        var reads = element.TryGetProperty("reads", out var r) ? FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(r.GetString()!))))) : null;
        return (binding.Request, procedures, permissions, pinTan, reads);
    }

    private static FinTsPinTanReadSessionResult AcceptInit(
        FinTsPinTanReadSessionAttempt attempt,
        int mode,
        (FinTsPinTanRequestBinding Binding, FinTsTanParameterSet Procedures, FinTsPermittedProcedureSet Permissions, FinTsPinTanParameterSet PinTan, FinTsReadParameterSet? Reads) input) =>
        mode switch
        {
            0 => attempt.AcceptInitializationResponse(FinTsParameterSet.Parse(input.Procedures.Source.Source)),
            1 => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions),
            2 => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions, input.PinTan),
            _ => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions, input.PinTan, input.Reads ?? FinTsReadParameterSet.Parse(input.PinTan.Source)),
        };

    private static FinTsPinTanReadSessionResult AcceptInit(
        FinTsPinTanReadSessionAttempt attempt,
        int mode,
        FinTsResponse response) =>
        mode switch
        {
            0 => attempt.AcceptInitializationResponse(FinTsParameterSet.Parse(response)),
            _ => attempt.AcceptInitializationResponse(FinTsParameterSet.Parse(response)),
        };

    private sealed class Clock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 1000;
    }
}
