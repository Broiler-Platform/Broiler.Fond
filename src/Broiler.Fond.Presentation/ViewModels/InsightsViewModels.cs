using System.Globalization;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Presentation.Mvvm;

namespace Broiler.Fond.Presentation.ViewModels;

/// <summary>
/// Presentation view model for category budget progress and threshold tracking.
/// </summary>
public sealed class CategoryBudgetViewModel : ObservableObject
{
    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    public CategoryBudgetViewModel(CategoryBudgetProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Category = progress.Category;
        Currency = progress.Currency;
        Year = progress.Year;
        Month = progress.Month;
        TargetAmount = progress.TargetAmount;
        ActualSpent = progress.ActualSpent;
        RemainingBudget = progress.RemainingBudget;
        PercentageConsumed = progress.PercentageConsumed;
        IsOverspent = progress.IsOverspent;
        OverspentAmount = progress.OverspentAmount;
        ContributingTransactionCount = progress.ContributingTransactionCount;
        IsNearLimit = !progress.IsOverspent && progress.PercentageConsumed >= 80m;
    }

    public string Category { get; }
    public string Currency { get; }
    public int Year { get; }
    public int Month { get; }
    public DateOnly MonthDate => new(Year, Month, 1);
    public decimal TargetAmount { get; }
    public decimal ActualSpent { get; }
    public decimal RemainingBudget { get; }
    public decimal PercentageConsumed { get; }
    public bool IsOverspent { get; }
    public bool IsOverBudget => IsOverspent;
    public decimal OverspentAmount { get; }
    public int ContributingTransactionCount { get; }
    public bool IsNearLimit { get; }

    public string TargetAmountDisplay => $"{TargetAmount.ToString("N2", DeCulture)} {Currency}";
    public string SpentAmountDisplay => $"{ActualSpent.ToString("N2", DeCulture)} {Currency}";
    public string RemainingAmountDisplay => $"{RemainingBudget.ToString("N2", DeCulture)} {Currency}";
    public string PercentageUsedDisplay => $"{PercentageConsumed:F1}%";

    public string StatusBadge => IsOverspent
        ? "[⚠ Over Budget]"
        : (IsNearLimit ? "[⚡ Approaching Limit]" : "[✓ Within Budget]");
}

/// <summary>
/// Presentation view model for a cash-flow projection item.
/// </summary>
public sealed class CashFlowForecastItemViewModel : ObservableObject
{
    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    public CashFlowForecastItemViewModel(ForecastItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ObligationId = item.ObligationId;
        AccountId = item.AccountId;
        ExpectedDate = item.ExpectedDate;
        Amount = item.Amount;
        Currency = item.Currency;
        CounterpartyName = item.CounterpartyName;
        Category = item.Category;
        IsCredit = item.IsCredit;
        IsEstimate = item.IsEstimate;
        DisplayLabel = item.DisplayLabel;
    }

    public ulong ObligationId { get; }
    public ulong AccountId { get; }
    public DateOnly ExpectedDate { get; }
    public decimal Amount { get; }
    public string Currency { get; }
    public string CounterpartyName { get; }
    public string? Category { get; }
    public bool IsCredit { get; }
    public bool IsEstimate { get; }
    public string DisplayLabel { get; }

    public string DateDisplay => ExpectedDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string AmountDisplay => $"{(Amount >= 0m ? "[+] +" : "[-] -")}{Math.Abs(Amount).ToString("N2", DeCulture)} {Currency}";
    public string CounterpartyDisplay => CounterpartyName;
}

/// <summary>
/// Presentation view model for single-currency net-worth summary.
/// </summary>
public sealed class CurrencyNetWorthViewModel : ObservableObject
{
    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    public CurrencyNetWorthViewModel(CurrencyNetWorthSummary summary, DateTimeOffset snapshotDate)
    {
        ArgumentNullException.ThrowIfNull(summary);

        SnapshotDate = snapshotDate;
        Currency = summary.Currency;
        TotalAssets = summary.TotalAssets.Amount;
        TotalLiabilities = summary.TotalLiabilities.Amount;
        NetWorth = summary.NetWorth.Amount;
    }

    public DateTimeOffset SnapshotDate { get; }
    public string Currency { get; }
    public decimal TotalAssets { get; }
    public decimal TotalLiabilities { get; }
    public decimal NetWorth { get; }

    public string DateDisplay => SnapshotDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    public string AssetsDisplay => $"{TotalAssets.ToString("N2", DeCulture)} {Currency}";
    public string LiabilitiesDisplay => $"{TotalLiabilities.ToString("N2", DeCulture)} {Currency}";
    public string NetWorthDisplay => $"{(NetWorth >= 0m ? "+" : "")}{NetWorth.ToString("N2", DeCulture)} {Currency}";
}

/// <summary>
/// Presentation view model for a multi-currency net worth snapshot.
/// Invariant: Respects multi-currency isolation; assets and liabilities are never converted across currencies.
/// </summary>
public sealed class NetWorthSnapshotViewModel : ObservableObject
{
    public NetWorthSnapshotViewModel(NetWorthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SnapshotDate = snapshot.SnapshotDate;
        Currencies = snapshot.Currencies;

        List<CurrencyNetWorthViewModel> list = new();
        foreach (string curr in snapshot.Currencies)
        {
            if (snapshot.TryGetSummary(curr, out CurrencyNetWorthSummary? summary) && summary is not null)
            {
                list.Add(new CurrencyNetWorthViewModel(summary, snapshot.SnapshotDate));
            }
        }
        Summaries = list.AsReadOnly();
    }

    public DateTimeOffset SnapshotDate { get; }
    public IReadOnlyList<string> Currencies { get; }
    public IReadOnlyList<CurrencyNetWorthViewModel> Summaries { get; }

    public string DateDisplay => SnapshotDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
