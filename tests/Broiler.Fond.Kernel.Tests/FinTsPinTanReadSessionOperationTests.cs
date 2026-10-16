using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadLaterContinuationContextTests;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionOperationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using var sessions = Fixture("pin-tan-read-session-attempt-v1.json");
        var vectors = sessions.RootElement.GetProperty("vectors");
        var detachedBalance = BalanceFirstReadCandidate(vectors[2].GetProperty("balanceInitial"));
        var detachedDiscovery = AllAccountCandidate(vectors[0].GetProperty("allAccount"));

        void RejectNewOperation(FinTsPinTanReadSessionAttempt attempt)
        {
            var before = attempt.GetSnapshot();
            var balances = attempt.AggregatedBalances.ToArray();
            var first = attempt.BalanceFirstPageEvidence;
            var discovery = attempt.AllAccountEvidence;
            Verify(attempt.RecordBalanceFirstReadCandidate(detachedBalance) == FinTsPinTanReadSessionTransition.WrongState,
                "A completed or limited operation cannot start another balance read.");
            Verify(attempt.RecordAllAccountCandidate(detachedDiscovery) == FinTsPinTanReadSessionTransition.WrongState,
                "A completed or limited operation cannot switch to discovery.");
            Verify(SameProgress(before, attempt.GetSnapshot()) && attempt.AggregatedBalances.SequenceEqual(balances) &&
                ReferenceEquals(first, attempt.BalanceFirstPageEvidence) && ReferenceEquals(discovery, attempt.AllAccountEvidence),
                "Rejected operation restart preserves the original results, page budget and deadline.");
        }

        using (var envelopes = Fixture("pin-tan-read-request-envelope-v1.json"))
        {
            var sample = envelopes.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("name").GetString() == "single-account-discovery");
            using var attempt = Open(sample.GetProperty("context").GetProperty("initialization"), 2);
            var candidate = Bind(FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadPinTrailerTests.Context(sample)), attempt);
            Verify(candidate.Context.HasMatchingEvidence && candidate.Context.Signature.Request.Request.Source.Code == "HKSPA",
                "Selected-account discovery is a valid generic first-read candidate.");
            var before = attempt.GetSnapshot();
            Verify(attempt.RecordBalanceFirstReadCandidate(candidate) == FinTsPinTanReadSessionTransition.ContextMismatch,
                "The balance path rejects HKSPA even with exact session provenance and counters.");
            Verify(attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanReadSessionIssue.OperationMismatch), "Wrong operation is reported with scalar diagnostics.");
            Verify(SameProgress(before, attempt.GetSnapshot()) && attempt.BalanceFirstPageEvidence is null && attempt.AggregatedBalances.Count == 0,
                "Wrong operation does not consume a request, a page or any balance data.");
            Verify(attempt.RecordClosingCandidate(ClosingCandidate(vectors[5].GetProperty("closing"), attempt.InitializationEvidence!)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "The session remains closable after rejecting a wrong operation.");
        }

        // Completion at the page budget is still completion when the bank reports no more pages.
        foreach (int index in new[] { 0, 1, 2, 4 })
        {
            var vector = vectors[index];
            using var attempt = Open(vector.GetProperty("initialization"), 1);
            bool balance = index is 2 or 4;
            FinTsPinTanReadSessionResult result;
            if (balance)
            {
                attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(vector.GetProperty("balanceInitial")), attempt));
                result = attempt.AcceptBalanceResponse(BalanceResponse(vector.GetProperty("balanceInitial")));
            }
            else
            {
                attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(vector.GetProperty("allAccount")), attempt));
                result = attempt.AcceptAllAccountResponse(AllAccountResponse(vector.GetProperty("allAccount")));
            }
            Verify(result.Transition == (index is 1 or 4 ? FinTsPinTanReadSessionTransition.UnavailableReported : FinTsPinTanReadSessionTransition.ExecutionReported),
                "Execution and unavailable outcomes keep their precise result at the configured budget.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.OperationCompleted &&
                attempt.GetSnapshot().PagesAccepted == (balance ? 1 : 0) && !attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanReadSessionIssue.PageLimitReached),
                "Only accepted balance pages consume the balance budget; complete outcomes are not truncated.");
            RejectNewOperation(attempt);
            Verify(attempt.RecordClosingCandidate(ClosingCandidate(vector.GetProperty("closing"), attempt.InitializationEvidence!)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Completed operation advances to closing.");
            Verify(attempt.AcceptClosingResponse(ClosingResponse(vector.GetProperty("closing"))).Transition == FinTsPinTanReadSessionTransition.Closed,
                "Completed discovery or balance operation closes successfully.");
            FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
            Verify(result.HasMatchingEvidence, "Caller retains the original operation result after closing.");
        }

        using var laterDocument = Fixture("pin-tan-read-later-continuation-context-v1.json");
        using var semantics = Fixture("pin-tan-read-continuation-semantics-v1.json");
        foreach (bool unavailable in new[] { false, true })
        {
            var finalPage = unavailable
                ? semantics.RootElement.GetProperty("vectors").EnumerateArray().Single(v => v.GetProperty("name").GetString() == "balance-unavailable")
                : vectors[3].GetProperty("balanceContinuations")[0];
            var initial = finalPage.GetProperty("candidate").GetProperty("context").GetProperty("continuation").GetProperty("previous");
            using var attempt = Open(initial.GetProperty("candidate").GetProperty("context").GetProperty("initialization"), 2);
            attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(initial), attempt));
            Verify(attempt.AcceptBalanceResponse(BalanceResponse(initial)).Transition == FinTsPinTanReadSessionTransition.PartialAccepted,
                "A partial first page leaves room for the final page.");
            attempt.RecordBalanceContinuationCandidate(Bind(BalanceContinuationCandidate(finalPage), attempt));
            var result = attempt.AcceptBalanceResponse(BalanceResponse(finalPage));
            Verify(result.Transition == (unavailable ? FinTsPinTanReadSessionTransition.UnavailableReported : FinTsPinTanReadSessionTransition.ExecutionReported) &&
                attempt.GetSnapshot().PagesAccepted == 2 && attempt.GetSnapshot().State == FinTsPinTanReadSessionState.OperationCompleted &&
                !attempt.GetSnapshot().Issues.HasFlag(FinTsPinTanReadSessionIssue.PageLimitReached),
                "A final continuation at the budget reports the bank outcome, not truncation.");
            RejectNewOperation(attempt);
        }
        var second = laterDocument.RootElement.GetProperty("vectors")[0].GetProperty("previous");
        var first = second.GetProperty("candidate").GetProperty("context").GetProperty("continuation").GetProperty("previous");
        var template = BalanceContinuationCandidate(second).Context;
        foreach (int maximum in new[] { 1, 2, 3, 128 })
        {
            using var attempt = Open(first.GetProperty("candidate").GetProperty("context").GetProperty("initialization"), maximum);
            var candidate = Bind(BalanceFirstReadCandidate(first), attempt);
            Verify(attempt.RecordBalanceFirstReadCandidate(candidate) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Fresh session starts one balance operation.");
            var result = attempt.AcceptBalanceResponse(BalanceResponse(first));
            var snapshots = new List<(FinTsPinTanReadSessionResult Result, FinTsBalanceReport[] Balances)>();
            for (int page = 1; page <= maximum; page++)
            {
                if (page > 1)
                {
                    var continuation = Continue(attempt, page + 1, attempt.CurrentContinuationToken!, template);
                    var before = attempt.GetSnapshot();
                    Verify(attempt.RecordBalanceContinuationCandidate(continuation) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                        "Only a continuation of the accepted predecessor advances the operation.");
                    Verify(attempt.GetSnapshot().PagesAccepted == before.PagesAccepted, "Recording a candidate does not consume a page.");
                    var data = FinTsReadDataSet.Parse(MakeContinuationResponse(page + 1, "PAGE-" + page.ToString(CultureInfo.InvariantCulture)));
                    result = attempt.AcceptBalanceResponse(data);
                    var accepted = attempt.GetSnapshot();
                    Verify(attempt.AcceptBalanceResponse(data).Transition == FinTsPinTanReadSessionTransition.ReplayRejected && SameProgress(accepted, attempt.GetSnapshot()),
                        "Replayed response cannot consume another page or duplicate balances.");
                }
                snapshots.Add((result, result.AggregatedBalances.ToArray()));
                var snapshot = attempt.GetSnapshot();
                Verify(snapshot.PagesAccepted == page && snapshot.BalancesReported == page && result.AggregatedBalances.Count == page,
                    "Each accepted partial page contributes exactly once to this operation.");
                Verify(result.Transition.ToString() == (page == maximum ? "PageLimitReached" : "PartialAccepted"),
                    "Partial data reaching the page budget reports a distinct limit outcome, never execution completion.");
            }
            var limited = attempt.GetSnapshot();
            Verify(limited.State == FinTsPinTanReadSessionState.PageLimitReached && limited.Stage == FinTsPinTanReadSessionStage.Closing && attempt.CurrentContinuationToken is null,
                "At the page budget, continuation stops while the dialogue remains available for closing.");
            string lastToken = result.BalanceContinuationEvidence?.ReportedContinuationToken ?? result.BalanceFirstPageEvidence!.ReportedContinuationToken!;
            // At the protocol maximum, no valid page 129 candidate can be built; an existing candidate still must be refused.
            var extra = maximum == 128 ? BalanceContinuationCandidate(second) : Continue(attempt, maximum + 2, lastToken, template);
            Verify(attempt.RecordBalanceContinuationCandidate(extra) == FinTsPinTanReadSessionTransition.WrongState && SameProgress(limited, attempt.GetSnapshot()),
                "The page budget cannot be exceeded by recording another continuation.");
            RejectNewOperation(attempt);
            var closing = CloseCandidate(attempt, maximum + 2, vectors[5].GetProperty("closing"));
            Verify(attempt.RecordClosingCandidate(closing) == FinTsPinTanReadSessionTransition.CandidateRecorded && attempt.GetSnapshot().PagesAccepted == maximum,
                "Closing after truncation preserves the accepted-page count.");
            Verify(attempt.AcceptClosingResponse(CloseResponse(maximum + 2)).Transition == FinTsPinTanReadSessionTransition.Closed,
                "Truncated operations can finish dialogue closure without consuming another balance page.");
            attempt.Dispose();
            FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
            Verify(snapshots.All(s => s.Result.AggregatedBalances.SequenceEqual(s.Balances)), "Every caller page snapshot survives later pages and disposal.");
            Verify(attempt.GetSnapshot().PagesAccepted == maximum && attempt.GetSnapshot().BalancesReported == maximum,
                "Terminal scalar diagnostics retain the operation totals.");
        }
        Console.WriteLine($"FinTS read-session operation scope and page budgets passed ({count} checks).");
    }

    private static FinTsPinTanReadContinuationRequestBinding Continue(FinTsPinTanReadSessionAttempt attempt, int message, string token, FinTsPinTanReadContinuationSignatureContext template)
    {
        var request = MakeContinuationRequest(message, message, token);
        var pages = attempt.BalanceContinuationPages;
        var context = pages.Count == 0
            ? FinTsPinTanReadContinuationContext.Evaluate(attempt.BalanceFirstPageEvidence!, request)
            : FinTsPinTanReadContinuationContext.Evaluate(pages[^1], request);
        return FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationSignatureContext.Evaluate(context, template.Header, template.Selection, template.ControlReference));
    }

    private static FinTsPinTanDialogueEndRequestBinding CloseCandidate(FinTsPinTanReadSessionAttempt attempt, int message, JsonElement template)
    {
        string number = message.ToString(CultureInfo.InvariantCulture);
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{number}'HKEND:2:1+SYNTHETIC'HNHBS:3:1+{number}'";
        wire = wire.Replace("000000000000", wire.Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var request = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)), message);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(template.GetProperty("headerBase64").GetString()!)).Segments.Single());
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.EvaluateForInitialization(attempt.InitializationEvidence!, request, header, message, message));
    }

    private static bool SameProgress(FinTsPinTanReadSessionSnapshot left, FinTsPinTanReadSessionSnapshot right) =>
        left.State == right.State && left.Stage == right.Stage && left.RequestsRecorded == right.RequestsRecorded && left.ResponsesHandled == right.ResponsesHandled &&
        left.PagesAccepted == right.PagesAccepted && left.BalancesReported == right.BalancesReported && left.AccountsDiscovered == right.AccountsDiscovered &&
        left.ClientMessageNumber == right.ClientMessageNumber && left.BankMessageNumber == right.BankMessageNumber && left.RemainingTime == right.RemainingTime;

    private static FinTsResponse CloseResponse(int message)
    {
        const string inner = "HIRMG:2:2+0100::PUBLIC-CLOSE'";
        string number = message.ToString(CultureInfo.InvariantCulture);
        string envelope = $"HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:PUBLIC-BANK:PUBLIC-USER:V:0:0+0'HNVSD:999:1+@{inner.Length}@{inner}'";
        string wire = $"HNHBK:1:3+000000000000+300+SYNTHETIC+{number}+SYNTHETIC:{number}'{envelope}HNHBS:3:1+{number}'";
        wire = wire.Replace("000000000000", wire.Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire))));
    }

    private static FinTsPinTanReadSessionAttempt Open(JsonElement initialization, int maximum)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(initialization);
        var clock = new Clock();
        var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(10), maximum, clock);
        attempt.StartInitialization(binding.Request);
        if (attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source)).Transition != FinTsPinTanReadSessionTransition.DialogueOpen)
        { throw new InvalidOperationException("Operation fixture must open a dialogue."); }
        clock.Milliseconds = 1000;
        return attempt;
    }

    private static JsonDocument Fixture(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs." + name)!);
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }
}
