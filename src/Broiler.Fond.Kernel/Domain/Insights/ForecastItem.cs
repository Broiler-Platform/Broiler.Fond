namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// A single prospective anticipated transaction occurrence in a cash-flow forecast.
/// INVARIANT: IsEstimate is ALWAYS true. Projections never masquerade as settled bank facts.
/// </summary>
public sealed record ForecastItem
{
    public ulong ObligationId { get; init; }
    public ulong AccountId { get; init; }
    public DateOnly ExpectedDate { get; init; }
    public decimal Amount { get; init; } // Signed: + for credit income, - for debit expense
    public string Currency { get; init; }
    public string CounterpartyName { get; init; }
    public string? Category { get; init; }
    public bool IsCredit => Amount > 0m;
    public bool IsEstimate { get; init; } = true;
    public string DisplayLabel { get; init; }

    public ForecastItem(
        ulong obligationId,
        ulong accountId,
        DateOnly expectedDate,
        decimal amount,
        string currency,
        string counterpartyName,
        string? category = null)
    {
        if (amount == 0m)
        {
            throw new ArgumentException("Forecast item amount cannot be zero.", nameof(amount));
        }

        ObligationId = obligationId;
        AccountId = accountId;
        ExpectedDate = expectedDate;
        Amount = amount;
        Currency = currency.ToUpperInvariant();
        CounterpartyName = counterpartyName;
        Category = category;
        IsEstimate = true;
        DisplayLabel = $"[Estimate / Prognose] {counterpartyName}";
    }
}
