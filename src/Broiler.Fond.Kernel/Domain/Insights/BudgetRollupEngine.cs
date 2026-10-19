using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Split-aware, deterministic engine for calculating monthly spending targets,
/// category rollups, and multi-month rollover budgets.
/// </summary>
public static class BudgetRollupEngine
{
    /// <summary>
    /// Calculates category budget progress for a single budget target and calendar month.
    /// Supports exact split-item attribution and optional unspent rollover from prior months.
    /// </summary>
    public static CategoryBudgetProgress CalculateMonthlyProgress(
        BudgetTarget target,
        IReadOnlyList<TransactionItemRecord> allTransactions,
        int year,
        int month,
        decimal rolloverAmount = 0m)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(allTransactions);

        DateOnly monthStart = new(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);
        DateOnly monthEnd = new(year, month, daysInMonth);

        decimal actualSpent = 0m;
        List<TransactionItemRecord> contributing = [];

        foreach (var tx in allTransactions)
        {
            if (tx.BookingDate < monthStart || tx.BookingDate > monthEnd)
            {
                continue;
            }

            if (!string.Equals(tx.Currency, target.Currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Exclude internal own-account transfers if categorized as such
            if (string.Equals(tx.Category, "Own-Account Transfer", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tx.Category, "Umbuchung", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tx.IsSplit && tx.Splits is not null)
            {
                decimal splitSpentForCategory = 0m;
                foreach (var split in tx.Splits)
                {
                    if (string.Equals(split.Category, target.Category, StringComparison.OrdinalIgnoreCase))
                    {
                        // Add positive magnitude of debit expense
                        splitSpentForCategory += Math.Abs(split.Amount);
                    }
                }

                if (splitSpentForCategory > 0m)
                {
                    actualSpent += splitSpentForCategory;
                    contributing.Add(tx);
                }
            }
            else if (!tx.IsCredit && string.Equals(tx.Category, target.Category, StringComparison.OrdinalIgnoreCase))
            {
                actualSpent += Math.Abs(tx.Amount);
                contributing.Add(tx);
            }
        }

        decimal baseLimit = target.MonthlyLimit;
        decimal targetAmount = baseLimit + rolloverAmount;
        decimal remaining = targetAmount - actualSpent;
        decimal percentage = targetAmount > 0m
            ? Math.Round(actualSpent / targetAmount * 100m, 2)
            : 0m;
        bool isOverspent = actualSpent > targetAmount;
        decimal overspentAmount = isOverspent ? actualSpent - targetAmount : 0m;

        return new CategoryBudgetProgress(
            Category: target.Category,
            Currency: target.Currency,
            Year: year,
            Month: month,
            BaseLimit: baseLimit,
            RolloverAmount: rolloverAmount,
            TargetAmount: targetAmount,
            ActualSpent: actualSpent,
            RemainingBudget: remaining,
            PercentageConsumed: percentage,
            IsOverspent: isOverspent,
            OverspentAmount: overspentAmount,
            ContributingTransactionCount: contributing.Count,
            ContributingTransactions: contributing.AsReadOnly());
    }

    /// <summary>
    /// Calculates cumulative unspent rollover amount across consecutive months up to the target month.
    /// </summary>
    public static decimal CalculateCumulativeRollover(
        BudgetTarget target,
        IReadOnlyList<TransactionItemRecord> allTransactions,
        int startYear,
        int startMonth,
        int targetYear,
        int targetMonth)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(allTransactions);

        if (!target.AllowRollover)
        {
            return 0m;
        }

        DateTime current = new(startYear, startMonth, 1);
        DateTime targetDt = new(targetYear, targetMonth, 1);

        if (current >= targetDt)
        {
            return 0m;
        }

        decimal cumulativeRollover = 0m;

        while (current < targetDt)
        {
            var progress = CalculateMonthlyProgress(target, allTransactions, current.Year, current.Month, cumulativeRollover);
            // Positive unspent remainder rolls over to next month
            cumulativeRollover = Math.Max(0m, progress.RemainingBudget);
            current = current.AddMonths(1);
        }

        return cumulativeRollover;
    }

    /// <summary>
    /// Computes spending rollups across all active budget targets for a calendar month,
    /// plus any unbudgeted categories that experienced spending in that month.
    /// </summary>
    public static IReadOnlyList<CategoryBudgetProgress> CalculateAllBudgets(
        IReadOnlyList<BudgetTarget> targets,
        IReadOnlyList<TransactionItemRecord> allTransactions,
        int year,
        int month,
        string currency = "EUR")
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(allTransactions);

        List<CategoryBudgetProgress> results = [];
        HashSet<string> budgetedCategories = new(StringComparer.OrdinalIgnoreCase);

        // 1. Process explicit budget targets
        foreach (var target in targets)
        {
            if (!string.Equals(target.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!target.IsEffectiveForMonth(year, month))
            {
                continue;
            }

            decimal rollover = 0m;
            if (target.AllowRollover)
            {
                rollover = CalculateCumulativeRollover(
                    target,
                    allTransactions,
                    target.EffectiveFrom.Year,
                    target.EffectiveFrom.Month,
                    year,
                    month);
            }

            var progress = CalculateMonthlyProgress(target, allTransactions, year, month, rollover);
            results.Add(progress);
            budgetedCategories.Add(target.Category);
        }

        // 2. Discover unbudgeted categories with expenditures in this month
        DateOnly monthStart = new(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);
        DateOnly monthEnd = new(year, month, daysInMonth);

        Dictionary<string, (decimal Total, List<TransactionItemRecord> Txs)> unbudgeted = new(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in allTransactions)
        {
            if (tx.BookingDate < monthStart || tx.BookingDate > monthEnd)
            {
                continue;
            }

            if (!string.Equals(tx.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tx.IsSplit && tx.Splits is not null)
            {
                foreach (var split in tx.Splits)
                {
                    if (!budgetedCategories.Contains(split.Category))
                    {
                        if (!unbudgeted.TryGetValue(split.Category, out var entry))
                        {
                            entry = (0m, new List<TransactionItemRecord>());
                            unbudgeted[split.Category] = entry;
                        }
                        unbudgeted[split.Category] = (entry.Total + Math.Abs(split.Amount), entry.Txs);
                        if (!entry.Txs.Contains(tx))
                        {
                            entry.Txs.Add(tx);
                        }
                    }
                }
            }
            else if (!tx.IsCredit && !string.IsNullOrWhiteSpace(tx.Category) && !budgetedCategories.Contains(tx.Category))
            {
                if (!unbudgeted.TryGetValue(tx.Category, out var entry))
                {
                    entry = (0m, new List<TransactionItemRecord>());
                    unbudgeted[tx.Category] = entry;
                }
                unbudgeted[tx.Category] = (entry.Total + Math.Abs(tx.Amount), entry.Txs);
                if (!entry.Txs.Contains(tx))
                {
                    entry.Txs.Add(tx);
                }
            }
        }

        foreach (var (cat, (spent, txs)) in unbudgeted)
        {
            results.Add(new CategoryBudgetProgress(
                Category: cat,
                Currency: currency,
                Year: year,
                Month: month,
                BaseLimit: 0m,
                RolloverAmount: 0m,
                TargetAmount: 0m,
                ActualSpent: spent,
                RemainingBudget: -spent,
                PercentageConsumed: 0m,
                IsOverspent: true,
                OverspentAmount: spent,
                ContributingTransactionCount: txs.Count,
                ContributingTransactions: txs.AsReadOnly()));
        }

        return results
            .OrderByDescending(r => r.TargetAmount)
            .ThenByDescending(r => r.ActualSpent)
            .ToList()
            .AsReadOnly();
    }
}
