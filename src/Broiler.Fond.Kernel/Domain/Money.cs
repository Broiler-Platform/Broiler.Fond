namespace Broiler.Fond.Kernel.Domain;

/// <summary>
/// Exact decimal money in a recognized reference currency. Values are not rounded
/// to minor units; source parsing must use ParseExact to reject precision loss.
/// </summary>
public sealed record Money
{
    public Money(decimal amount, string currency)
    {
        if (!CurrencyCatalog.IsRecognized(currency))
        {
            throw new ArgumentException("An explicitly recognized currency code is required.", nameof(currency));
        }

        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    /// <summary>Parses invariant signed decimal text without whitespace, exponent or rounding.</summary>
    public static Money ParseExact(string amount, string currency) => new(ExactDecimal.Parse(amount), currency);

    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new ArgumentException("Different currencies cannot be added.", nameof(other));
        }

        return new(ExactDecimal.FromUnits(ExactDecimal.ToUnits(Amount) + ExactDecimal.ToUnits(other.Amount)), Currency);
    }

    public override string ToString() => nameof(Money);
}
