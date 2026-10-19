namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Daily snapshot of projected balance over the forecast timeline.
/// </summary>
public sealed record DailyProjectedBalance(
    DateOnly Date,
    decimal ProjectedBalance,
    bool IsBelowThreshold);

/// <summary>
/// Proactive alert triggered when a projected cash-flow dip breaches the user's safety threshold.
/// </summary>
public sealed record LowBalanceAlert(
    DateOnly Date,
    decimal ProjectedBalance,
    decimal Threshold,
    decimal Shortfall,
    ulong TriggeringObligationId,
    string TriggeringCounterparty,
    string WarningMessage);

/// <summary>
/// Complete outcome of a cash-flow projection run over a configured time horizon.
/// </summary>
public sealed record CashFlowForecastResult(
    ulong AccountId,
    string Currency,
    decimal CurrentBookedBalance,
    DateOnly StartDate,
    DateOnly EndDate,
    int HorizonDays,
    decimal SafetyThreshold,
    decimal ProjectedClosingBalance,
    decimal TotalProjectedIncome,
    decimal TotalProjectedExpenses,
    IReadOnlyList<ForecastItem> ForecastItems,
    IReadOnlyList<DailyProjectedBalance> DailyBalances,
    IReadOnlyList<LowBalanceAlert> LowBalanceAlerts)
{
    public bool HasLowBalanceAlerts => LowBalanceAlerts.Count > 0;
}
