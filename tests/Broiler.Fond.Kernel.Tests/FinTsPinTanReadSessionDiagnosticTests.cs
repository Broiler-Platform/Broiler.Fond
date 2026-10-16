using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionDiagnosticTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using var document = Fixture("pin-tan-read-session-attempt-v1.json");
        var vectors = document.RootElement.GetProperty("vectors");
        var foreign = AllAccountResponse(vectors[12].GetProperty("extraResponses")[0]);

        void Foreign(FinTsPinTanReadSessionAttempt attempt, Func<FinTsPinTanReadSessionResult> submit)
        {
            var before = attempt.GetSnapshot();
            var pending = Pending(attempt);
            var result = submit();
            var after = attempt.GetSnapshot();
            Verify(result.Transition == FinTsPinTanReadSessionTransition.ContextMismatch && !result.HasMatchingEvidence,
                "Foreign responses report context mismatch without an evidence handoff.");
            Verify(after.Issues.HasFlag(FinTsPinTanReadSessionIssue.BindingNeedsReview) && after.BindingIssues != FinTsPinTanResponseBindingIssue.None,
                "An unresolved binding mismatch remains visible in scalar diagnostics.");
            Verify(after.State == before.State && after.RequestsRecorded == before.RequestsRecorded && after.ResponsesHandled == before.ResponsesHandled &&
                after.PagesAccepted == before.PagesAccepted && after.BalancesReported == before.BalancesReported && after.RemainingTime == before.RemainingTime &&
                ReferenceEquals(Pending(attempt), pending), "Foreign responses preserve the pending candidate, progress and original deadline.");
        }

        void Diagnostics(FinTsPinTanReadSessionAttempt attempt, FinTsPinTanReadSessionIssue expected, FinTsPinTanResponseBindingIssue binding = FinTsPinTanResponseBindingIssue.None)
        {
            var snapshot = attempt.GetSnapshot();
            Verify(snapshot.Issues == expected, $"Current outcome has exactly its diagnostic flags: {snapshot.Issues} vs {expected}.");
            Verify(snapshot.BindingIssues == binding, "Resolved foreign binding details are replaced by the current response binding, including genuine review errors.");
        }

        foreach (int index in new[] { 0, 7 })
        {
            var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(vectors[index].GetProperty("initialization"));
            using var attempt = New();
            attempt.StartInitialization(binding.Request);
            Foreign(attempt, () => attempt.AcceptInitializationResponse(FinTsParameterSet.Parse(foreign.Source)));
            var result = attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
            Verify(result.Transition == (index == 7 ? FinTsPinTanReadSessionTransition.RejectedForReview : FinTsPinTanReadSessionTransition.DialogueOpen),
                "Initialization retry publishes the current semantic outcome.");
            Diagnostics(attempt, index == 7 ? FinTsPinTanReadSessionIssue.InitializationNeedsReview : FinTsPinTanReadSessionIssue.None, result.RequirementsEvidence!.Procedures.Initialization.Binding.Issues);
            Verify(result.RequirementsEvidence is not null && attempt.GetSnapshot().RequirementsIssues == result.RequirementsEvidence.Issues,
                "Initialization review details come from the published evidence.");
        }

        foreach (int index in new[] { 0, 1, 8 })
        {
            var vector = vectors[index];
            using var attempt = Open(vector.GetProperty("initialization"));
            var candidate = AllAccountCandidate(vector.GetProperty("allAccount"));
            Verify(attempt.RecordAllAccountCandidate(candidate) == FinTsPinTanReadSessionTransition.ContextMismatch,
                "Detached discovery candidate supplies a transient provenance error.");
            Verify(attempt.RecordAllAccountCandidate(Bind(candidate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Valid discovery candidate replaces the rejected proposal.");
            Diagnostics(attempt, FinTsPinTanReadSessionIssue.None);
            Foreign(attempt, () => attempt.AcceptAllAccountResponse(foreign));
            Verify(attempt.RecordAllAccountCandidate(candidate) == FinTsPinTanReadSessionTransition.WrongState &&
                attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanReadSessionIssue.BindingNeedsReview),
                "A wrong-state call does not erase an unresolved response diagnostic.");
            var result = attempt.AcceptAllAccountResponse(AllAccountResponse(vector.GetProperty("allAccount")));
            var expected = index == 8 ? FinTsPinTanReadSessionIssue.OperationNeedsReview : FinTsPinTanReadSessionIssue.None;
            Diagnostics(attempt, expected, result.AllAccountEvidence!.Binding.Issues);
            Verify(result.Transition == (index == 8 ? FinTsPinTanReadSessionTransition.RejectedForReview : index == 1 ? FinTsPinTanReadSessionTransition.UnavailableReported : FinTsPinTanReadSessionTransition.ExecutionReported),
                "Discovery retry preserves execution, unavailable and review distinctions.");
            Verify(attempt.GetSnapshot().AllAccountIssues == result.AllAccountEvidence!.Issues && attempt.GetSnapshot().ReadIssues == result.AllAccountEvidence.ResponseIssues,
                "Discovery's detailed response diagnostics agree with its handed-out evidence.");
        }

        foreach (int index in new[] { 2, 3, 4, 9, 17 })
        {
            var vector = vectors[index];
            using var attempt = Open(vector.GetProperty("initialization"), index == 17 ? 1 : 16);
            var candidate = BalanceFirstReadCandidate(vector.GetProperty("balanceInitial"));
            Verify(attempt.RecordBalanceFirstReadCandidate(candidate) == FinTsPinTanReadSessionTransition.ContextMismatch, "Detached balance candidate produces a transient error.");
            Verify(attempt.RecordBalanceFirstReadCandidate(Bind(candidate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Balance candidate can be retried using accepted evidence.");
            Diagnostics(attempt, FinTsPinTanReadSessionIssue.None);
            Foreign(attempt, () => attempt.AcceptBalanceResponse(foreign));
            var result = attempt.AcceptBalanceResponse(BalanceResponse(vector.GetProperty("balanceInitial")));
            var expected = index == 9 ? FinTsPinTanReadSessionIssue.OperationNeedsReview : index == 17 ? FinTsPinTanReadSessionIssue.PageLimitReached : FinTsPinTanReadSessionIssue.None;
            Diagnostics(attempt, expected, result.BalanceFirstPageEvidence!.Binding.Issues);
            var transition = index switch
            {
                3 => FinTsPinTanReadSessionTransition.PartialAccepted,
                4 => FinTsPinTanReadSessionTransition.UnavailableReported,
                9 => FinTsPinTanReadSessionTransition.RejectedForReview,
                17 => FinTsPinTanReadSessionTransition.PageLimitReached,
                _ => FinTsPinTanReadSessionTransition.ExecutionReported,
            };
            Verify(result.Transition == transition && attempt.GetSnapshot().ReadIssues == result.BalanceFirstPageEvidence!.ReadIssues,
                "Balance result and current diagnostics describe the same semantic outcome.");
        }

        using var continuationDocument = Fixture("pin-tan-read-continuation-semantics-v1.json");
        var continuations = continuationDocument.RootElement.GetProperty("vectors");
        foreach (int index in new[] { 0, 10, 13, 17 })
        {
            var second = continuations[index];
            var first = second.GetProperty("candidate").GetProperty("context").GetProperty("continuation").GetProperty("previous");
            using var attempt = Open(first.GetProperty("candidate").GetProperty("context").GetProperty("initialization"));
            attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(first), attempt));
            var firstData = BalanceResponse(first);
            Verify(attempt.AcceptBalanceResponse(firstData).Transition == FinTsPinTanReadSessionTransition.PartialAccepted, "Continuation diagnostics start from an accepted partial page.");
            Verify(attempt.AcceptBalanceResponse(firstData).Transition == FinTsPinTanReadSessionTransition.ReplayRejected, "Repeated prior page is diagnosed as a replay.");
            var candidate = BalanceContinuationCandidate(second);
            Verify(attempt.RecordBalanceContinuationCandidate(candidate) == FinTsPinTanReadSessionTransition.ContextMismatch, "Detached continuation supplies an additional transient provenance error.");
            Verify(attempt.RecordBalanceContinuationCandidate(Bind(candidate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Valid continuation resolves prior candidate and replay diagnostics.");
            Diagnostics(attempt, FinTsPinTanReadSessionIssue.None);
            Foreign(attempt, () => attempt.AcceptBalanceResponse(foreign));
            var result = attempt.AcceptBalanceResponse(BalanceResponse(second));
            Diagnostics(attempt, index == 17 ? FinTsPinTanReadSessionIssue.OperationNeedsReview : FinTsPinTanReadSessionIssue.None, result.BalanceContinuationEvidence!.Binding.Issues);
            var transition = index switch
            {
                10 => FinTsPinTanReadSessionTransition.UnavailableReported,
                13 => FinTsPinTanReadSessionTransition.PartialAccepted,
                17 => FinTsPinTanReadSessionTransition.RejectedForReview,
                _ => FinTsPinTanReadSessionTransition.ExecutionReported,
            };
            Verify(result.Transition == transition && attempt.GetSnapshot().ReadIssues == result.BalanceContinuationEvidence!.ReadIssues,
                "Continuation retry publishes only its current response outcome and details.");
        }

        foreach (bool limited in new[] { false, true })
            foreach (int closingIndex in new[] { 0, 6, 10 })
            {
                var vector = vectors[17];
                using var attempt = Open(vector.GetProperty("initialization"), limited ? 1 : 2);
                attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(vector.GetProperty("balanceInitial")), attempt));
                var data = BalanceResponse(vector.GetProperty("balanceInitial"));
                var partial = attempt.AcceptBalanceResponse(data);
                var retained = partial.AggregatedBalances.ToArray();
                var durable = limited ? FinTsPinTanReadSessionIssue.PageLimitReached : FinTsPinTanReadSessionIssue.None;
                Verify(attempt.AcceptBalanceResponse(data).Transition == FinTsPinTanReadSessionTransition.ReplayRejected, "Replay before closing remains diagnosed until progress resumes.");
                var closing = ClosingCandidate(vectors[closingIndex].GetProperty("closing"), attempt.InitializationEvidence!);
                Verify(attempt.RecordClosingCandidate(closing) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Valid closing candidate resolves transient replay diagnostics.");
                Diagnostics(attempt, durable);
                Foreign(attempt, () => attempt.AcceptClosingResponse(foreign.Source));
                var result = attempt.AcceptClosingResponse(ClosingResponse(vectors[closingIndex].GetProperty("closing")));
                Diagnostics(attempt, durable | (closingIndex == 10 ? FinTsPinTanReadSessionIssue.ClosingNeedsReview : FinTsPinTanReadSessionIssue.None), result.ClosingEvidence!.Binding.Issues);
                Verify(result.Transition == (closingIndex == 10 ? FinTsPinTanReadSessionTransition.RejectedForReview : closingIndex == 6 ? FinTsPinTanReadSessionTransition.AbortReported : FinTsPinTanReadSessionTransition.Closed),
                    "Closing reports clean closure, bank abort or review without reinterpreting the read result.");
                Verify(attempt.GetSnapshot().ClosingIssues == result.ClosingEvidence!.Issues && partial.AggregatedBalances.SequenceEqual(retained),
                    "Closing diagnostics match closing evidence and preserve the caller's earlier partial data.");
                var issues = attempt.GetSnapshot().Issues;
                Verify(attempt.AcceptClosingResponse(ClosingResponse(vectors[closingIndex].GetProperty("closing"))).Transition == FinTsPinTanReadSessionTransition.Terminal && attempt.GetSnapshot().Issues == issues,
                    "Terminal replay cannot clear durable truncation or current review diagnostics.");
                FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
            }
        Console.WriteLine($"FinTS read-session transient diagnostics and outcomes passed ({count} checks).");
    }

    private static FinTsPinTanReadSessionAttempt Open(JsonElement initialization, int maximum = 16)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(initialization);
        var attempt = New(maximum);
        attempt.StartInitialization(binding.Request);
        if (attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source)).Transition != FinTsPinTanReadSessionTransition.DialogueOpen)
        { throw new InvalidOperationException("Diagnostic fixture must open a dialogue."); }
        return attempt;
    }

    private static FinTsPinTanReadSessionAttempt New(int maximum = 16) => new(TimeSpan.FromSeconds(10), maximum, new Clock());
    private static object? Pending(FinTsPinTanReadSessionAttempt attempt) => typeof(FinTsPinTanReadSessionAttempt).GetProperty("PendingCandidate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);
    private static JsonDocument Fixture(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs." + name)!);
    private sealed class Clock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 1000;
    }
}
