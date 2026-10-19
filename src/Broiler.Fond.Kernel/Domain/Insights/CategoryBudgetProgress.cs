using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Detailed, explainable projection of monthly spending progress against a category budget.
/// </summary>
public sealed record CategoryBudgetProgress(
    string Category,
    string Currency,
    int Year,
    int Month,
    decimal BaseLimit,
    decimal RolloverAmount,
    decimal TargetAmount,
    decimal ActualSpent,
    decimal RemainingBudget,
    decimal PercentageConsumed,
    bool IsOverspent,
    decimal OverspentAmount,
    int ContributingTransactionCount,
    IReadOnlyList<TransactionItemRecord> ContributingTransactions)
{
    /// <summary>
    /// Gets whether this category has a zero budget target.
    /// </summary>
    public bool HasNoTarget => TargetAmount <= 0m;
}
