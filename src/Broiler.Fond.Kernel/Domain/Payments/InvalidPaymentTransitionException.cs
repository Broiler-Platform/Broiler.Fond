namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Domain exception thrown when an illegal payment state transition is attempted.
/// </summary>
public sealed class InvalidPaymentTransitionException : InvalidOperationException
{
    /// <summary>The state from which the transition was attempted.</summary>
    public PaymentState FromState { get; }

    /// <summary>The target state that was disallowed.</summary>
    public PaymentState ToState { get; }

    public InvalidPaymentTransitionException(PaymentState fromState, PaymentState toState, string? message = null)
        : base(message ?? $"Invalid payment state transition from '{fromState}' to '{toState}'.")
    {
        FromState = fromState;
        ToState = toState;
    }
}
