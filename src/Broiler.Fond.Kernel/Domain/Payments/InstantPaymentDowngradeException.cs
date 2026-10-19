namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Thrown when an instant SEPA transfer is subjected to an impermissible or silent downgrade
/// to standard SEPA credit transfer without explicit user intervention.
/// </summary>
public sealed class InstantPaymentDowngradeException : InvalidOperationException
{
    public InstantPaymentDowngradeException(string message)
        : base(message)
    {
    }

    public InstantPaymentDowngradeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
