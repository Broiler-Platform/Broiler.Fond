namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Distinguishes asset equity from debt liability within net-worth calculation.
/// </summary>
public enum AccountClassificationKind
{
    /// <summary>Positive equity, deposits, cash, or holdings.</summary>
    Asset = 1,

    /// <summary>Debt, negative overdraft, revolving card balances, or loan obligations.</summary>
    Liability = 2,
}
