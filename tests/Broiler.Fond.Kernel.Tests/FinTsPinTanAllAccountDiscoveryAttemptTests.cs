using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanAllAccountDiscoveryAttemptTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-all-account-attempt-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        foreach (var vector in vectors)
        {
            var clock = new Clock();
            using var attempt = New(clock);
            var candidate = Candidate(vector);
            var responses = vector.GetProperty("responses").EnumerateArray().Select(Data).ToArray();

            foreach (var step in vector.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                string transition;
                FinTsPinTanAllAccountEvidence? evidence = null;

                switch (step.GetProperty("action").GetString())
                {
                    case "start":
                        transition = attempt.Start(candidate).ToString();
                        break;
                    case "response":
                        var result = attempt.AcceptResponse(responses[step.GetProperty("index").GetInt32()]);
                        transition = result.Transition.ToString();
                        evidence = result.Evidence;
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
                Verify(transition == step.GetProperty("transition").GetString() &&
                    snapshot.State.ToString() == step.GetProperty("state").GetString(),
                    "Independent all-account attempt transition/state: " + vector.GetProperty("name").GetString());
                Verify((evidence is not null) == step.GetProperty("evidence").GetBoolean() &&
                    snapshot.CandidatesRecorded == step.GetProperty("candidates").GetInt32() &&
                    snapshot.ResponsesHandled == step.GetProperty("handled").GetInt32(),
                    "Independent one-time handoff and recorded/handled counts match.");
                Verify(snapshot.State == FinTsPinTanAllAccountDiscoveryState.AwaitingResponse
                    ? ReferenceEquals(Pending(attempt), candidate) && snapshot.RemainingTime == TimeSpan.FromMilliseconds(10000 - clock.Milliseconds)
                    : Pending(attempt) is null && snapshot.RemainingTime == TimeSpan.Zero,
                    "Pending attempts pin the exact candidate and deadline; terminal states release metadata.");

                if (evidence is not null)
                {
                    Verify(evidence.Outcome.ToString() == snapshot.State.ToString() &&
                        ReferenceEquals(evidence.Request, candidate) &&
                        ReferenceEquals(evidence.ReadData, responses[step.GetProperty("index").GetInt32()]),
                        "Each terminal outcome hands out exact caller-owned semantic evidence.");
                    Verify(snapshot.Issues == evidence.Issues &&
                        snapshot.BindingIssues == evidence.Binding.Issues &&
                        snapshot.ResponseIssues == evidence.ResponseIssues,
                        "Snapshots retain only the exact scalar handoff diagnostics.");
                }
            }

            Verify(Pending(attempt) is null, "Every independent terminal trace releases its pending candidate.");
        }

        Verify(vectors.Length == 28, "All twenty-eight independent all-account discovery attempt traces ran.");

        var sample = vectors[0];
        var request = Candidate(sample);
        var data = Data(sample.GetProperty("responses")[0]);

        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsReadDataSet ResponseNamed(string name) => Data(Named(name).GetProperty("responses")[0]);

        using (var attempt = New())
        {
            Verify(attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.Ready &&
                attempt.GetSnapshot().RemainingTime == TimeSpan.Zero &&
                attempt.AcceptResponse(data).Transition == FinTsPinTanAllAccountDiscoveryTransition.WrongState,
                "Ready attempts have no deadline and cannot accept data.");
            Verify(attempt.Start(request) == FinTsPinTanAllAccountDiscoveryTransition.CandidateRecorded &&
                attempt.Start(Candidate(sample)) == FinTsPinTanAllAccountDiscoveryTransition.WrongState &&
                ReferenceEquals(Pending(attempt), request),
                "A second candidate cannot replace the pinned context.");
            var result = attempt.AcceptResponse(data);
            attempt.Cancel();
            attempt.Stop();
            attempt.Dispose();
            Verify(result.Evidence!.HasMatchingEvidence &&
                ReferenceEquals(result.Evidence.ReadData, data) &&
                Pending(attempt) is null,
                "Caller evidence survives terminal pending-reference cleanup.");
            Verify(attempt.AcceptResponse(Data(sample.GetProperty("responses")[0])).Evidence is null &&
                attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.ExecutionReported,
                "Reparsed replies and disposal cannot repeat handoff or alter a completed state.");
        }

        using (var attempt = New())
        {
            attempt.Start(request);
            Verify(attempt.AcceptResponse(ResponseNamed("wrong-dialogue-then-valid")).Transition == FinTsPinTanAllAccountDiscoveryTransition.ContextMismatch &&
                attempt.GetSnapshot().ResponsesHandled == 0 &&
                attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.MessageMismatch,
                "Foreign scope records only transient scalar diagnostics and consumes no response.");
            attempt.AcceptResponse(data);
            Verify(attempt.GetSnapshot().Issues == FinTsPinTanAllAccountIssue.None &&
                attempt.GetSnapshot().BindingIssues == FinTsPinTanResponseBindingIssue.None &&
                attempt.GetSnapshot().ResponseIssues == FinTsReadContextIssue.None,
                "A later valid response clears transient mismatch diagnostics.");
        }

        using (var attempt = New())
        {
            attempt.Start(request);
            var unavailable = attempt.AcceptResponse(ResponseNamed("clean-unavailable-absent"));
            Verify(unavailable.Transition == FinTsPinTanAllAccountDiscoveryTransition.UnavailableReported &&
                unavailable.Evidence!.Outcome == FinTsReadOutcomeObservation.UnavailableReported &&
                Pending(attempt) is null &&
                attempt.Start(request) == FinTsPinTanAllAccountDiscoveryTransition.Terminal &&
                attempt.AcceptResponse(data).Evidence is null,
                "Unavailable handoff releases the candidate from attempt ownership.");
        }

        // Independently exercise every response vector through the real lifecycle
        using (var responsesDoc = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-all-account-response-v1.json")!))
        {
            foreach (var vector in responsesDoc.RootElement.GetProperty("vectors").EnumerateArray())
            {
                var req = Candidate(vector);
                var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
                var resp = vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
                var binding = FinTsPinTanAllAccountResponseBinding.Evaluate(req, resp);

                if (!vector.GetProperty("testSemantics").GetBoolean())
                {
                    continue;
                }

                var incoming = FinTsReadDataSet.Parse(resp);
                using var attempt = New();
                attempt.Start(req);
                var result = attempt.AcceptResponse(incoming);

                const FinTsPinTanResponseBindingIssue foreign =
                    FinTsPinTanResponseBindingIssue.MessageMismatch |
                    FinTsPinTanResponseBindingIssue.MissingSegmentReference |
                    FinTsPinTanResponseBindingIssue.UnknownSegmentReference |
                    FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;

                bool mismatch = (binding.Issues & foreign) != 0;
                Verify(mismatch
                    ? result.Transition == FinTsPinTanAllAccountDiscoveryTransition.ContextMismatch && result.Evidence is null && ReferenceEquals(Pending(attempt), req)
                    : result.Evidence is not null && result.Evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString() && Pending(attempt) is null,
                    "The lifecycle applies real binding and independent semantic expectations: " + vector.GetProperty("name").GetString());
            }
        }

        foreach (var incoming in new[] { data, ResponseNamed("clean-unavailable-absent"), ResponseNamed("missing-envelope"), ResponseNamed("foreign-envelope-user") })
        {
            foreach (int stage in new[] { 1, 2, 3 })
            {
                foreach (string failure in new[] { "expiry", "token", "clock", "regression", "stop", "dispose", "cancel" })
                {
                    var clock = new Clock();
                    using var attempt = New(clock);
                    using var cancellation = new CancellationTokenSource();
                    attempt.Start(request);

                    clock.AfterReads = stage;
                    clock.Action = () =>
                    {
                        if (failure == "expiry") { clock.Milliseconds = 10000; }
                        else if (failure == "token") { cancellation.Cancel(); }
                        else if (failure == "regression") { clock.Milliseconds = -1; }
                        else if (failure == "stop") { attempt.Stop(); }
                        else if (failure == "dispose") { attempt.Dispose(); }
                        else if (failure == "cancel") { attempt.Cancel(); }
                        else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                    };

                    try
                    {
                        var result = attempt.AcceptResponse(incoming, cancellation.Token);
                        Verify(failure != "token" &&
                            result.Transition == FinTsPinTanAllAccountDiscoveryTransition.Terminal &&
                            result.Evidence is null,
                            "Late lifetime/clock/abandonment failure withholds all outcome handoffs.");
                    }
                    catch (OperationCanceledException)
                    {
                        Verify(failure == "token", "Token cancellation withholds all outcome handoffs.");
                    }

                    var expected = failure switch
                    {
                        "expiry" => FinTsPinTanAllAccountDiscoveryState.TimedOut,
                        "token" or "cancel" => FinTsPinTanAllAccountDiscoveryState.Cancelled,
                        "stop" or "dispose" => FinTsPinTanAllAccountDiscoveryState.Stopped,
                        _ => FinTsPinTanAllAccountDiscoveryState.ClockInvalid,
                    };

                    Verify(attempt.GetSnapshot().State == expected &&
                        attempt.GetSnapshot().ResponsesHandled == 0 &&
                        Pending(attempt) is null,
                        "Each processing boundary releases pending references without counting or retaining a response.");
                }
            }
        }

        foreach (bool start in new[] { false, true })
        {
            using var attempt = New();
            using var cancellation = new CancellationTokenSource();
            if (!start) { attempt.Start(request); }
            cancellation.Cancel();

            try
            {
                if (start) { attempt.Start(request, cancellation.Token); }
                else { attempt.AcceptResponse(data, cancellation.Token); }
                Verify(false, "Pre-cancellation must throw.");
            }
            catch (OperationCanceledException)
            {
                Verify(attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.Cancelled &&
                    Pending(attempt) is null,
                    "Pre-cancellation releases ready/active context without a handoff.");
            }
        }

        foreach (int stage in new[] { 1, 2 })
        {
            var clock = new Clock();
            using var attempt = New(clock);
            clock.AfterReads = stage;
            clock.Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL");

            Verify(attempt.Start(request) == FinTsPinTanAllAccountDiscoveryTransition.Terminal &&
                attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.ClockInvalid &&
                attempt.GetSnapshot().CandidatesRecorded == (stage == 1 ? 0 : 1) &&
                Pending(attempt) is null,
                "Both start clock boundaries fail closed without retaining a candidate or clock exception text.");
        }

        var reentrantStartClock = new Clock();
        using (var attempt = New(reentrantStartClock))
        {
            reentrantStartClock.AfterReads = 1;
            reentrantStartClock.Action = attempt.Stop;
            Verify(attempt.Start(request) == FinTsPinTanAllAccountDiscoveryTransition.Terminal &&
                attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.Stopped &&
                attempt.GetSnapshot().CandidatesRecorded == 0 &&
                Pending(attempt) is null,
                "Reentrant abandonment during start cannot resurrect the candidate.");
        }

        foreach (TimeSpan invalid in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), TimeSpan.FromMinutes(15) + TimeSpan.FromTicks(1) })
        {
            try
            {
                using var attempt = new FinTsPinTanAllAccountDiscoveryAttempt(invalid);
                Verify(false, "Invalid lifetime must fail.");
            }
            catch (ArgumentOutOfRangeException)
            {
                Verify(true, "Lifetime bounds are enforced.");
            }
        }

        foreach (bool throws in new[] { false, true })
        {
            try
            {
                using var attempt = New(new Clock { Frequency = 0, ThrowFrequency = throws });
                Verify(false, "Invalid clock must fail.");
            }
            catch (ArgumentException error)
            {
                Verify(error.InnerException is null &&
                    !error.ToString().Contains("PUBLIC-CLOCK-DETAIL", StringComparison.Ordinal),
                    "Clock validation uses fixed diagnostics.");
            }
        }

        using (var attempt = New())
        {
            attempt.Start(request);
            var results = new FinTsPinTanAllAccountDiscoveryResult[16];
            Parallel.For(0, results.Length, i => results[i] = attempt.AcceptResponse(data));
            Verify(results.Count(r => r.Evidence is not null) == 1 &&
                results.Count(r => r.Transition == FinTsPinTanAllAccountDiscoveryTransition.ExecutionReported) == 1 &&
                attempt.GetSnapshot().ResponsesHandled == 1 &&
                Pending(attempt) is null,
                "Concurrent response submissions hand out evidence exactly once.");
        }

        Verify(typeof(FinTsPinTanAllAccountDiscoverySnapshot).GetProperties().All(p =>
            p.PropertyType.IsEnum || p.PropertyType == typeof(int) || p.PropertyType == typeof(TimeSpan)),
            "Snapshots expose only scalar state/count/time/issues, with no token, account, response or credential objects.");

        foreach (bool start in new[] { false, true })
        {
            using var attempt = New();
            try
            {
                if (start) { attempt.Start(null!); }
                else { attempt.AcceptResponse(null!); }
                Verify(false, "Null input must throw.");
            }
            catch (ArgumentNullException)
            {
                Verify(attempt.GetSnapshot().State == FinTsPinTanAllAccountDiscoveryState.Ready &&
                    Pending(attempt) is null,
                    "Null input cannot partially record or close an unused attempt.");
            }
        }

        // Culture invariance check
        var cultures = new[] { "de-DE", "tr-TR", "en-US", "ar-SA" };
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in cultures)
            {
                CultureInfo.CurrentCulture = new CultureInfo(cultureName);
                using var attempt = New();
                Verify(attempt.Start(request) == FinTsPinTanAllAccountDiscoveryTransition.CandidateRecorded,
                    "Start is culture-invariant under " + cultureName);
                var res = attempt.AcceptResponse(data);
                Verify(res.Transition == FinTsPinTanAllAccountDiscoveryTransition.ExecutionReported,
                    "AcceptResponse is culture-invariant under " + cultureName);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }

        Console.WriteLine($"FinTS assembled PIN/TAN all-account discovery attempt passed ({vectors.Length} independent traces, {count} checks).");
    }

    private static FinTsPinTanAllAccountDiscoveryAttempt New(Clock? clock = null) =>
        new(TimeSpan.FromSeconds(10), clock ?? new Clock());

    private static object? Pending(FinTsPinTanAllAccountDiscoveryAttempt attempt) =>
        typeof(FinTsPinTanAllAccountDiscoveryAttempt).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(attempt);

    private static FinTsPinTanAllAccountRequestBinding Candidate(JsonElement vector)
    {
        var candidateElement = vector.GetProperty("candidate");
        var context = Context(candidateElement);
        return FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate(context);
    }

    private static FinTsReadDataSet Data(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!));
        var response = vector.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
        return FinTsReadDataSet.Parse(response);
    }

    private static FinTsPinTanAllAccountCapabilityContext Context(JsonElement candidateElement)
    {
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
        return FinTsPinTanAllAccountCapabilityContext.Evaluate(signature);
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
