using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Calculates point-in-time and historical net-worth snapshots across multiple accounts,
/// strictly isolating totals by ISO currency and accurately distinguishing assets from liabilities.
/// </summary>
public static class NetWorthHistoryCalculator
{
    /// <summary>
    /// Calculates the net-worth snapshot across all specified accounts as of a specific point in time.
    /// </summary>
    public static NetWorthSnapshot CalculateSnapshot(
        DateTimeOffset pointInTime,
        IEnumerable<ExtendedAccountProduct> accounts,
        IEnumerable<BalanceSnapshot>? snapshots = null,
        IEnumerable<TransactionItemRecord>? transactions = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        List<ExtendedAccountProduct> accountList = accounts.ToList();
        List<BalanceSnapshot> snapshotList = snapshots?.ToList() ?? [];
        List<TransactionItemRecord> transactionList = transactions?.ToList() ?? [];

        List<NetWorthAccountBalance> constituentBalances = [];

        foreach (ExtendedAccountProduct account in accountList)
        {
            // 1. Locate the latest booked balance snapshot at or prior to pointInTime
            BalanceSnapshot? latestSnapshot = snapshotList
                .Where(s => s.AccountId == account.Id &&
                            s.Kind == BalanceKind.Booked &&
                            (s.BankTimestamp ?? s.RetrievedAt) <= pointInTime)
                .OrderByDescending(s => s.BankTimestamp ?? s.RetrievedAt)
                .ThenByDescending(s => s.Id.Value)
                .FirstOrDefault();

            Money balance;
            BalanceSnapshotId? sourceSnapshotId = null;
            DateTimeOffset effectiveDate;

            if (latestSnapshot is not null)
            {
                decimal baseAmount = latestSnapshot.Value?.Amount ?? 0m;
                DateTimeOffset snapshotTime = latestSnapshot.BankTimestamp ?? latestSnapshot.RetrievedAt;
                sourceSnapshotId = latestSnapshot.Id;

                // Adjust with intermediate transactions occurring between snapshot and pointInTime
                if (transactionList.Count > 0)
                {
                    var subsequentTransactions = transactionList
                        .Where(t => t.AccountId == account.Id.Value &&
                                    !string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                        .Select(t => (Transaction: t, Timestamp: GetTransactionTimestamp(t)))
                        .Where(pair => pair.Timestamp > snapshotTime && pair.Timestamp <= pointInTime)
                        .OrderBy(pair => pair.Timestamp);

                    foreach (var pair in subsequentTransactions)
                    {
                        if (pair.Transaction.IsCredit)
                        {
                            baseAmount += pair.Transaction.Amount;
                        }
                        else
                        {
                            baseAmount -= pair.Transaction.Amount;
                        }
                    }
                }

                balance = new Money(baseAmount, account.Currency);
                effectiveDate = snapshotTime;
            }
            else
            {
                // No prior snapshot found. Check if transactions exist up to pointInTime
                var priorTransactions = transactionList
                    .Where(t => t.AccountId == account.Id.Value &&
                                !string.Equals(t.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                    .Select(t => (Transaction: t, Timestamp: GetTransactionTimestamp(t)))
                    .Where(pair => pair.Timestamp <= pointInTime)
                    .OrderBy(pair => pair.Timestamp)
                    .ToList();

                if (priorTransactions.Count > 0)
                {
                    decimal baseAmount = 0m;
                    foreach (var pair in priorTransactions)
                    {
                        if (pair.Transaction.IsCredit)
                        {
                            baseAmount += pair.Transaction.Amount;
                        }
                        else
                        {
                            baseAmount -= pair.Transaction.Amount;
                        }
                    }

                    balance = new Money(baseAmount, account.Currency);
                    effectiveDate = pointInTime;
                }
                else if (account.BookedBalance is not null && pointInTime >= (account.LastRefreshedAt ?? DateTimeOffset.MinValue))
                {
                    // Fall back to account's current booked balance
                    balance = account.BookedBalance;
                    effectiveDate = account.LastRefreshedAt ?? pointInTime;
                }
                else
                {
                    // No history available before pointInTime
                    balance = new Money(0m, account.Currency);
                    effectiveDate = pointInTime;
                }
            }

            // 2. Classify balance as Asset or Liability
            AccountClassificationKind classification = ClassifyBalance(account.ProductCategory, balance.Amount);

            constituentBalances.Add(new NetWorthAccountBalance(
                account.Id,
                account.DisplayName,
                account.ProductCategory,
                classification,
                balance,
                account.CreditLine,
                sourceSnapshotId,
                effectiveDate,
                account.IsOfflineManual));
        }

        // 3. Group balances strictly by currency and compute isolated summaries
        Dictionary<string, List<NetWorthAccountBalance>> byCurrency = new(StringComparer.Ordinal);
        foreach (NetWorthAccountBalance item in constituentBalances)
        {
            if (!byCurrency.TryGetValue(item.Balance.Currency, out List<NetWorthAccountBalance>? list))
            {
                list = [];
                byCurrency.Add(item.Balance.Currency, list);
            }
            list.Add(item);
        }

        List<CurrencyNetWorthSummary> summaries = [];
        foreach ((string currency, List<NetWorthAccountBalance> accountsInCurrency) in byCurrency.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            summaries.Add(new CurrencyNetWorthSummary(currency, accountsInCurrency));
        }

        return new NetWorthSnapshot(pointInTime, summaries);
    }

    /// <summary>
    /// Calculates net-worth snapshots across a sequence of discrete evaluation points in time.
    /// </summary>
    public static IReadOnlyList<NetWorthSnapshot> CalculateHistory(
        IEnumerable<DateTimeOffset> evaluationPoints,
        IEnumerable<ExtendedAccountProduct> accounts,
        IEnumerable<BalanceSnapshot>? snapshots = null,
        IEnumerable<TransactionItemRecord>? transactions = null)
    {
        ArgumentNullException.ThrowIfNull(evaluationPoints);
        ArgumentNullException.ThrowIfNull(accounts);

        List<ExtendedAccountProduct> accountList = accounts.ToList();
        List<BalanceSnapshot> snapshotList = snapshots?.ToList() ?? [];
        List<TransactionItemRecord> transactionList = transactions?.ToList() ?? [];

        List<NetWorthSnapshot> history = [];
        foreach (DateTimeOffset point in evaluationPoints.OrderBy(p => p))
        {
            history.Add(CalculateSnapshot(point, accountList, snapshotList, transactionList));
        }

        return history.AsReadOnly();
    }

    /// <summary>
    /// Calculates month-end net-worth snapshots for a given inclusive monthly range.
    /// </summary>
    public static IReadOnlyList<NetWorthSnapshot> CalculateMonthlyHistory(
        DateOnly startMonth,
        DateOnly endMonth,
        IEnumerable<ExtendedAccountProduct> accounts,
        IEnumerable<BalanceSnapshot>? snapshots = null,
        IEnumerable<TransactionItemRecord>? transactions = null)
    {
        if (endMonth < startMonth)
        {
            throw new ArgumentException("End month cannot be earlier than start month.", nameof(endMonth));
        }

        List<DateTimeOffset> points = [];
        DateOnly current = new(startMonth.Year, startMonth.Month, 1);
        DateOnly stop = new(endMonth.Year, endMonth.Month, 1);

        while (current <= stop)
        {
            int daysInMonth = DateTime.DaysInMonth(current.Year, current.Month);
            DateTimeOffset monthEnd = new(new DateTime(current.Year, current.Month, daysInMonth, 23, 59, 59), TimeSpan.Zero);
            points.Add(monthEnd);
            current = current.AddMonths(1);
        }

        return CalculateHistory(points, accounts, snapshots, transactions);
    }

    private static AccountClassificationKind ClassifyBalance(AccountProductCategory category, decimal balanceAmount)
    {
        if (category == AccountProductCategory.Loan)
        {
            return AccountClassificationKind.Liability;
        }

        if (category is AccountProductCategory.Savings or AccountProductCategory.ManualCash or AccountProductCategory.Depot)
        {
            return AccountClassificationKind.Asset;
        }

        // Current, CreditCard, Other
        return balanceAmount < 0m ? AccountClassificationKind.Liability : AccountClassificationKind.Asset;
    }

    private static DateTimeOffset GetTransactionTimestamp(TransactionItemRecord tx)
    {
        if (tx.BankTimestamp.HasValue)
        {
            return tx.BankTimestamp.Value;
        }

        DateTime dt = tx.BookingDate.ToDateTime(new TimeOnly(12, 0, 0));
        return new DateTimeOffset(dt, TimeSpan.Zero);
    }
}
