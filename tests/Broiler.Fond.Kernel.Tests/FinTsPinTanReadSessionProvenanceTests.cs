using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionProvenanceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using var document = Fixture("pin-tan-read-session-attempt-v1.json");
        var vectors = document.RootElement.GetProperty("vectors");

        void Reject(FinTsPinTanReadSessionAttempt attempt, Func<FinTsPinTanReadSessionTransition> submit)
        {
            var before = attempt.GetSnapshot();
            var initialization = attempt.RequirementsEvidence;
            var token = attempt.CurrentContinuationToken;
            Verify(submit() == FinTsPinTanReadSessionTransition.ContextMismatch, "Detached evidence is rejected despite matching dialogue and counters.");
            var after = attempt.GetSnapshot();
            Verify(after.Issues.HasFlag(FinTsPinTanReadSessionIssue.EvidenceSourceMismatch), "Provenance failure uses fixed scalar diagnostics.");
            Verify(after.State == before.State && after.Stage == before.Stage && after.RequestsRecorded == before.RequestsRecorded &&
                after.ResponsesHandled == before.ResponsesHandled && after.PagesAccepted == before.PagesAccepted &&
                after.BalancesReported == before.BalancesReported && after.AccountsDiscovered == before.AccountsDiscovered &&
                after.RemainingTime == before.RemainingTime, "Provenance rejection preserves state, progress and absolute deadline.");
            Verify(ReferenceEquals(attempt.RequirementsEvidence, initialization) && attempt.CurrentContinuationToken == token &&
                typeof(FinTsPinTanReadSessionAttempt).GetProperty("PendingCandidate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt) is null,
                "Provenance rejection preserves accepted evidence without pinning the rejected candidate.");
        }

        foreach (bool foreignSession in new[] { false, true })
        {
            var vector = vectors[0];
            using var attempt = Open(vector.GetProperty("initialization"));
            using var foreign = Open(vector.GetProperty("initialization"));
            var raw = AllAccountCandidate(vector.GetProperty("allAccount"));
            var detached = foreignSession ? Bind(raw, foreign) : raw;
            Reject(attempt, () => attempt.RecordAllAccountCandidate(detached));
            Verify(attempt.RecordAllAccountCandidate(Bind(raw, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Exact session initialization permits all-account retry after rejection.");
            Verify(attempt.AcceptAllAccountResponse(AllAccountResponse(vector.GetProperty("allAccount"))).HasMatchingEvidence,
                "All-account evidence still completes after provenance rejection.");
            Reject(attempt, () => attempt.RecordClosingCandidate(ClosingCandidate(vector.GetProperty("closing"), foreign.InitializationEvidence!)));
            Verify(attempt.RecordClosingCandidate(ClosingCandidate(vector.GetProperty("closing"), attempt.InitializationEvidence!)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Closing uses the exact accepted initialization evidence after an operation.");
            Verify(attempt.AcceptClosingResponse(ClosingResponse(vector.GetProperty("closing"))).Transition == FinTsPinTanReadSessionTransition.Closed,
                "All-account session closes after rejecting foreign closing evidence.");
            FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
        }

        foreach (bool foreignSession in new[] { false, true })
        {
            var vector = vectors[2];
            using var attempt = Open(vector.GetProperty("initialization"));
            using var foreign = Open(vector.GetProperty("initialization"));
            var raw = BalanceFirstReadCandidate(vector.GetProperty("balanceInitial"));
            var detached = foreignSession ? Bind(raw, foreign) : raw;
            Reject(attempt, () => attempt.RecordBalanceFirstReadCandidate(detached));
            Verify(attempt.RecordBalanceFirstReadCandidate(Bind(raw, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Exact session initialization permits first balance read after rejection.");
            Verify(attempt.AcceptBalanceResponse(BalanceResponse(vector.GetProperty("balanceInitial"))).HasMatchingEvidence,
                "Balance read still completes after provenance rejection.");
        }

        // Opening without requirements does not authorize importing a detached requirements report.
        foreach (bool balance in new[] { false, true })
        {
            using var basicDocument = Fixture("pin-tan-initialization-evidence-v1.json");
            var basic = basicDocument.RootElement.GetProperty("vectors")[0];
            var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(basic.GetProperty("context")));
            using var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(10), clock: new Clock());
            attempt.StartInitialization(request);
            Verify(attempt.AcceptInitializationResponse(FinTsParameterSet.Parse(ClosingResponse(basic))).Transition == FinTsPinTanReadSessionTransition.DialogueOpen && attempt.RequirementsEvidence is null,
                "Basic initialization opens without accepted operational requirements.");
            Reject(attempt, () => balance
                ? attempt.RecordBalanceFirstReadCandidate(BalanceFirstReadCandidate(vectors[2].GetProperty("balanceInitial")))
                : attempt.RecordAllAccountCandidate(AllAccountCandidate(vectors[0].GetProperty("allAccount"))));
            Verify(attempt.RecordClosingCandidate(ClosingCandidate(vectors[5].GetProperty("closing"), attempt.InitializationEvidence!)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Basic initialization can still close with its own evidence.");
        }

        // A synchronization closing context is valid in isolation but belongs to another dialogue lifecycle.
        using (var syncDocument = Fixture("pin-tan-synchronization-session-attempt-v1.json"))
        using (var attempt = Open(vectors[5].GetProperty("initialization")))
        {
            var syncVector = syncDocument.RootElement.GetProperty("vectors")[0];
            var request = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(syncVector.GetProperty("syncCandidate")));
            using var synchronization = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromSeconds(10), new Clock());
            synchronization.StartSynchronization(request);
            var response = Response(syncVector.GetProperty("syncResponse"));
            var evidence = synchronization.AcceptSynchronizationResponse(response).SynchronizationEvidence!;
            var close = ClosingCandidate(vectors[5].GetProperty("closing"), attempt.InitializationEvidence!);
            var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(syncVector.GetProperty("closingCandidate").GetProperty("headerBase64").GetString()!)).Segments.Single());
            var foreign = FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.Evaluate(evidence, close.Context.Request, header));
            Reject(attempt, () => attempt.RecordClosingCandidate(foreign));
            Verify(attempt.RecordClosingCandidate(close) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Read session can close directly using its own initialization after rejecting synchronization evidence.");
        }

        // Exercise both predecessor overloads with matching values and even the exact same response binding.
        using var laterDocument = Fixture("pin-tan-read-later-continuation-context-v1.json");
        var later = laterDocument.RootElement.GetProperty("vectors")[0];
        var second = later.GetProperty("previous");
        var first = second.GetProperty("candidate").GetProperty("context").GetProperty("continuation").GetProperty("previous");
        using (var attempt = Open(first.GetProperty("candidate").GetProperty("context").GetProperty("initialization")))
        {
            var firstCandidate = Bind(BalanceFirstReadCandidate(first), attempt);
            Verify(attempt.RecordBalanceFirstReadCandidate(firstCandidate) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Multi-page provenance setup records first page.");
            var firstEvidence = attempt.AcceptBalanceResponse(BalanceResponse(first)).BalanceFirstPageEvidence!;
            var secondTemplate = BalanceContinuationCandidate(second);
            var repeatedFirst = FinTsPinTanReadEvidence.Evaluate(firstEvidence.Binding, firstEvidence.ReadData);
            var detachedSecond = Continuation(FinTsPinTanReadContinuationContext.Evaluate(repeatedFirst, secondTemplate.Context.Request), secondTemplate.Context);
            Verify(repeatedFirst.HasMatchingEvidence && ReferenceEquals(repeatedFirst.Request.Context, firstEvidence.Request.Context),
                "Re-evaluated first-page evidence has identical origin and matching values.");
            Reject(attempt, () => attempt.RecordBalanceContinuationCandidate(detachedSecond));
            Reject(attempt, () => attempt.RecordBalanceContinuationCandidate(secondTemplate));
            Verify(attempt.RecordBalanceContinuationCandidate(Bind(secondTemplate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Exact first-page evidence permits second page after detached predecessors are rejected.");
            var secondEvidence = attempt.AcceptBalanceResponse(BalanceResponse(second)).BalanceContinuationEvidence!;
            var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(later.GetProperty("requestBase64").GetString()!)), later.GetProperty("expectedBankMessageNumber").GetInt32());
            var repeatedSecond = FinTsPinTanReadContinuationEvidence.Evaluate(secondEvidence.Binding, secondEvidence.ReadData);
            Verify(repeatedSecond.HasMatchingEvidence && ReferenceEquals(repeatedSecond.Request, secondEvidence.Request),
                "Re-evaluated later-page evidence retains the same request but is not the accepted handoff.");
            Reject(attempt, () => attempt.RecordBalanceContinuationCandidate(Continuation(FinTsPinTanReadContinuationContext.Evaluate(repeatedSecond, request), secondTemplate.Context)));
            var detachedPrevious = FinTsPinTanReadLaterContinuationContextTests.Inputs(later).Previous;
            Reject(attempt, () => attempt.RecordBalanceContinuationCandidate(Continuation(FinTsPinTanReadContinuationContext.Evaluate(detachedPrevious, request), secondTemplate.Context)));
            Verify(attempt.RecordBalanceContinuationCandidate(Continuation(FinTsPinTanReadContinuationContext.Evaluate(secondEvidence, request), secondTemplate.Context)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Exact accepted later-page evidence permits page three.");
            Verify(attempt.GetSnapshot().PagesAccepted == 2 && attempt.GetSnapshot().RequestsRecorded == 4,
                "Only genuine candidates advance the multi-page session.");
            attempt.Dispose();
            FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
        }

        Console.WriteLine($"FinTS read-session evidence provenance passed ({count} checks).");
    }

    private static FinTsPinTanReadContinuationRequestBinding Continuation(FinTsPinTanReadContinuationContext context, FinTsPinTanReadContinuationSignatureContext template) =>
        FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationSignatureContext.Evaluate(context, template.Header, template.Selection, template.ControlReference));

    private static FinTsPinTanReadSessionAttempt Open(JsonElement initialization)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(initialization);
        var clock = new Clock();
        var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(10), clock: clock);
        attempt.StartInitialization(binding.Request);
        var result = attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
        if (result.Transition != FinTsPinTanReadSessionTransition.DialogueOpen) { throw new InvalidOperationException("Provenance fixture must open a dialogue."); }
        clock.Milliseconds = 1000;
        return attempt;
    }

    private static JsonDocument Fixture(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs." + name)!);

    private static FinTsResponse Response(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("wireBase64").GetString()!));
        return element.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }

    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
}
