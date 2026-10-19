using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Deterministic statistical detector for recurring income and payment obligations
/// based on historical booked transaction intervals.
/// </summary>
public static class RecurringCadenceDetector
{
    public const decimal MaxAmountVarianceRatio = 0.20m; // 20% variance allowed for variable subscriptions/utilities

    /// <summary>
    /// Analyzes an account's transaction history to identify repeating payment patterns.
    /// Emits unconfirmed candidates with IsUserConfirmed = false.
    /// </summary>
    public static IReadOnlyList<RecurringObligation> DetectObligations(
        ulong accountId,
        IReadOnlyList<TransactionItemRecord> history,
        string currency = "EUR")
    {
        ArgumentNullException.ThrowIfNull(history);

        var eligible = history
            .Where(tx => tx.AccountId == accountId &&
                         string.Equals(tx.Currency, currency, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(tx.Status, "Booked", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (eligible.Count < 2)
        {
            return Array.Empty<RecurringObligation>();
        }

        // Group by normalized counterparty key
        var groups = eligible.GroupBy(tx => GetGroupingKey(tx));
        List<RecurringObligation> candidates = [];
        ulong idCounter = 1;

        foreach (var group in groups)
        {
            if (string.IsNullOrEmpty(group.Key))
            {
                continue;
            }

            var txs = group
                .OrderBy(t => t.BookingDate)
                .ThenBy(t => t.TransactionId)
                .ToList();

            if (txs.Count < 2)
            {
                continue;
            }

            // Check direction consistency
            bool isCredit = txs[0].IsCredit;
            if (txs.Any(t => t.IsCredit != isCredit))
            {
                // Mixed credit and debit under same key; skip or partition
                continue;
            }

            // Compute inter-transaction intervals in days
            List<int> intervals = [];
            for (int i = 0; i < txs.Count - 1; i++)
            {
                int days = txs[i + 1].BookingDate.DayNumber - txs[i].BookingDate.DayNumber;
                if (days > 0)
                {
                    intervals.Add(days);
                }
            }

            if (intervals.Count == 0)
            {
                continue;
            }

            RecurringCadence? detectedCadence = DetectCadence(intervals);
            if (!detectedCadence.HasValue)
            {
                continue;
            }

            // Verify amount stability
            decimal avgAmount = txs.Average(t => Math.Abs(t.Amount));
            if (avgAmount <= 0m)
            {
                continue;
            }

            bool amountsStable = txs.All(t =>
            {
                decimal diff = Math.Abs(Math.Abs(t.Amount) - avgAmount);
                return diff <= (avgAmount * MaxAmountVarianceRatio) || diff <= 5.00m;
            });

            if (!amountsStable)
            {
                continue;
            }

            var lastTx = txs[^1];
            string normalizedName = MerchantNormalizer.Normalize(lastTx.CounterpartyName, lastTx.RemittanceText);
            decimal expectedAmount = Math.Round(avgAmount, 2);
            int expectedDay = lastTx.BookingDate.Day;

            var obligation = new RecurringObligation(
                id: idCounter++,
                accountId: accountId,
                counterpartyName: normalizedName,
                expectedAmount: expectedAmount,
                currency: currency,
                isCredit: isCredit,
                cadence: detectedCadence.Value,
                expectedDayOfMonth: expectedDay,
                counterpartyIban: lastTx.CounterpartyIban,
                category: lastTx.Category,
                isUserConfirmed: false, // auto-detected candidate
                isActive: true,
                lastObservedDate: lastTx.BookingDate);

            // Compute next expected date strictly in the future relative to last observed
            DateOnly nextOccurrence = obligation.CalculateNextOccurrence(lastTx.BookingDate);
            obligation = obligation with { NextExpectedDate = nextOccurrence };

            candidates.Add(obligation);
        }

        return candidates
            .OrderByDescending(c => c.ExpectedAmount)
            .ToList()
            .AsReadOnly();
    }

    private static string GetGroupingKey(TransactionItemRecord tx)
    {
        if (!string.IsNullOrWhiteSpace(tx.CounterpartyIban))
        {
            return "IBAN:" + tx.CounterpartyIban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        }

        string normalized = MerchantNormalizer.Normalize(tx.CounterpartyName, tx.RemittanceText);
        if (!string.IsNullOrWhiteSpace(normalized) && !normalized.Equals("(Unknown Merchant)", StringComparison.OrdinalIgnoreCase))
        {
            return "NAME:" + normalized.ToUpperInvariant();
        }

        return string.Empty;
    }

    private static RecurringCadence? DetectCadence(IReadOnlyList<int> intervals)
    {
        double avg = intervals.Average();

        // 1. Weekly: 6 - 8 days (requires >= 3 intervals)
        if (intervals.Count >= 2 && avg >= 6.0 && avg <= 8.5 && intervals.All(d => d >= 5 && d <= 10))
        {
            return RecurringCadence.Weekly;
        }

        // 2. Bi-Weekly: 13 - 16 days
        if (intervals.Count >= 2 && avg >= 13.0 && avg <= 16.0 && intervals.All(d => d >= 11 && d <= 18))
        {
            return RecurringCadence.BiWeekly;
        }

        // 3. Monthly: 26 - 34 days
        if (intervals.Count >= 2 && avg >= 26.0 && avg <= 34.0 && intervals.All(d => d >= 24 && d <= 36))
        {
            return RecurringCadence.Monthly;
        }

        // 4. Bi-Monthly: 55 - 66 days
        if (intervals.Count >= 1 && avg >= 55.0 && avg <= 66.0 && intervals.All(d => d >= 50 && d <= 70))
        {
            return RecurringCadence.BiMonthly;
        }

        // 5. Quarterly: 85 - 98 days
        if (intervals.Count >= 1 && avg >= 85.0 && avg <= 98.0 && intervals.All(d => d >= 80 && d <= 105))
        {
            return RecurringCadence.Quarterly;
        }

        // 6. Semi-Annually: 175 - 192 days
        if (intervals.Count >= 1 && avg >= 170.0 && avg <= 195.0 && intervals.All(d => d >= 165 && d <= 200))
        {
            return RecurringCadence.SemiAnnually;
        }

        // 7. Annually: 350 - 375 days
        if (intervals.Count >= 1 && avg >= 350.0 && avg <= 375.0 && intervals.All(d => d >= 340 && d <= 385))
        {
            return RecurringCadence.Annually;
        }

        return null;
    }
}
