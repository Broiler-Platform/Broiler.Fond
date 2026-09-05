namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>Distinguishes bank-supplied balance semantics.</summary>
public enum BalanceKind
{
    /// <summary>The bank supplied a type the current kernel does not understand.</summary>
    Unknown = 0,

    /// <summary>A booked balance.</summary>
    Booked = 1,

    /// <summary>An available balance.</summary>
    Available = 2,

    /// <summary>A separately supplied credit-line value.</summary>
    CreditLine = 3,

    /// <summary>Another explicitly source-labelled balance type.</summary>
    Other = 4,
}
