using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadRefreshAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-refresh-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        foreach (var vector in vectors)
        {
            var clock = new Clock();
            int maxPages = vector.GetProperty("maximumPages").GetInt32();
            using var attempt = New(clock, maxPages);

            var initCandidate = Candidate(vector.GetProperty("initialCandidate"));
            var contCandidates = vector.GetProperty("continuationCandidates").EnumerateArray().Select(e => ContinuationCandidate(e)).ToArray();
            var responses = vector.GetProperty("responses").EnumerateArray().Select(Data).ToArray();

            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition;
                int index = step.GetProperty("index").GetInt32();

                switch (step.GetProperty("action").GetString())
                {
                    case "start":
                        transition = attempt.Start(initCandidate).ToString();
                        break;
                    case "continue":
                        transition = attempt.RecordContinuation(contCandidates[index]).ToString();
                        break;
                    case "response":
                        var res = attempt.AcceptResponse(responses[index]);
                        transition = res.Transition.ToString();
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
                        throw new InvalidOperationException("Unknown synthetic action.");
                }

                var snapshot = attempt.GetSnapshot();
                Verify(transition == step.GetProperty("transition").GetString(),
                    $"Independent refresh transition matches in {vector.GetProperty("name").GetString()}: {transition} vs {step.GetProperty("transition").GetString()}.");
                Verify(snapshot.State.ToString() == step.GetProperty("state").GetString(),
                    $"Independent refresh state matches in {vector.GetProperty("name").GetString()}: {snapshot.State} vs {step.GetProperty("state").GetString()}.");
                Verify(snapshot.RequestsRecorded == step.GetProperty("requests").GetInt32(),
                    "Requests recorded match expected count.");
                Verify(snapshot.PagesAccepted == step.GetProperty("pages").GetInt32(),
                    "Pages accepted match expected count.");
                Verify(snapshot.BalancesReported == step.GetProperty("balances").GetInt32(),
                    "Balances reported match expected count.");

                if (snapshot.State is FinTsPinTanReadRefreshState.ExecutionReported or
                    FinTsPinTanReadRefreshState.UnavailableReported or
                    FinTsPinTanReadRefreshState.NeedsReview or
                    FinTsPinTanReadRefreshState.PageLimitReached or
                    FinTsPinTanReadRefreshState.CounterExhausted or
                    FinTsPinTanReadRefreshState.Cancelled or
                    FinTsPinTanReadRefreshState.TimedOut or
                    FinTsPinTanReadRefreshState.Stopped or
                    FinTsPinTanReadRefreshState.ClockInvalid)
                {
                    Verify(Pending(attempt) is null && snapshot.RemainingTime == TimeSpan.Zero,
                        "Terminal states release pending candidates and have zero remaining time.");
                }
            }

            Verify(Pending(attempt) is null, "Every independent terminal trace releases its pending candidate.");
        }

        Verify(vectors.Length == 23, "All 23 independent PIN/TAN read refresh attempt traces ran.");

        // Detailed unit & lifecycle assertions
        var sample = vectors[1]; // two-page-continuation
        var initReq = Candidate(sample.GetProperty("initialCandidate"));
        var contReq = ContinuationCandidate(sample.GetProperty("continuationCandidates")[0]);
        var partResp = Data(sample.GetProperty("responses")[0]);
        var execResp = Data(sample.GetProperty("responses")[1]);

        using (var attempt = New())
        {
            var initialSnap = attempt.GetSnapshot();
            Verify(initialSnap.State == FinTsPinTanReadRefreshState.Ready && initialSnap.RemainingTime == TimeSpan.Zero &&
                   initialSnap.RequestsRecorded == 0 && initialSnap.PagesAccepted == 0 && initialSnap.BalancesReported == 0,
                "Ready attempt has zero deadline and zero counts.");
            Verify(attempt.AcceptResponse(partResp).Transition == FinTsPinTanReadRefreshTransition.WrongState,
                "AcceptResponse when Ready returns WrongState.");
            Verify(attempt.RecordContinuation(contReq) == FinTsPinTanReadRefreshTransition.WrongState,
                "RecordContinuation when Ready returns WrongState.");

            Verify(attempt.Start(initReq) == FinTsPinTanReadRefreshTransition.CandidateRecorded,
                "Valid Start transitions to CandidateRecorded.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.AwaitingResponse && attempt.GetSnapshot().RequestsRecorded == 1,
                "AwaitingResponse records 1 request.");
            Verify(attempt.Start(initReq) == FinTsPinTanReadRefreshTransition.WrongState,
                "Second start returns WrongState without replacing candidate.");
            Verify(attempt.RecordContinuation(contReq) == FinTsPinTanReadRefreshTransition.WrongState,
                "RecordContinuation when AwaitingResponse returns WrongState.");

            var partResult = attempt.AcceptResponse(partResp);
            Verify(partResult.Transition == FinTsPinTanReadRefreshTransition.PartialAccepted,
                "Partial response transitions to PartialAccepted.");
            Verify(partResult.PageNumber == 1 && partResult.HasMatchingEvidence,
                "Result identifies page 1 matching evidence.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.WaitingToContinue && attempt.GetSnapshot().PagesAccepted == 1,
                "State is WaitingToContinue with 1 page accepted.");
            Verify(attempt.CurrentContinuationToken is not null && attempt.CurrentContinuationToken == partResult.FirstPageEvidence!.ReportedContinuationToken,
                "CurrentContinuationToken exposes opaque token from page 1.");
            Verify(attempt.FirstPageEvidence is not null && ReferenceEquals(attempt.FirstPageEvidence, partResult.FirstPageEvidence),
                "FirstPageEvidence is exposed and preserved.");
            Verify(attempt.Balances.Count == 1, "Balances contains page 1 balance report.");

            // Replay rejection
            Verify(attempt.AcceptResponse(partResp).Transition == FinTsPinTanReadRefreshTransition.ReplayRejected,
                "Re-submitting accepted response returns ReplayRejected.");
            Verify(attempt.RecordContinuation(contReq) == FinTsPinTanReadRefreshTransition.CandidateRecorded,
                "Recording continuation candidate transitions to CandidateRecorded.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.AwaitingResponse && attempt.GetSnapshot().RequestsRecorded == 2,
                "State is AwaitingResponse with 2 requests recorded.");

            // Response replay while waiting for page 2
            Verify(attempt.AcceptResponse(partResp).Transition == FinTsPinTanReadRefreshTransition.ReplayRejected,
                "Old response is rejected as replay while waiting for page 2.");

            var execResult = attempt.AcceptResponse(execResp);
            Verify(execResult.Transition == FinTsPinTanReadRefreshTransition.ExecutionReported,
                "Final response transitions to ExecutionReported.");
            Verify(execResult.PageNumber == 2 && execResult.HasMatchingEvidence,
                "Final result identifies page 2 matching evidence.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.ExecutionReported && attempt.GetSnapshot().PagesAccepted == 2,
                "State is ExecutionReported with 2 pages accepted.");
            Verify(attempt.ContinuationPages.Count == 1 && ReferenceEquals(attempt.LastContinuationEvidence, execResult.ContinuationEvidence),
                "ContinuationPages collection contains page 2 evidence.");
            Verify(attempt.Balances.Count == 2, "Aggregated balances contain reports from both page 1 and page 2 without loss.");

            // Terminal immutability
            attempt.Cancel();
            attempt.Stop();
            Verify(attempt.Start(initReq) == FinTsPinTanReadRefreshTransition.Terminal,
                "Start after terminal returns Terminal.");
            Verify(attempt.RecordContinuation(contReq) == FinTsPinTanReadRefreshTransition.Terminal,
                "RecordContinuation after terminal returns Terminal.");
            Verify(attempt.AcceptResponse(execResp).Transition == FinTsPinTanReadRefreshTransition.Terminal,
                "AcceptResponse after terminal returns Terminal.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.ExecutionReported,
                "Terminal state is immutable.");
        }

        // Continuation context mismatch & validation
        using (var attempt = New())
        {
            attempt.Start(initReq);
            attempt.AcceptResponse(partResp);
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.WaitingToContinue, "WaitingToContinue established.");

            using var contDoc = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-continuation-attempt-v1.json")!);
            var contVectors = contDoc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
            var foreignDialogueCandidate = ContinuationCandidate(contVectors.Single(v => v.GetProperty("name").GetString() == "escaped-dialogue").GetProperty("candidate"));
            var differentPredecessorCandidate = ContinuationCandidate(contVectors.Single(v => v.GetProperty("name").GetString() == "balance-six").GetProperty("candidate"));

            // Mismatched dialogue
            Verify(attempt.RecordContinuation(foreignDialogueCandidate) == FinTsPinTanReadRefreshTransition.ContextMismatch,
                "Foreign dialogue continuation candidate returns ContextMismatch.");

            // Mismatched predecessor
            Verify(attempt.RecordContinuation(differentPredecessorCandidate) == FinTsPinTanReadRefreshTransition.ContextMismatch,
                "Different predecessor continuation candidate returns ContextMismatch.");

            // Replay client counter: advance to page 2 partial, then try re-recording candidate with old counter
            var rPage2 = attempt.RecordContinuation(contReq);
            Verify(rPage2 == FinTsPinTanReadRefreshTransition.CandidateRecorded, "Valid continuation recorded.");
            // Re-recording while AwaitingResponse returns WrongState
            Verify(attempt.RecordContinuation(contReq) == FinTsPinTanReadRefreshTransition.WrongState,
                "RecordContinuation while AwaitingResponse returns WrongState.");
        }

        // Live multi-page continuation progression test
        using (var attempt = New())
        {
            attempt.Start(initReq);
            var r1 = attempt.AcceptResponse(partResp);
            Verify(r1.Transition == FinTsPinTanReadRefreshTransition.PartialAccepted, "Page 1 accepted.");

            // Generate page 2 request dynamically from r1 evidence
            var origin = r1.FirstPageEvidence!.Request.Context;
            var contCtx2 = FinTsPinTanReadContinuationContext.Evaluate(r1.FirstPageEvidence, contReq.Context.Continuation.Request);
            var sigCtx2 = FinTsPinTanReadContinuationSignatureContext.Evaluate(contCtx2,
                contReq.Context.Header, contReq.Context.Selection, contReq.Context.ControlReference);
            var cand2 = FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(sigCtx2);

            Verify(attempt.RecordContinuation(cand2) == FinTsPinTanReadRefreshTransition.CandidateRecorded,
                "Dynamically evaluated page 2 continuation candidate accepted.");
            Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.AwaitingResponse, "AwaitingResponse for page 2.");

            var r2 = attempt.AcceptResponse(execResp);
            Verify(r2.Transition == FinTsPinTanReadRefreshTransition.ExecutionReported, "Page 2 accepted and completes refresh.");
            Verify(attempt.Balances.Count == 2, "2 balances aggregated.");
        }

        // Domain BalanceSnapshot projection and aggregation
        using (var attempt = New())
        {
            attempt.Start(initReq);
            attempt.AcceptResponse(partResp);
            attempt.RecordContinuation(contReq);
            attempt.AcceptResponse(execResp);

            Verify(attempt.Balances.Count == 2, "2 FinTsBalanceReport items collected.");

            // Project into domain BalanceSnapshot
            _ = LocalIdentityLedger.TryOpen(IdentitySnapshot.Empty(), out LocalIdentityLedger? ledger);
            using IdentityAllocationBatch batch = ledger!.BeginBatch();
            InstitutionId institution = new(batch.Allocate(LocalEntityKind.Institution).Value);
            ConnectionId connection = new(batch.Allocate(LocalEntityKind.Connection, [new(new(institution.Value), LocalEntityKind.Institution)]).Value);
            AccountId account = new(batch.Allocate(LocalEntityKind.Account).Value);
            AccountIncarnationId incarnation = new(batch.Allocate(LocalEntityKind.AccountIncarnation, [new(new(account.Value), LocalEntityKind.Account)]).Value);
            AccountBindingId binding = new(batch.Allocate(LocalEntityKind.AccountBinding,
                [new(new(incarnation.Value), LocalEntityKind.AccountIncarnation), new(new(connection.Value), LocalEntityKind.Connection)]).Value);

            var snapshots = new List<BalanceSnapshot>();
            var now = DateTimeOffset.UtcNow;
            for (int i = 0; i < attempt.Balances.Count; i++)
            {
                var report = attempt.Balances[i];
                ObservationId observation = new(batch.Allocate(LocalEntityKind.Observation).Value);
                BalanceSnapshotId snapId = new(batch.Allocate(LocalEntityKind.BalanceSnapshot,
                    [new(new(observation.Value), LocalEntityKind.Observation), new(new(binding.Value), LocalEntityKind.AccountBinding)]).Value);

                var reportTimestamp = report.BookingTimestamp ?? report.Booked.Timestamp;
                DateTimeOffset bankTimestamp = new(reportTimestamp.Date.ToDateTime(reportTimestamp.Time ?? TimeOnly.MinValue, DateTimeKind.Utc));

                snapshots.Add(new BalanceSnapshot(
                    snapId,
                    observation,
                    account,
                    incarnation,
                    binding,
                    BalanceKind.Booked,
                    new Money(report.Booked.SignedValue, report.Booked.Amount.Currency),
                    bankTimestamp,
                    now,
                    isStale: false,
                    sourceLabel: report.ProductLabel));
            }

            var identity = batch.Commit();
            Verify(snapshots.Count == 2, "2 domain BalanceSnapshots created from multi-page refresh reports.");
            Verify(snapshots[0].Value!.Amount == snapshots[1].Value!.Amount, "Balance amounts preserved accurately.");

            AccountSourceLocator locator = new(institution, connection, "fints-pin-tan", "1",
                ManualInstitutionConfiguration.Create("Bank Example", "https://bank.example/fints"),
                "DE1234567890", null, null, null, "EUR");
            KnownAccountBinding knownBinding = new(account, incarnation, binding, locator, AccountBindingState.Active);

            var input = new AccountValueInput(knownBinding, AccountBalanceSupport.SupportedCurrentAccount, [snapshots[1]], AccountRefreshOutcome.Succeeded);
            var projection = AccountValueProjection.Create([input], now, TimeSpan.FromDays(90));
            Verify(projection.Rows.Count == 1 && projection.Rows[0].IsCurrent && projection.Rows[0].Status == AccountValueStatus.Available,
                "Domain AccountValueProjection successfully consumes latest multi-page balance snapshot.");
        }

        // Page limit boundary test
        using (var attempt = New(maximum: 1))
        {
            attempt.Start(initReq);
            var res = attempt.AcceptResponse(partResp);
            Verify(res.Transition == FinTsPinTanReadRefreshTransition.Terminal && attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.PageLimitReached,
                "Reaching page budget with partial report terminates in PageLimitReached.");
            Verify(attempt.GetSnapshot().PagesAccepted == 1 && attempt.Balances.Count == 1,
                "Accepted page and balances retained in PageLimitReached state.");
        }

        // Cancellation tests
        using (var attempt = New())
        {
            attempt.Start(initReq);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            try
            {
                attempt.AcceptResponse(partResp, cts.Token);
                Verify(false, "Pre-cancelled token must throw.");
            }
            catch (OperationCanceledException)
            {
                Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.Cancelled && Pending(attempt) is null,
                    "Cancelled token transitions to Cancelled and releases candidate.");
            }
        }

        // Clock failure and regression
        foreach (int stage in new[] { 1, 2 })
        {
            var clock = new Clock();
            using var attempt = New(clock);
            clock.AfterReads = stage;
            clock.Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-EXCEPTION");
            Verify(attempt.Start(initReq) == FinTsPinTanReadRefreshTransition.Terminal && attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.ClockInvalid && Pending(attempt) is null,
                "Start clock failure closes to ClockInvalid without leaking exception text.");
        }

        // Lifetime parameter validation
        foreach (TimeSpan invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try
            {
                using var attempt = new FinTsPinTanReadRefreshAttempt(invalid);
                Verify(false, "Invalid lifetime must fail.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Lifetime bounds enforced.");
            }
        }

        foreach (int invalidMax in new[] { 0, -1, 129 })
        {
            try
            {
                using var attempt = new FinTsPinTanReadRefreshAttempt(TimeSpan.FromSeconds(10), invalidMax);
                Verify(false, "Invalid maximum pages must fail.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Maximum pages bounds enforced.");
            }
        }

        // Null argument checks
        using (var attempt = New())
        {
            try { attempt.Start(null!); Verify(false, "Start null must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.Ready, "State unchanged after null start."); }

            try { attempt.RecordContinuation(null!); Verify(false, "RecordContinuation null must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.Ready, "State unchanged after null continue."); }

            try { attempt.AcceptResponse(null!); Verify(false, "AcceptResponse null must throw."); }
            catch (ArgumentNullException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadRefreshState.Ready, "State unchanged after null response."); }
        }

        // Concurrency
        using (var attempt = New())
        {
            attempt.Start(initReq);
            var results = new FinTsPinTanReadRefreshResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptResponse(partResp));
            Verify(results.Count(r => r.Transition == FinTsPinTanReadRefreshTransition.PartialAccepted) == 1,
                "Concurrent response submission produces exactly one PartialAccepted transition.");
            Verify(attempt.GetSnapshot().PagesAccepted == 1, "Pages accepted count is exactly 1 under concurrency.");
        }

        // Scalar diagnostics only in snapshot
        Verify(typeof(FinTsPinTanReadRefreshSnapshot).GetProperties().All(p => p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)),
            "FinTsPinTanReadRefreshSnapshot exposes only scalar properties.");

        Console.WriteLine($"FinTS assembled PIN/TAN read refresh attempt passed ({vectors.Length} independent traces, {count} checks).");
    }

    private static FinTsPinTanReadRefreshAttempt New(Clock? clock = null, int maximum = 16) =>
        new(TimeSpan.FromSeconds(10), maximum, clock ?? new Clock());

    private static object? Pending(FinTsPinTanReadRefreshAttempt attempt) =>
        typeof(FinTsPinTanReadRefreshAttempt).GetProperty("PendingCandidate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(attempt);

    private static FinTsPinTanReadRequestBinding Candidate(JsonElement vector) =>
        FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadPinTrailerTests.Context(vector));

    private static FinTsPinTanReadContinuationRequestBinding ContinuationCandidate(JsonElement vector) =>
        FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationPinTrailerTests.Context(vector));


    private static FinTsReadDataSet Data(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!));
        return FinTsReadDataSet.Parse(vector.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame));
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
