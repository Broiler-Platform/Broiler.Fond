namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Exact decimal net-worth summary isolated to a single ISO currency.
/// </summary>
public sealed class CurrencyNetWorthSummary
{
    public CurrencyNetWorthSummary(
        string currency,
        IReadOnlyList<NetWorthAccountBalance> accounts)
    {
        if (!CurrencyCatalog.IsRecognized(currency))
        {
            throw new ArgumentException("A recognized ISO currency is required.", nameof(currency));
        }

        ArgumentNullException.ThrowIfNull(accounts);

        Currency = currency;
        Accounts = accounts;

        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        Dictionary<AccountProductCategory, decimal> categoryTotals = [];

        foreach (NetWorthAccountBalance acc in accounts)
        {
            if (acc.Balance.Currency != currency)
            {
                throw new ArgumentException(
                    $"Account balance currency '{acc.Balance.Currency}' does not match summary currency '{currency}'.");
            }

            decimal balanceAmount = acc.Balance.Amount;

            if (acc.Classification == AccountClassificationKind.Asset)
            {
                // Positive asset equity
                totalAssets += balanceAmount;
            }
            else
            {
                // Debt liability magnitude
                decimal debtMagnitude = balanceAmount < 0m ? -balanceAmount : balanceAmount;
                totalLiabilities += debtMagnitude;
            }

            if (!categoryTotals.TryGetValue(acc.ProductCategory, out decimal catSum))
            {
                catSum = 0m;
            }
            categoryTotals[acc.ProductCategory] = catSum + balanceAmount;
        }

        TotalAssets = new Money(totalAssets, currency);
        TotalLiabilities = new Money(totalLiabilities, currency);
        NetWorth = new Money(totalAssets - totalLiabilities, currency);

        Dictionary<AccountProductCategory, Money> breakdown = [];
        foreach ((AccountProductCategory cat, decimal sum) in categoryTotals)
        {
            breakdown[cat] = new Money(sum, currency);
        }
        CategoryBreakdown = breakdown;
    }

    public string Currency { get; }
    public Money TotalAssets { get; }
    public Money TotalLiabilities { get; }
    public Money NetWorth { get; }
    public IReadOnlyList<NetWorthAccountBalance> Accounts { get; }
    public IReadOnlyDictionary<AccountProductCategory, Money> CategoryBreakdown { get; }

    /// <summary>
    /// Gets the subtotal for a specific product category in this currency, or zero if none exist.
    /// </summary>
    public Money GetCategoryTotal(AccountProductCategory category) =>
        CategoryBreakdown.TryGetValue(category, out Money? money)
            ? money
            : new Money(0m, Currency);
}
