namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Categorizes financial accounts into specific product types for presentation,
/// capability qualification, and net-worth asset/liability classification.
/// </summary>
public enum AccountProductCategory
{
    /// <summary>Bank-synced payment / checking account (Girokonto).</summary>
    Current = 0,

    /// <summary>Read-only bank-synced savings account (Tagesgeld, Festgeld, Sparbuch).</summary>
    Savings = 1,

    /// <summary>Bank-synced credit card account with revolving balance.</summary>
    CreditCard = 2,

    /// <summary>Bank-synced consumer loan or mortgage debt account.</summary>
    Loan = 3,

    /// <summary>User-managed offline manual cash tracking (physical wallet, safe, petty cash).</summary>
    ManualCash = 4,

    /// <summary>Read-only securities custody / investment depot shell.</summary>
    Depot = 5,

    /// <summary>Other or unclassified account product.</summary>
    Other = 6,
}
