using System.Collections.Frozen;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Enforces normative payment state machine transitions defined in the product specification.
/// </summary>
public static class PaymentStateMachine
{
    private static readonly FrozenDictionary<PaymentState, FrozenSet<PaymentState>> AllowedTransitions =
        new Dictionary<PaymentState, HashSet<PaymentState>>
        {
            [PaymentState.Draft] = [PaymentState.Validated],
            [PaymentState.Validated] =
            [
                PaymentState.PayeeCheckPending,
                PaymentState.PayeeCheckNotApplicable
            ],
            [PaymentState.PayeeCheckPending] =
            [
                PaymentState.PayeeMatch,
                PaymentState.PayeeCloseMatch,
                PaymentState.PayeeNoMatch,
                PaymentState.PayeeCheckUnavailable,
                PaymentState.Rejected
            ],
            [PaymentState.PayeeMatch] = [PaymentState.AwaitingAuthorization],
            [PaymentState.PayeeCloseMatch] =
            [
                PaymentState.AwaitingAuthorization,
                PaymentState.AuthorizationCancelled
            ],
            [PaymentState.PayeeNoMatch] =
            [
                PaymentState.AwaitingAuthorization,
                PaymentState.AuthorizationCancelled
            ],
            [PaymentState.PayeeCheckNotApplicable] = [PaymentState.AwaitingAuthorization],
            [PaymentState.PayeeCheckUnavailable] =
            [
                PaymentState.AwaitingAuthorization,
                PaymentState.AuthorizationCancelled,
                PaymentState.Rejected
            ],
            [PaymentState.AwaitingAuthorization] =
            [
                PaymentState.Submitting,
                PaymentState.AuthorizationCancelled,
                PaymentState.Rejected
            ],
            [PaymentState.Submitting] =
            [
                PaymentState.Accepted,
                PaymentState.BankPending,
                PaymentState.Rejected,
                PaymentState.Unknown
            ],
            [PaymentState.Accepted] =
            [
                PaymentState.BankPending,
                PaymentState.Reconciled
            ],
            [PaymentState.BankPending] =
            [
                PaymentState.Accepted,
                PaymentState.Rejected,
                PaymentState.Unknown,
                PaymentState.Reconciled
            ],
            [PaymentState.Unknown] =
            [
                PaymentState.Accepted,
                PaymentState.BankPending,
                PaymentState.Rejected,
                PaymentState.Reconciled
            ],
            [PaymentState.AuthorizationCancelled] = [],
            [PaymentState.Rejected] = [],
            [PaymentState.Reconciled] = []
        }.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToFrozenSet());

    private static readonly FrozenSet<PaymentState> TerminalStates =
        new HashSet<PaymentState>
        {
            PaymentState.AuthorizationCancelled,
            PaymentState.Rejected,
            PaymentState.Reconciled
        }.ToFrozenSet();

    /// <summary>
    /// Determines whether a state transition from <paramref name="from"/> to <paramref name="to"/> is valid.
    /// </summary>
    public static bool CanTransition(PaymentState from, PaymentState to)
    {
        return AllowedTransitions.TryGetValue(from, out FrozenSet<PaymentState>? allowed) &&
               allowed.Contains(to);
    }

    /// <summary>
    /// Validates that a transition from <paramref name="from"/> to <paramref name="to"/> is allowed.
    /// Throws <see cref="InvalidPaymentTransitionException"/> if the transition is illegal.
    /// </summary>
    public static void ValidateTransition(PaymentState from, PaymentState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidPaymentTransitionException(from, to);
        }
    }

    /// <summary>
    /// Gets all allowed next states for the given <paramref name="state"/>.
    /// </summary>
    public static IReadOnlySet<PaymentState> GetAllowedNextStates(PaymentState state)
    {
        return AllowedTransitions.TryGetValue(state, out FrozenSet<PaymentState>? allowed)
            ? allowed
            : FrozenSet<PaymentState>.Empty;
    }

    /// <summary>
    /// Indicates whether the specified state is terminal (i.e. has no further valid transitions).
    /// </summary>
    public static bool IsTerminal(PaymentState state) => TerminalStates.Contains(state);

    /// <summary>
    /// Maps a <see cref="VerificationOfPayeeStatus"/> to the corresponding <see cref="PaymentState"/>.
    /// </summary>
    public static PaymentState FromVoPStatus(VerificationOfPayeeStatus status) => status switch
    {
        VerificationOfPayeeStatus.Match => PaymentState.PayeeMatch,
        VerificationOfPayeeStatus.CloseMatch => PaymentState.PayeeCloseMatch,
        VerificationOfPayeeStatus.NoMatch => PaymentState.PayeeNoMatch,
        VerificationOfPayeeStatus.NotApplicable => PaymentState.PayeeCheckNotApplicable,
        VerificationOfPayeeStatus.Unavailable => PaymentState.PayeeCheckUnavailable,
        VerificationOfPayeeStatus.Pending => PaymentState.PayeeCheckPending,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, $"Unrecognized VoP status '{status}'.")
    };
}
