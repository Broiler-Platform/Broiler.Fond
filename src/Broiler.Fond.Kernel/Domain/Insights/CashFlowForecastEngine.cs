namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// On-device deterministic cash-flow forecasting engine.
/// Projects future account balances and detects prospective low-balance dips without conflating estimates with bank facts.
/// </summary>
public static class CashFlowForecastEngine
{
    public const int MinHorizonDays = 7;
    public const int MaxHorizonDays = 365;

    /// <summary>
    /// Generates a day-by-day cash-flow projection and threshold alert report starting from the current booked balance.
    /// </summary>
    public static CashFlowForecastResult GenerateForecast(
        ulong accountId,
        decimal currentBalance,
        IReadOnlyList<RecurringObligation> obligations,
        DateOnly startDate,
        int horizonDays = 60,
        decimal safetyThreshold = 100.00m,
        string currency = "EUR")
    {
        ArgumentNullException.ThrowIfNull(obligations);

        if (horizonDays < MinHorizonDays || horizonDays > MaxHorizonDays)
        {
            throw new ArgumentOutOfRangeException(nameof(horizonDays), $"Forecast horizon must be between {MinHorizonDays} and {MaxHorizonDays} days.");
        }

        DateOnly endDate = startDate.AddDays(horizonDays - 1);
        List<ForecastItem> forecastItems = [];

        // 1. Generate prospective occurrences for active obligations
        foreach (var ob in obligations)
        {
            if (!ob.IsActive)
            {
                continue;
            }

            if (ob.AccountId != accountId)
            {
                continue;
            }

            if (!string.Equals(ob.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DateOnly occ = ob.NextExpectedDate;

            // Fast-forward if initial expected date is in the past
            if (occ < startDate)
            {
                occ = ob.CalculateNextOccurrence(startDate.AddDays(-1));
            }

            while (occ <= endDate)
            {
                if (occ >= startDate)
                {
                    decimal signedAmount = ob.IsCredit ? ob.ExpectedAmount : -ob.ExpectedAmount;
                    forecastItems.Add(new ForecastItem(
                        obligationId: ob.Id,
                        accountId: accountId,
                        expectedDate: occ,
                        amount: signedAmount,
                        currency: currency,
                        counterpartyName: ob.CounterpartyName,
                        category: ob.Category));
                }

                occ = ob.CalculateNextOccurrence(occ);
            }
        }

        forecastItems = forecastItems
            .OrderBy(f => f.ExpectedDate)
            .ThenBy(f => f.ObligationId)
            .ToList();

        // 2. Day-by-day running balance calculation and low-balance alert detection
        decimal runningBalance = currentBalance;
        decimal totalIncome = 0m;
        decimal totalExpenses = 0m;

        List<DailyProjectedBalance> dailyBalances = new(horizonDays);
        List<LowBalanceAlert> alerts = [];
        var itemsByDate = forecastItems.ToLookup(f => f.ExpectedDate);

        for (int d = 0; d < horizonDays; d++)
        {
            DateOnly currentDate = startDate.AddDays(d);
            var dayItems = itemsByDate[currentDate].ToList();

            foreach (var item in dayItems)
            {
                runningBalance += item.Amount;
                if (item.IsCredit)
                {
                    totalIncome += item.Amount;
                }
                else
                {
                    totalExpenses += Math.Abs(item.Amount);
                }
            }

            bool isBelow = runningBalance < safetyThreshold;
            dailyBalances.Add(new DailyProjectedBalance(currentDate, runningBalance, isBelow));

            if (isBelow)
            {
                decimal shortfall = safetyThreshold - runningBalance;
                var triggeringItem = dayItems.LastOrDefault(i => !i.IsCredit) ?? dayItems.LastOrDefault();
                ulong trigId = triggeringItem?.ObligationId ?? 0UL;
                string trigName = triggeringItem?.CounterpartyName ?? "Cumulative Expenses";

                string warning = $"Projected balance ({runningBalance:0.00} {currency}) dips below safety threshold ({safetyThreshold:0.00} {currency}) by {shortfall:0.00} {currency} on {currentDate:yyyy-MM-dd} following debit from {trigName}.";

                alerts.Add(new LowBalanceAlert(
                    Date: currentDate,
                    ProjectedBalance: runningBalance,
                    Threshold: safetyThreshold,
                    Shortfall: shortfall,
                    TriggeringObligationId: trigId,
                    TriggeringCounterparty: trigName,
                    WarningMessage: warning));
            }
        }

        return new CashFlowForecastResult(
            AccountId: accountId,
            Currency: currency.ToUpperInvariant(),
            CurrentBookedBalance: currentBalance,
            StartDate: startDate,
            EndDate: endDate,
            HorizonDays: horizonDays,
            SafetyThreshold: safetyThreshold,
            ProjectedClosingBalance: runningBalance,
            TotalProjectedIncome: totalIncome,
            TotalProjectedExpenses: totalExpenses,
            ForecastItems: forecastItems.AsReadOnly(),
            DailyBalances: dailyBalances.AsReadOnly(),
            LowBalanceAlerts: alerts.AsReadOnly());
    }
}
