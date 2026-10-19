namespace Broiler.Fond.Kernel.Domain.Transactions.Query;

/// <summary>
/// Immutable filter parameters for querying and filtering transactions.
/// </summary>
public sealed record TransactionQueryFilter(
    ulong? AccountId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Status = null,
    bool? IsCredit = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string? TextQuery = null,
    string? Category = null,
    string? Tag = null)
{
    public static readonly TransactionQueryFilter Empty = new();

    /// <summary>
    /// Checks if a transaction item matches all specified criteria in this filter.
    /// </summary>
    public bool Matches(TransactionItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (AccountId.HasValue && item.AccountId != AccountId.Value)
        {
            return false;
        }

        if (FromDate.HasValue && item.BookingDate < FromDate.Value)
        {
            return false;
        }

        if (ToDate.HasValue && item.BookingDate > ToDate.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Status) && !string.Equals(Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(item.Status, Status, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (IsCredit.HasValue && item.IsCredit != IsCredit.Value)
        {
            return false;
        }

        if (MinAmount.HasValue && item.Amount < MinAmount.Value)
        {
            return false;
        }

        if (MaxAmount.HasValue && item.Amount > MaxAmount.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            bool matchesMain = string.Equals(item.Category, Category, StringComparison.OrdinalIgnoreCase);
            bool matchesSplit = item.Splits != null && item.Splits.Any(s => string.Equals(s.Category, Category, StringComparison.OrdinalIgnoreCase));
            if (!matchesMain && !matchesSplit)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(Tag))
        {
            bool matchesMain = item.Tags.Any(t => string.Equals(t, Tag, StringComparison.OrdinalIgnoreCase));
            bool matchesSplit = item.Splits != null && item.Splits.Any(s => s.Tags != null && s.Tags.Any(t => string.Equals(t, Tag, StringComparison.OrdinalIgnoreCase)));
            if (!matchesMain && !matchesSplit)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(TextQuery))
        {
            string[] tokens = TextQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string corpus = item.SearchCorpus;
            foreach (string token in tokens)
            {
                if (!corpus.Contains(token.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
