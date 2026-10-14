using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-session-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        var (cleanBinding, cleanProc, cleanPerm, cleanPinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(vectors[0].GetProperty("initialization"));
        var cleanReq = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(cleanBinding, cleanProc, cleanPerm, cleanPinTan, FinTsReadParameterSet.Parse(cleanPinTan.Source));
        var cleanInitEvidence = cleanReq.Procedures.Initialization;

        foreach (var vector in vectors)
        {
            var clock = new Clock();
            int maxPages = vector.GetProperty("maximumBalancePages").GetInt32();
            using var attempt = New(clock, maxPages);

            var (initBinding, initProcedures, initPermissions, initPinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
            var initCandidate = initBinding.Request;

            var opType = vector.GetProperty("operationType").GetString();
            FinTsPinTanAllAccountRequestBinding? allAccountCandidate = null;
            FinTsReadDataSet? allAccountResponse = null;
            if (opType == "AllAccount")
            {
                var allAccElem = vector.GetProperty("allAccount");
                allAccountCandidate = AllAccountCandidate(allAccElem);
                allAccountResponse = AllAccountResponse(allAccElem);
            }

            FinTsPinTanReadRequestBinding? balanceFirstCandidate = null;
            FinTsReadDataSet? balanceFirstResponse = null;
            var balanceContinuationCandidates = new List<FinTsPinTanReadContinuationRequestBinding>();
            var balanceContinuationResponses = new List<FinTsReadDataSet>();
            int continuationCandidateIndex = 0;
            int balanceResponseIndex = 0;

            if (opType == "BalanceRefresh")
            {
                var balInitElem = vector.GetProperty("balanceInitial");
                balanceFirstCandidate = BalanceFirstReadCandidate(balInitElem);
                balanceFirstResponse = BalanceResponse(balInitElem);

                foreach (var contElem in vector.GetProperty("balanceContinuations").EnumerateArray())
                {
                    balanceContinuationCandidates.Add(BalanceContinuationCandidate(contElem));
                    balanceContinuationResponses.Add(BalanceResponse(contElem));
                }
            }

            var closingElem = vector.GetProperty("closing");
            var closingCandidate = ClosingCandidate(closingElem, cleanInitEvidence);
            var closingResponse = ClosingResponse(closingElem);

            var extraResponses = vector.GetProperty("extraResponses").EnumerateArray().ToArray();

            string AcceptClosing(FinTsResponse response)
            {
                var result = attempt.AcceptClosingResponse(response);
                if (result.Transition is FinTsPinTanReadSessionTransition.Closed or FinTsPinTanReadSessionTransition.AbortReported or FinTsPinTanReadSessionTransition.RejectedForReview)
                {
                    Verify(result.ClosingEvidence is not null && result.Stage == FinTsPinTanReadSessionStage.Terminal,
                        "Every processed terminal closing response hands evidence to its caller.");
                    Verify(attempt.ClosingEvidence is null, "Terminal closing evidence is not retained by the read session.");
                }
                if (result.Transition == FinTsPinTanReadSessionTransition.Terminal)
                {
                    Verify(result.ClosingEvidence is null, "Terminal replay does not repeat read closing evidence.");
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
                    case "start":
                        transition = attempt.StartInitialization(initCandidate).ToString();
                        break;

                    case "initResponse":
                        if (index.HasValue)
                        {
                            var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(extraResponses[index.Value].GetProperty("responseBase64").GetString()!));
                            var resp = extraResponses[index.Value].GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
                            var extraParams = FinTsParameterSet.Parse(resp);
                            transition = attempt.AcceptInitializationResponse(extraParams).Transition.ToString();
                        }
                        else
                        {
                            transition = attempt.AcceptInitializationResponse(initProcedures, initPermissions, initPinTan, FinTsReadParameterSet.Parse(initPinTan.Source)).Transition.ToString();
                        }
                        break;

                    case "recordAllAccount":
                        if (index.HasValue)
                        {
                            var extraCandidate = AllAccountCandidate(extraResponses[index.Value]);
                            transition = attempt.RecordAllAccountCandidate(extraCandidate).ToString();
                        }
                        else
                        {
                            transition = attempt.RecordAllAccountCandidate(Bind(allAccountCandidate!, attempt)).ToString();
                        }
                        break;

                    case "allAccountResponse":
                        if (index.HasValue)
                        {
                            var extraData = AllAccountResponse(extraResponses[index.Value]);
                            transition = attempt.AcceptAllAccountResponse(extraData).Transition.ToString();
                        }
                        else
                        {
                            transition = attempt.AcceptAllAccountResponse(allAccountResponse!).Transition.ToString();
                        }
                        break;

                    case "recordBalance":
                        transition = attempt.RecordBalanceFirstReadCandidate(Bind(balanceFirstCandidate!, attempt)).ToString();
                        break;

                    case "recordBalanceContinuation":
                        transition = attempt.RecordBalanceContinuationCandidate(Bind(balanceContinuationCandidates[continuationCandidateIndex++], attempt)).ToString();
                        break;

                    case "balanceResponse":
                        if (attempt.GetSnapshot().State == FinTsPinTanReadSessionState.AwaitingBalanceContinuationResponse)
                        {
                            transition = attempt.AcceptBalanceResponse(balanceContinuationResponses[balanceResponseIndex++]).Transition.ToString();
                        }
                        else
                        {
                            transition = attempt.AcceptBalanceResponse(balanceFirstResponse!).Transition.ToString();
                        }
                        break;

                    case "recordClosing":
                        transition = attempt.RecordClosingCandidate(Bind(closingCandidate, attempt)).ToString();
                        break;

                    case "closingResponse":
                        if (index.HasValue)
                        {
                            var extraClose = ClosingResponse(extraResponses[index.Value]);
                            transition = AcceptClosing(extraClose);
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
                        throw new InvalidOperationException("Unknown synthetic session action: " + step.GetProperty("action").GetString());
                }

                var snapshot = attempt.GetSnapshot();
                Verify(transition == step.GetProperty("transition").GetString(),
                    $"Independent session transition matches in {vector.GetProperty("name").GetString()}: {transition} vs {step.GetProperty("transition").GetString()}.");
                Verify(snapshot.State.ToString() == step.GetProperty("state").GetString(),
                    $"Independent session state matches in {vector.GetProperty("name").GetString()}: {snapshot.State} vs {step.GetProperty("state").GetString()}.");
                Verify(snapshot.Stage.ToString() == step.GetProperty("stage").GetString(),
                    $"Independent session stage matches in {vector.GetProperty("name").GetString()}: {snapshot.Stage} vs {step.GetProperty("stage").GetString()}.");
                Verify(snapshot.RequestsRecorded == step.GetProperty("requests").GetInt32(),
                    "Requests recorded match expected count in " + vector.GetProperty("name").GetString());
                Verify(snapshot.ResponsesHandled == step.GetProperty("handled").GetInt32(),
                    "Responses handled match expected count in " + vector.GetProperty("name").GetString());
                Verify(snapshot.PagesAccepted == step.GetProperty("pages").GetInt32(),
                    "Pages accepted match expected count in " + vector.GetProperty("name").GetString());
                Verify(snapshot.BalancesReported == step.GetProperty("balances").GetInt32(),
                    "Balances reported match expected count in " + vector.GetProperty("name").GetString());
                Verify(snapshot.AccountsDiscovered == step.GetProperty("accounts").GetInt32(),
                    "Accounts discovered match expected count in " + vector.GetProperty("name").GetString());

                if (snapshot.Stage == FinTsPinTanReadSessionStage.Terminal)
                {
                    FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                    Verify(Pending(attempt) is null && snapshot.RemainingTime == TimeSpan.Zero,
                        "Terminal session releases pending candidate and has zero remaining time.");
                }
                else
                {
                    Verify(snapshot.RemainingTime > TimeSpan.Zero,
                        "Active session retains non-zero remaining deadline.");
                }
            }

            if (attempt.GetSnapshot().Stage == FinTsPinTanReadSessionStage.Terminal)
            {
                Verify(Pending(attempt) is null, "Every independent terminal session trace releases its pending candidate: " + vector.GetProperty("name").GetString());
            }
        }

        Verify(vectors.Length == 28, "All twenty-eight independent read session attempt traces ran.");

        // Detailed lifecycle and boundary checks
        var sampleVector = vectors[0];
        var (sInitBinding, sInitProc, sInitPerm, sInitPinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(sampleVector.GetProperty("initialization"));
        var sInitCandidate = sInitBinding.Request;
        var sAllAccountCandidate = AllAccountCandidate(sampleVector.GetProperty("allAccount"));
        var sAllAccountResponse = AllAccountResponse(sampleVector.GetProperty("allAccount"));
        var sClosingElem = sampleVector.GetProperty("closing");
        var sClosingCandidate = ClosingCandidate(sClosingElem, cleanInitEvidence);
        var sClosingResponse = ClosingResponse(sClosingElem);

        // 1. Ready state assertions
        using (var attempt = New())
        {
            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanReadSessionState.Ready && snap.Stage == FinTsPinTanReadSessionStage.Initialization && snap.RemainingTime == TimeSpan.Zero,
                "Ready attempt has initial stage and zero deadline.");
            Verify(attempt.RecordAllAccountCandidate(Bind(sAllAccountCandidate, attempt)) == FinTsPinTanReadSessionTransition.Terminal,
                "Ready attempt cannot record operational candidates before initialization.");
            Verify(attempt.RecordClosingCandidate(Bind(sClosingCandidate, attempt)) == FinTsPinTanReadSessionTransition.Terminal,
                "Ready attempt cannot record closing candidates.");
            Verify(attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source)).Transition == FinTsPinTanReadSessionTransition.WrongState,
                "Ready attempt cannot accept responses.");
        }

        // 2. Start initialization, duplicate start rejection, dialogue pinning
        using (var attempt = New())
        {
            Verify(attempt.StartInitialization(sInitCandidate) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "First start records initialization candidate.");
            Verify(attempt.StartInitialization(sInitCandidate) == FinTsPinTanReadSessionTransition.WrongState,
                "Second start is rejected with WrongState without changing candidate.");
            Verify(Pending(attempt) is not null, "Pending candidate remains pinned.");

            var initResult = attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
            Verify(initResult.Transition == FinTsPinTanReadSessionTransition.DialogueOpen, "Init response opens dialogue.");
            Verify(attempt.DialogueId == "SYNTHETIC", "Reported dialogue ID is pinned.");
            Verify(attempt.RequirementsEvidence is not null && attempt.ProcedureEvidence is not null && attempt.InitializationEvidence is not null,
                "Initialization evidences are populated.");

            // Operational stage
            Verify(attempt.RecordAllAccountCandidate(Bind(sAllAccountCandidate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Records all-account candidate.");
            Verify(attempt.RecordAllAccountCandidate(Bind(sAllAccountCandidate, attempt)) == FinTsPinTanReadSessionTransition.WrongState,
                "Cannot record second candidate while awaiting response.");

            var opResult = attempt.AcceptAllAccountResponse(sAllAccountResponse);
            Verify(opResult.Transition == FinTsPinTanReadSessionTransition.ExecutionReported,
                "All-account discovery completed.");
            Verify(attempt.AllAccountEvidence is not null, "All-account evidence is populated.");

            // Closing stage
            Verify(attempt.RecordClosingCandidate(Bind(sClosingCandidate, attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Closing candidate recorded.");
            var closeResult = attempt.AcceptClosingResponse(sClosingResponse);
            Verify(closeResult.Transition == FinTsPinTanReadSessionTransition.Closed,
                "Dialogue closed successfully.");
            Verify(closeResult.ClosingEvidence is not null && attempt.ClosingEvidence is null, "Closing evidence belongs to the result after terminal cleanup.");
            Verify(Pending(attempt) is null, "Pending candidate cleared on close.");

            // Terminal replay immunity
            Verify(attempt.AcceptClosingResponse(sClosingResponse).Transition == FinTsPinTanReadSessionTransition.Terminal,
                "Terminal state rejects replayed responses.");
            Verify(attempt.StartInitialization(sInitCandidate) == FinTsPinTanReadSessionTransition.Terminal,
                "Terminal state rejects start.");
        }

        // 3. Transient ContextMismatch clears on subsequent valid response
        using (var attempt = New())
        {
            attempt.StartInitialization(sInitCandidate);
            var foreignInit = vectors.Single(v => v.GetProperty("name").GetString() == "foreign-init-response-keeps-state").GetProperty("extraResponses")[0];
            var fFrame = FinTsMessageFrame.Parse(Convert.FromBase64String(foreignInit.GetProperty("responseBase64").GetString()!));
            var fResp = foreignInit.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(fFrame)) : FinTsResponse.Parse(fFrame);
            var fParams = FinTsParameterSet.Parse(fResp);

            var foreignResult = attempt.AcceptInitializationResponse(fParams);
            Verify(foreignResult.Transition == FinTsPinTanReadSessionTransition.ContextMismatch,
                "Foreign init response produces ContextMismatch.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.AwaitingInitializationResponse,
                "State preserved after foreign response.");
            Verify(attempt.GetSnapshot().ResponsesHandled == 0, "No responses counted for foreign mismatch.");

            var validResult = attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
            Verify(validResult.Transition == FinTsPinTanReadSessionTransition.DialogueOpen,
                "Subsequent valid response clears mismatch diagnostics and succeeds.");
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanReadSessionIssue.None &&
                attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.None,
                "Transient mismatch issues cleared.");
        }

        // 4. Pre-cancellation
        using (var attempt = New())
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                attempt.StartInitialization(sInitCandidate, cts.Token);
                Verify(false, "Pre-cancellation must throw OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Cancelled && Pending(attempt) is null,
                    "Pre-cancellation sets Cancelled state and clears candidates.");
            }
        }

        // 5. In-flight cancellation across stages
        foreach (string stageName in new[] { "init", "operation", "closing" })
        {
            using var attempt = New();
            attempt.StartInitialization(sInitCandidate);
            if (stageName != "init")
            {
                attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
                attempt.RecordAllAccountCandidate(Bind(sAllAccountCandidate, attempt));
            }
            if (stageName == "closing")
            {
                attempt.AcceptAllAccountResponse(sAllAccountResponse);
                attempt.RecordClosingCandidate(Bind(sClosingCandidate, attempt));
            }

            attempt.Cancel();
            var snap = attempt.GetSnapshot();
            Verify(snap.State == FinTsPinTanReadSessionState.Cancelled && snap.Stage == FinTsPinTanReadSessionStage.Terminal && Pending(attempt) is null,
                $"Cancellation during {stageName} sets Cancelled terminal state.");
        }

        // 6. Stop and Dispose
        using (var attempt = New())
        {
            attempt.StartInitialization(sInitCandidate);
            attempt.Stop();
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Stopped && Pending(attempt) is null,
                "Stop sets Stopped terminal state.");
        }
        using (var attempt = New())
        {
            attempt.StartInitialization(sInitCandidate);
            attempt.Dispose();
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Stopped && Pending(attempt) is null,
                "Dispose sets Stopped terminal state.");
        }

        // 7. Single monotonic deadline: operation does not renew deadline
        {
            var clock = new Clock();
            using var attempt = New(clock);
            attempt.StartInitialization(sInitCandidate);
            clock.Milliseconds = 5000;
            attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
            Verify(attempt.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(5000),
                "Remaining time decreased to 5s at mid-session.");
            clock.Milliseconds = 9999;
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.DialogueOpen,
                "Session still open before deadline.");
            clock.Milliseconds = 10000;
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.TimedOut,
                "Session timed out exactly at 10s monotonic limit.");
            Verify(Pending(attempt) is null, "Timed out session clears pending candidate.");
        }

        // 8. Clock regression triggers ClockInvalid
        {
            var clock = new Clock();
            using var attempt = New(clock);
            attempt.StartInitialization(sInitCandidate);
            clock.Milliseconds = 100;
            _ = attempt.GetSnapshot();
            clock.Milliseconds = 50; // regression
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.ClockInvalid,
                "Backward clock regression transitions to ClockInvalid.");
        }

        // 9. Clock exception triggers ClockInvalid
        {
            var clock = new Clock();
            using var attempt = New(clock);
            attempt.StartInitialization(sInitCandidate);
            clock.AfterReads = 1;
            clock.Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.ClockInvalid,
                "Clock read failure transitions to ClockInvalid.");
        }

        // 10. Constructor lifetime and page bounds
        foreach (var invalidLifetime in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try
            {
                using var attempt = new FinTsPinTanReadSessionAttempt(invalidLifetime);
                Verify(false, "Invalid lifetime must throw ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Invalid lifetime bound enforced.");
            }
        }
        foreach (int invalidPages in new[] { 0, -1, 129 })
        {
            try
            {
                using var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(10), invalidPages);
                Verify(false, "Invalid maximum balance pages must throw ArgumentOutOfRangeException.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Maximum balance pages bounds enforced.");
            }
        }
        try
        {
            using var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(10), 16, new Clock { Frequency = 0 });
            Verify(false, "Zero clock frequency must throw ArgumentException.");
        }
        catch (ArgumentException error)
        {
            Verify(!error.ToString().Contains("PUBLIC-CLOCK-DETAIL", StringComparison.Ordinal),
                "Clock validation uses fixed message without leak.");
        }

        // 11. Multithreaded concurrent responses
        using (var attempt = New())
        {
            attempt.StartInitialization(sInitCandidate);
            attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
            attempt.RecordAllAccountCandidate(Bind(sAllAccountCandidate, attempt));

            var results = new FinTsPinTanReadSessionResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptAllAccountResponse(sAllAccountResponse));
            Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.ExecutionReported) == 1,
                "Concurrent all-account response acceptance yields exactly one ExecutionReported transition.");
            Verify(attempt.GetSnapshot().ResponsesHandled == 2, "Only two responses handled under concurrency.");
            Verify(Pending(attempt) is null, "Candidate cleared after handoff.");
        }

        // 12. Null argument defense
        using (var attempt = New())
        {
            try { attempt.StartInitialization(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null start."); }

            try { attempt.RecordAllAccountCandidate(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null record all account."); }

            try { attempt.RecordBalanceFirstReadCandidate(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null record balance."); }

            try { attempt.RecordBalanceContinuationCandidate(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null record continuation."); }

            try { attempt.RecordClosingCandidate(null!); Verify(false, "Null candidate must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null record closing."); }

            try { attempt.AcceptInitializationResponse((FinTsParameterSet)null!); Verify(false, "Null response must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null response."); }

            try { attempt.AcceptAllAccountResponse(null!); Verify(false, "Null response must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null response."); }

            try { attempt.AcceptBalanceResponse(null!); Verify(false, "Null response must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null response."); }

            try { attempt.AcceptClosingResponse(null!); Verify(false, "Null response must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Ready, "State unchanged after null response."); }
        }

        // 13. Culture invariance
        var cultures = new[] { "de-DE", "tr-TR", "en-US", "ar-SA" };
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                using var attempt = New();
                Verify(attempt.StartInitialization(sInitCandidate) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                    "Start is culture-invariant under " + cultureName);
                var res = attempt.AcceptInitializationResponse(sInitProc, sInitPerm, sInitPinTan, FinTsReadParameterSet.Parse(sInitPinTan.Source));
                Verify(res.Transition == FinTsPinTanReadSessionTransition.DialogueOpen,
                    "Init response is culture-invariant under " + cultureName);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        // 14. Reflection: Snapshot diagnostics strictly scalar
        Verify(typeof(FinTsPinTanReadSessionSnapshot).GetProperties().All(p =>
            p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)),
            "FinTsPinTanReadSessionSnapshot exposes only scalar properties (enums, ints, TimeSpan). No secrets, IBANs, or references.");

        // Every populated stage must release its own references while caller snapshots survive.
        foreach (int vectorIndex in new[] { 0, 3 })
        {
            var vector = vectors[vectorIndex];
            bool balance = vectorIndex == 3;
            var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(vector.GetProperty("initialization"));
            foreach (string ending in new[] { "Cancel", "Stop", "Dispose", "Timeout", "ClockInvalid", "TokenCancellation" })
            {
                for (int stage = 0; stage <= (balance ? 7 : 5); stage++)
                {
                    var clock = new Clock();
                    using var attempt = New(clock);
                    var results = new List<FinTsPinTanReadSessionResult>();
                    var balanceSnapshots = new List<(IReadOnlyList<FinTsBalanceReport> Snapshot, FinTsBalanceReport[] Expected)>();
                    var pageSnapshots = new List<(IReadOnlyList<FinTsPinTanReadContinuationEvidence> Snapshot, FinTsPinTanReadContinuationEvidence[] Expected)>();
                    for (int step = 1; step <= stage; step++)
                    {
                        switch (step)
                        {
                            case 1:
                                Verify(attempt.StartInitialization(binding.Request) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Ownership setup starts initialization.");
                                break;
                            case 2:
                                results.Add(attempt.AcceptInitializationResponse(procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source)));
                                Verify(results[^1].Transition == FinTsPinTanReadSessionTransition.DialogueOpen, "Ownership setup opens dialogue.");
                                break;
                            case 3:
                                var recorded = balance
                                    ? attempt.RecordBalanceFirstReadCandidate(Bind(BalanceFirstReadCandidate(vector.GetProperty("balanceInitial")), attempt))
                                    : attempt.RecordAllAccountCandidate(Bind(AllAccountCandidate(vector.GetProperty("allAccount")), attempt));
                                Verify(recorded == FinTsPinTanReadSessionTransition.CandidateRecorded, "Ownership setup records operation.");
                                break;
                            case 4:
                                results.Add(balance
                                    ? attempt.AcceptBalanceResponse(BalanceResponse(vector.GetProperty("balanceInitial")))
                                    : attempt.AcceptAllAccountResponse(AllAccountResponse(vector.GetProperty("allAccount"))));
                                Verify(results[^1].HasMatchingEvidence, "Ownership setup accepts operation evidence.");
                                break;
                            case 5 when balance:
                                Verify(attempt.RecordBalanceContinuationCandidate(Bind(BalanceContinuationCandidate(vector.GetProperty("balanceContinuations")[0]), attempt)) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Ownership setup records continuation.");
                                break;
                            case 6:
                                results.Add(attempt.AcceptBalanceResponse(BalanceResponse(vector.GetProperty("balanceContinuations")[0])));
                                Verify(results[^1].HasMatchingEvidence, "Ownership setup accepts continuation evidence.");
                                break;
                            default:
                                Verify(attempt.RecordClosingCandidate(ClosingCandidate(vector.GetProperty("closing"), attempt.InitializationEvidence!)) == FinTsPinTanReadSessionTransition.CandidateRecorded, "Ownership setup records closing.");
                                break;
                        }
                        var balances = attempt.AggregatedBalances;
                        balanceSnapshots.Add((balances, balances.ToArray()));
                        var pages = attempt.BalanceContinuationPages;
                        pageSnapshots.Add((pages, pages.ToArray()));
                    }
                    var before = attempt.GetSnapshot();
                    var resultBalances = results.Select(r => r.AggregatedBalances.ToArray()).ToArray();
                    switch (ending)
                    {
                        case "Cancel": attempt.Cancel(); break;
                        case "Stop": attempt.Stop(); break;
                        case "Dispose": attempt.Dispose(); break;
                        case "Timeout":
                            if (stage == 0) { attempt.StartInitialization(binding.Request); before = attempt.GetSnapshot(); }
                            clock.Milliseconds = 10_000;
                            break;
                        case "ClockInvalid":
                            if (stage == 0) { attempt.StartInitialization(binding.Request); before = attempt.GetSnapshot(); }
                            clock.ThrowFrequency = true;
                            break;
                        default:
                            try
                            {
                                attempt.StartInitialization(binding.Request, new CancellationToken(true));
                                Verify(false, "Cancelled token must throw.");
                            }
                            catch (OperationCanceledException) { Verify(true, "Cancelled token terminates ownership."); }
                            break;
                    }
                    var after = attempt.GetSnapshot();
                    var expectedState = ending switch
                    {
                        "Cancel" or "TokenCancellation" => FinTsPinTanReadSessionState.Cancelled,
                        "Timeout" => FinTsPinTanReadSessionState.TimedOut,
                        "ClockInvalid" => FinTsPinTanReadSessionState.ClockInvalid,
                        _ => FinTsPinTanReadSessionState.Stopped,
                    };
                    Verify(after.State == expectedState && after.Stage == FinTsPinTanReadSessionStage.Terminal, "Ownership termination reaches the expected terminal state.");
                    FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                    Verify(after.RequestsRecorded == before.RequestsRecorded && after.ResponsesHandled == before.ResponsesHandled &&
                           after.PagesAccepted == before.PagesAccepted && after.BalancesReported == before.BalancesReported &&
                           after.AccountsDiscovered == before.AccountsDiscovered, "Terminal cleanup preserves scalar progress counts.");
                    Verify(balanceSnapshots.All(s => s.Snapshot.SequenceEqual(s.Expected)) && pageSnapshots.All(s => s.Snapshot.SequenceEqual(s.Expected)),
                        "Caller collection snapshots survive later pages and terminal cleanup unchanged.");
                    Verify(results.All(r => r.HasMatchingEvidence) && results.Select((r, i) => r.AggregatedBalances.SequenceEqual(resultBalances[i])).All(v => v),
                        "Caller result evidence and aggregated balances survive terminal cleanup.");
                    Verify(attempt.CurrentContinuationToken is null && attempt.AggregatedBalances.Count == 0 && attempt.BalanceContinuationPages.Count == 0,
                        "Terminal read accessors expose no retained page or continuation data.");
                    attempt.Dispose();
                    Verify(attempt.StartInitialization(binding.Request) == FinTsPinTanReadSessionTransition.Terminal && attempt.GetSnapshot().State == expectedState,
                        "Cleanup is idempotent and prevents restart.");
                }
            }
        }

        Console.WriteLine($"FinTS assembled PIN/TAN read session attempt passed ({vectors.Length} independent traces, {count} checks).");
    }

    private static FinTsPinTanReadSessionAttempt New(Clock? clock = null, int maximum = 16) =>
        new(TimeSpan.FromSeconds(10), maximum, clock ?? new Clock());

    private static object? Pending(FinTsPinTanReadSessionAttempt attempt) =>
        typeof(FinTsPinTanReadSessionAttempt).GetProperty("PendingCandidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(attempt);

    internal static FinTsPinTanAllAccountRequestBinding AllAccountCandidate(JsonElement element)
    {
        var candidateElement = element.GetProperty("candidate");
        var contextElement = candidateElement.GetProperty("context");
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(contextElement.GetProperty("initialization"));
        var reads = FinTsReadParameterSet.Parse(hipins.Source);
        var initialization = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(binding, tan, permissions, hipins, reads);
        int expectedBank = contextElement.GetProperty("expectedBankMessageNumber").GetInt32();
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(contextElement.GetProperty("requestBase64").GetString()!)), expectedBank);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(contextElement.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var selection = binding.Request.SignatureEvidence.Request.Selection;
        string controlReference = candidateElement.GetProperty("controlReference").GetString()!;
        var signature = FinTsPinTanReadSignatureContext.Evaluate(initialization, request, header, selection, controlReference);
        var context = FinTsPinTanAllAccountCapabilityContext.Evaluate(signature);
        return FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate(context);
    }

    internal static FinTsReadDataSet AllAccountResponse(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("responseBase64").GetString()!));
        var response = element.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
        return FinTsReadDataSet.Parse(response);
    }

    internal static FinTsPinTanReadRequestBinding BalanceFirstReadCandidate(JsonElement element) =>
        FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadPinTrailerTests.Context(element.GetProperty("candidate")));

    internal static FinTsPinTanReadContinuationRequestBinding BalanceContinuationCandidate(JsonElement element) =>
        FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationPinTrailerTests.Context(element.GetProperty("candidate")));

    internal static FinTsReadDataSet BalanceResponse(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("responseBase64").GetString()!));
        var response = element.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
        return FinTsReadDataSet.Parse(response);
    }

    internal static FinTsPinTanDialogueEndRequestBinding ClosingCandidate(JsonElement element, FinTsPinTanInitializationEvidence initEvidence)
    {
        var unsignedClosing = FinTsUnsignedDialogueEndRequest.Parse(
            FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("requestBase64").GetString()!)),
            element.GetProperty("expectedBankMessageNumber").GetInt32());
        var closingHeader = FinTsPinTanSignatureHeader.Parse(
            FinTsSyntax.ParseSegments(Convert.FromBase64String(element.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var closingContext = FinTsPinTanDialogueEndContext.EvaluateForInitialization(
            initEvidence, unsignedClosing, closingHeader, unsignedClosing.Frame.MessageNumber, unsignedClosing.ExpectedBankMessageNumber);
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(closingContext);
    }

    internal static FinTsResponse ClosingResponse(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("responseBase64").GetString()!));
        return element.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
    }

    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal long Frequency { get; set; } = 1000;
        internal bool ThrowFrequency { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => ThrowFrequency ? throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL") : Frequency;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
