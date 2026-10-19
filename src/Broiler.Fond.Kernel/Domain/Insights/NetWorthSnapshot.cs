namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// A multi-currency net-worth snapshot at a point in time, strictly maintaining currency isolation.
/// </summary>
public sealed class NetWorthSnapshot
{
    public NetWorthSnapshot(
        DateTimeOffset snapshotDate,
        IEnumerable<CurrencyNetWorthSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        SnapshotDate = snapshotDate;
        Dictionary<string, CurrencyNetWorthSummary> dict = new(StringComparer.Ordinal);
        foreach (CurrencyNetWorthSummary summary in summaries)
        {
            ArgumentNullException.ThrowIfNull(summary);
            if (!dict.TryAdd(summary.Currency, summary))
            {
                throw new ArgumentException($"Duplicate currency summary for '{summary.Currency}'.");
            }
        }

        CurrencySummaries = dict;
        Currencies = dict.Keys.OrderBy(static c => c, StringComparer.Ordinal).ToArray();
    }

    public DateTimeOffset SnapshotDate { get; }
    public IReadOnlyDictionary<string, CurrencyNetWorthSummary> CurrencySummaries { get; }
    public IReadOnlyList<string> Currencies { get; }

    public CurrencyNetWorthSummary? GetSummary(string currency) =>
        CurrencySummaries.TryGetValue(currency, out CurrencyNetWorthSummary? summary) ? summary : null;

    public bool TryGetSummary(string currency, out CurrencyNetWorthSummary? summary) =>
        CurrencySummaries.TryGetValue(currency, out summary);
}
