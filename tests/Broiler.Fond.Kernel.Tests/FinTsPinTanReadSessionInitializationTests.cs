using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadSessionInitializationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var basic = Fixture("pin-tan-initialization-evidence-v1.json");
        using var procedures = Fixture("pin-tan-initialization-procedures-v1.json");
        using var requirements = Fixture("pin-tan-initialization-requirements-v1.json");
        using var sessions = Fixture("pin-tan-read-session-attempt-v1.json");
        var sessionVectors = sessions.RootElement.GetProperty("vectors");
        var valid = new[]
        {
            Parse(basic.RootElement.GetProperty("vectors")[0]),
            Parse(procedures.RootElement.GetProperty("vectors")[0]),
            Parse(requirements.RootElement.GetProperty("vectors")[0]),
            Parse(sessionVectors[0].GetProperty("initialization")),
        };
        var review = new[]
        {
            Parse(basic.RootElement.GetProperty("vectors")[4]),
            Parse(procedures.RootElement.GetProperty("vectors")[5]),
            Parse(requirements.RootElement.GetProperty("vectors")[7]),
            Parse(requirements.RootElement.GetProperty("vectors")[7]),
        };
        var foreign = Parse(sessionVectors[11].GetProperty("extraResponses")[0]);

        void Empty(FinTsPinTanReadSessionResult result)
        {
            Verify(result.InitializationEvidence is null && result.ProcedureEvidence is null && result.RequirementsEvidence is null && !result.HasMatchingEvidence,
                "A non-handoff result exposes no initialization evidence or qualification.");
        }

        for (int mode = 0; mode < 4; mode++)
            foreach (bool matching in new[] { false, true })
            {
                var input = matching ? valid[mode] : review[mode];
                using var attempt = New();
                var early = Accept(attempt, mode, input);
                Verify(early.Transition == FinTsPinTanReadSessionTransition.WrongState, "Initialization before a recorded request has no handoff.");
                Empty(early);
                attempt.StartInitialization(input.Binding.Request);
                var mismatch = Accept(attempt, mode, foreign);
                Verify(mismatch.Transition == FinTsPinTanReadSessionTransition.ContextMismatch && attempt.GetSnapshot().ResponsesHandled == 0,
                    "A foreign response cannot hand out initialization evidence or consume the request.");
                Empty(mismatch);
                var result = Accept(attempt, mode, input);
                Verify(result.Transition == (matching ? FinTsPinTanReadSessionTransition.DialogueOpen : FinTsPinTanReadSessionTransition.RejectedForReview) && result.HasMatchingEvidence == matching,
                    "Every initialization overload reports its complete qualification, including review.");
                Verify(result.InitializationEvidence is not null && ReferenceEquals(result.InitializationEvidence.Request, input.Binding.Request) &&
                    ReferenceEquals(result.InitializationEvidence.Response, input.Procedures.Source.Source),
                    "The initialization handoff retains the exact recorded request and accepted response source.");
                Verify((result.ProcedureEvidence is not null) == (mode >= 1) && (result.RequirementsEvidence is not null) == (mode >= 2),
                    "Each overload publishes precisely the qualification layers it evaluated.");
                if (mode >= 1)
                {
                    Verify(result.ProcedureEvidence is not null && ReferenceEquals(result.ProcedureEvidence.Initialization, result.InitializationEvidence) &&
                        ReferenceEquals(result.ProcedureEvidence.Procedures, input.Procedures) && ReferenceEquals(result.ProcedureEvidence.Permissions, input.Permissions),
                        "Procedure handoff preserves the same initialization and parameter instances.");
                }
                if (mode >= 2)
                {
                    Verify(result.RequirementsEvidence is not null && ReferenceEquals(result.RequirementsEvidence.Procedures, result.ProcedureEvidence) &&
                        ReferenceEquals(result.RequirementsEvidence.PinTan, input.PinTan) && ReferenceEquals(result.RequirementsEvidence.ReadParameters, mode == 3 ? input.Reads : null),
                        "Requirements and optional read parameters belong to the exact same evidence chain.");
                }
                if (!matching && mode >= 1)
                {
                    Verify(result.InitializationEvidence?.HasMatchingEvidence == true && !result.HasMatchingEvidence,
                        "A matching base initialization cannot hide a rejected procedure or requirements qualification.");
                }
                if (!matching && mode >= 2)
                {
                    Verify(result.ProcedureEvidence?.HasMatchingEvidence == true && !result.HasMatchingEvidence,
                        "Matching procedure evidence cannot hide a rejected HIPINS requirements qualification.");
                }
                if (matching)
                {
                    Verify(ReferenceEquals(result.InitializationEvidence, attempt.InitializationEvidence) && ReferenceEquals(result.ProcedureEvidence, attempt.ProcedureEvidence) &&
                        ReferenceEquals(result.RequirementsEvidence, attempt.RequirementsEvidence), "Returned and session-owned evidence share exact provenance.");
                }
                int handled = attempt.GetSnapshot().ResponsesHandled;
                for (int repeatedMode = 0; repeatedMode < 4; repeatedMode++)
                {
                    var repeated = Accept(attempt, repeatedMode, input);
                    Verify(repeated.Transition == (matching ? FinTsPinTanReadSessionTransition.WrongState : FinTsPinTanReadSessionTransition.Terminal),
                        "Repeated or upgraded initialization overloads cannot produce another handoff.");
                    Empty(repeated);
                }
                Verify(attempt.GetSnapshot().ResponsesHandled == handled, "Non-handoff calls do not change response accounting.");
                if (matching && result.InitializationEvidence is not null)
                {
                    var closing = sessionVectors[5].GetProperty("closing");
                    Verify(attempt.RecordClosingCandidate(ClosingCandidate(closing, result.InitializationEvidence)) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                        "The caller can construct closing directly from the returned initialization evidence.");
                    var closed = attempt.AcceptClosingResponse(ClosingResponse(closing));
                    Verify(closed.Transition == FinTsPinTanReadSessionTransition.Closed && closed.InitializationEvidence is null && closed.ProcedureEvidence is null && closed.RequirementsEvidence is null,
                        "Closing publishes its own evidence without repeating initialization handoffs.");
                }
                attempt.Dispose();
                FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                Verify(result.InitializationEvidence is not null && ReferenceEquals(result.InitializationEvidence.Response, input.Procedures.Source.Source) && result.HasMatchingEvidence == matching,
                    "Caller-owned initialization results survive review, closing and disposal unchanged.");
                Empty(Accept(attempt, mode, input));
            }

        for (int mode = 0; mode < 4; mode++)
        {
            var input = valid[mode];
            foreach (string ending in new[] { "Cancel", "Stop", "Dispose", "Timeout", "ClockInvalid" })
            {
                var clock = new Clock();
                using var attempt = New(clock: clock);
                attempt.StartInitialization(input.Binding.Request);
                var result = Accept(attempt, mode, input);
                switch (ending)
                {
                    case "Cancel": attempt.Cancel(); break;
                    case "Stop": attempt.Stop(); break;
                    case "Dispose": attempt.Dispose(); break;
                    case "Timeout": clock.Timestamp = 10_000; break;
                    default: clock.Throw = true; break;
                }
                Verify(attempt.GetSnapshot().Stage == FinTsPinTanReadSessionStage.Terminal, "Each abandonment path terminates initialization ownership.");
                FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                Verify(result.InitializationEvidence is not null && result.HasMatchingEvidence && ReferenceEquals(result.InitializationEvidence.Request, input.Binding.Request),
                    "Previously handed-out initialization evidence survives every terminal cleanup path.");
                Empty(Accept(attempt, mode, input));
            }

            // ObserveClock runs before binding, after binding and before semantic publication.
            for (int boundary = 1; boundary <= 3; boundary++)
                foreach (bool timeout in new[] { false, true })
                {
                    var clock = new Clock();
                    using var attempt = New(clock: clock);
                    attempt.StartInitialization(input.Binding.Request);
                    clock.AfterReads = boundary;
                    clock.Action = timeout ? () => clock.Timestamp = 10_000 : attempt.Cancel;
                    var result = Accept(attempt, mode, input);
                    Verify(result.Transition == FinTsPinTanReadSessionTransition.Terminal && attempt.GetSnapshot().ResponsesHandled == 0,
                        "Cancellation or deadline expiry before publication suppresses the whole initialization handoff.");
                    Empty(result);
                    FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
                }

            using (var attempt = New())
            {
                attempt.StartInitialization(input.Binding.Request);
                try { Accept(attempt, mode, input, new CancellationToken(true)); Verify(false, "A cancelled initialization token must throw."); }
                catch (OperationCanceledException) { Verify(attempt.GetSnapshot().State == FinTsPinTanReadSessionState.Cancelled, "Every overload honors token cancellation before publication."); }
                FinTsSessionOwnershipTests.VerifyReleased(attempt, Verify);
            }
            using (var attempt = New())
            {
                attempt.StartInitialization(input.Binding.Request);
                var results = new FinTsPinTanReadSessionResult[8];
                Parallel.For(0, results.Length, i => results[i] = Accept(attempt, mode, input));
                Verify(results.Count(r => r.Transition == FinTsPinTanReadSessionTransition.DialogueOpen && r.HasMatchingEvidence && r.InitializationEvidence is not null) == 1 &&
                    attempt.GetSnapshot().ResponsesHandled == 1, "Concurrent initialization submissions publish exactly one complete evidence handoff.");
                foreach (var result in results.Where(r => r.Transition != FinTsPinTanReadSessionTransition.DialogueOpen)) { Empty(result); }
            }
        }
        Console.WriteLine($"FinTS read-session initialization result handoffs passed ({count} checks).");
    }

    private sealed record Inputs(FinTsPinTanResponseBinding Binding, FinTsTanParameterSet Procedures, FinTsPermittedProcedureSet Permissions,
        FinTsPinTanParameterSet PinTan, FinTsReadParameterSet Reads);

    private static Inputs Parse(JsonElement sample)
    {
        var (binding, procedures, permissions, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(sample);
        return new(binding, procedures, permissions, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
    }

    private static FinTsPinTanReadSessionResult Accept(FinTsPinTanReadSessionAttempt attempt, int mode, Inputs input, CancellationToken token = default) => mode switch
    {
        0 => attempt.AcceptInitializationResponse(input.Procedures.Source, token),
        1 => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions, token),
        2 => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions, input.PinTan, token),
        _ => attempt.AcceptInitializationResponse(input.Procedures, input.Permissions, input.PinTan, input.Reads, token),
    };

    private static FinTsPinTanReadSessionAttempt New(Clock? clock = null) => new(TimeSpan.FromSeconds(10), clock: clock ?? new Clock());
    private static JsonDocument Fixture(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs." + name)!);
    private sealed class Clock : TimeProvider
    {
        internal long Timestamp { get; set; }
        internal bool Throw { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp()
        {
            if (--AfterReads == 0) { Action?.Invoke(); }
            return Throw ? throw new InvalidOperationException("PUBLIC-CLOCK-FAILURE") : Timestamp;
        }
    }
}
