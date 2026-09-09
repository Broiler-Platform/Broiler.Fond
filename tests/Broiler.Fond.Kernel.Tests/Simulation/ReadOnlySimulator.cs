using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Accounts;

namespace Broiler.Fond.Kernel.Tests.Simulation;

internal enum SimulatedCommand { Authenticate, ContinueChallenge, ReadAccounts }
internal enum SimulatedReplyKind
{
    Authenticated, ManualChallenge, InvalidCredentials, AccessLocked, Success,
    Partial, Timeout, Maintenance, ChangedParameters, Malformed, TransportFailure,
}
internal enum SimulationState { Ready, ChallengeRequired, Completed, Rejected, Cancelled, TimedOut, Unavailable, InvalidResponse, ReauthenticationRequired }

// RawBankText is deliberately hostile/synthetic test input. It is never an output.
internal sealed record SimulationStep(SimulatedCommand Command, SimulatedReplyKind Reply, string? RawBankText = null)
{
    public override string ToString() => nameof(SimulationStep);
}

internal sealed class SimulationChallenge
{
    internal SimulationChallenge(DateTimeOffset expiresAt) => ExpiresAt = expiresAt;
    internal DateTimeOffset ExpiresAt { get; }
}

internal sealed class SimulationResult
{
    internal SimulationResult(SimulationState state, LocalDiagnosticOutcome outcome, SimulationChallenge? challenge = null,
        IReadOnlyList<AccountRediscoveryDecision>? rediscovery = null, AccountValueProjection? values = null)
    {
        State = state;
        Outcome = outcome;
        Challenge = challenge;
        Rediscovery = rediscovery;
        Values = values;
    }

    internal SimulationState State { get; }
    internal LocalDiagnosticOutcome Outcome { get; }
    internal SimulationChallenge? Challenge { get; }
    internal IReadOnlyList<AccountRediscoveryDecision>? Rediscovery { get; }
    internal AccountValueProjection? Values { get; }
}

/// <summary>
/// Test-only workflow simulator, not a FinTS codec or bank connector. It consumes
/// mutable synthetic PIN/TAN spans and clears them on every exit. No credentials
/// are retained between steps; script outcomes do not validate real credentials.
/// </summary>
internal sealed class ReadOnlySimulator
{
    internal const int MaximumScriptSteps = 16;
    internal const int MaximumCredentialLength = 128;
    private readonly ReadOnlyCollection<SimulationStep> _steps;
    private readonly LocalDiagnosticBuffer _diagnostics;
    private readonly SyntheticAccountFixture _fixture;
    private int _position;
    private SimulationState _state = SimulationState.Ready;
    private SimulationChallenge? _challenge;

    internal ReadOnlySimulator(IEnumerable<SimulationStep> steps, LocalDiagnosticBuffer diagnostics, SyntheticAccountFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(fixture);
        List<SimulationStep> copy = [];
        foreach (SimulationStep step in steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (copy.Count == MaximumScriptSteps || !Enum.IsDefined(step.Command) || !Enum.IsDefined(step.Reply) || step.RawBankText?.Length > 4096)
            {
                throw new ArgumentException("Invalid or over-limit simulation script.");
            }

            copy.Add(step);
        }

        if (copy.Count == 0)
        {
            throw new ArgumentException("A simulation script must contain steps.");
        }

        _steps = copy.AsReadOnly();
        _diagnostics = diagnostics;
        _fixture = fixture;
    }

    internal int ConsumedSteps => _position;
    internal SimulationState State => _state;

    internal SimulationResult Start(Span<char> pin, DateTimeOffset now, CancellationToken token = default)
    {
        try
        {
            if (_state != SimulationState.Ready)
            {
                throw new InvalidOperationException("Simulation session has already started.");
            }

            if (token.IsCancellationRequested)
            {
                return Finish(SimulationState.Cancelled, LocalDiagnosticCode.SessionCancelled, LocalDiagnosticOutcome.Cancelled, now);
            }

            _diagnostics.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, now);
            if (pin.Length is 0 or > MaximumCredentialLength)
            {
                return Finish(SimulationState.Rejected, LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.InvalidCredentials, now);
            }

            return Authenticate(SimulatedCommand.Authenticate, now, token);
        }
        finally
        {
            pin.Clear();
        }
    }

    internal SimulationResult Continue(SimulationChallenge challenge, Span<char> tan, DateTimeOffset now, CancellationToken token = default)
    {
        try
        {
            if (_state != SimulationState.ChallengeRequired || !ReferenceEquals(challenge, _challenge))
            {
                throw new InvalidOperationException("The simulation challenge is not current for this session.");
            }

            if (token.IsCancellationRequested)
            {
                return Cancel(now);
            }

            if (now >= challenge.ExpiresAt)
            {
                return Finish(SimulationState.TimedOut, LocalDiagnosticCode.SessionTimedOut, LocalDiagnosticOutcome.TimedOut, now);
            }

            _challenge = null; // A continuation attempt consumes the challenge once.
            if (tan.Length is 0 or > MaximumCredentialLength)
            {
                return Finish(SimulationState.Rejected, LocalDiagnosticCode.ChallengeContinued, LocalDiagnosticOutcome.InvalidCredentials, now);
            }

            return Authenticate(SimulatedCommand.ContinueChallenge, now, token);
        }
        finally
        {
            tan.Clear();
        }
    }

    internal SimulationResult Cancel(DateTimeOffset now)
    {
        if (_state is not (SimulationState.Ready or SimulationState.ChallengeRequired))
        {
            throw new InvalidOperationException("The simulation session has already ended.");
        }

        return Finish(SimulationState.Cancelled, LocalDiagnosticCode.SessionCancelled, LocalDiagnosticOutcome.Cancelled, now);
    }

    private SimulationResult Authenticate(SimulatedCommand command, DateTimeOffset now, CancellationToken token)
    {
        SimulationStep? step = Take(command);
        if (step?.Reply == SimulatedReplyKind.ManualChallenge && command == SimulatedCommand.Authenticate)
        {
            if (now.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - TimeSpan.FromMinutes(2).Ticks)
            {
                return Finish(SimulationState.TimedOut, LocalDiagnosticCode.SessionTimedOut, LocalDiagnosticOutcome.TimedOut, now);
            }

            _state = SimulationState.ChallengeRequired;
            _challenge = new(now.ToUniversalTime().AddMinutes(2));
            _diagnostics.Record(LocalDiagnosticCode.ChallengeIssued, LocalDiagnosticOutcome.Pending, now);
            return new(_state, LocalDiagnosticOutcome.Pending, _challenge);
        }

        if (step?.Reply != SimulatedReplyKind.Authenticated)
        {
            return Failure(step, now);
        }

        _diagnostics.Record(command == SimulatedCommand.Authenticate ? LocalDiagnosticCode.Authentication : LocalDiagnosticCode.ChallengeContinued,
            LocalDiagnosticOutcome.Succeeded, now);
        if (token.IsCancellationRequested)
        {
            return Finish(SimulationState.Cancelled, LocalDiagnosticCode.SessionCancelled, LocalDiagnosticOutcome.Cancelled, now);
        }

        return Read(now);
    }

    private SimulationResult Read(DateTimeOffset now)
    {
        SimulationStep? step = Take(SimulatedCommand.ReadAccounts);
        if (step?.Reply is not (SimulatedReplyKind.Success or SimulatedReplyKind.Partial))
        {
            return Failure(step, now);
        }

        // Extra steps must never be ignored as a successful script completion.
        if (_position != _steps.Count)
        {
            return Failure(null, now);
        }

        bool partial = step.Reply == SimulatedReplyKind.Partial;
        var rediscovery = AccountRediscoveryPlanner.Plan(_fixture.Bindings, _fixture.Occurrences);
        AccountValueProjection values = AccountValueProjection.Create(_fixture.Values(partial), now, TimeSpan.FromHours(1));
        _state = SimulationState.Completed;
        LocalDiagnosticOutcome outcome = partial ? LocalDiagnosticOutcome.PartialResult : LocalDiagnosticOutcome.Succeeded;
        _diagnostics.Record(LocalDiagnosticCode.ReadCompleted, outcome, now);
        return new(_state, outcome, rediscovery: rediscovery, values: values);
    }

    private SimulationStep? Take(SimulatedCommand command)
    {
        if (_position == _steps.Count || _steps[_position].Command != command)
        {
            return null;
        }

        return _steps[_position++];
    }

    private SimulationResult Failure(SimulationStep? step, DateTimeOffset now) => step?.Reply switch
    {
        SimulatedReplyKind.InvalidCredentials => Finish(SimulationState.Rejected, LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.InvalidCredentials, now),
        SimulatedReplyKind.AccessLocked => Finish(SimulationState.Rejected, LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.AccessLocked, now),
        SimulatedReplyKind.Timeout => Finish(SimulationState.TimedOut, LocalDiagnosticCode.SessionTimedOut, LocalDiagnosticOutcome.TimedOut, now),
        SimulatedReplyKind.Maintenance or SimulatedReplyKind.TransportFailure => Finish(SimulationState.Unavailable, LocalDiagnosticCode.ReadCompleted, LocalDiagnosticOutcome.TemporarilyUnavailable, now),
        SimulatedReplyKind.ChangedParameters => Finish(SimulationState.ReauthenticationRequired, LocalDiagnosticCode.ParametersChanged, LocalDiagnosticOutcome.ReauthenticationRequired, now),
        _ => Finish(SimulationState.InvalidResponse, LocalDiagnosticCode.ResponseRejected, LocalDiagnosticOutcome.MalformedResponse, now),
    };

    private SimulationResult Finish(SimulationState state, LocalDiagnosticCode code, LocalDiagnosticOutcome outcome, DateTimeOffset now)
    {
        _state = state;
        _challenge = null;
        _diagnostics.Record(code, outcome, now);
        return new(state, outcome);
    }
}
