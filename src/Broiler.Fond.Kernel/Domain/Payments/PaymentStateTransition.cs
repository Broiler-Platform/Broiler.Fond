namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Append-only audit record documenting an individual payment state transition.
/// Never contains authentication secrets, PINs, or TANs.
/// </summary>
public sealed record PaymentStateTransition
{
    public ulong Id { get; init; }
    public ulong AttemptId { get; init; }
    public PaymentState FromState { get; init; }
    public PaymentState ToState { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string Reason { get; init; }
    public string? BankCode { get; init; }

    public PaymentStateTransition(
        ulong id,
        ulong attemptId,
        PaymentState fromState,
        PaymentState toState,
        DateTimeOffset timestamp,
        string reason,
        string? bankCode = null)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Transition ID must be greater than zero.");
        }

        if (attemptId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptId), "Attempt ID must be greater than zero.");
        }

        if (!Enum.IsDefined(fromState))
        {
            throw new ArgumentOutOfRangeException(nameof(fromState), fromState, "Undefined source state.");
        }

        if (!Enum.IsDefined(toState))
        {
            throw new ArgumentOutOfRangeException(nameof(toState), toState, "Undefined target state.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Transition reason cannot be null or whitespace.", nameof(reason));
        }

        Id = id;
        AttemptId = attemptId;
        FromState = fromState;
        ToState = toState;
        Timestamp = timestamp;
        Reason = reason;
        BankCode = bankCode;
    }

    /// <summary>
    /// Factory creating a validated transition record from an existing attempt to a new state.
    /// </summary>
    public static PaymentStateTransition Record(
        ulong transitionId,
        PaymentAttempt attempt,
        PaymentState newState,
        DateTimeOffset timestamp,
        string reason,
        string? bankCode = null)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        PaymentStateMachine.ValidateTransition(attempt.State, newState);

        return new PaymentStateTransition(
            id: transitionId,
            attemptId: attempt.Id,
            fromState: attempt.State,
            toState: newState,
            timestamp: timestamp,
            reason: reason,
            bankCode: bankCode);
    }
}
