namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Enforces domain invariants for transaction splitting:
/// 1. Cardinality: At least 2 split items required.
/// 2. Strict Balance Invariant: The exact decimal sum of split item amounts must equal the parent transaction amount.
/// 3. Unique indices: Split items must have distinct indices.
/// </summary>
public static class TransactionSplitValidator
{
    public const int MinimumSplitCount = 2;
    public const int MaximumSplitCount = 100;

    /// <summary>
    /// Validates that a set of split items satisfies all domain invariants against a parent transaction amount.
    /// Throws an InvalidOperationException or ArgumentException if validation fails.
    /// </summary>
    public static void Validate(IReadOnlyList<TransactionSplitItem> splits, decimal parentAmount)
    {
        ArgumentNullException.ThrowIfNull(splits);

        if (splits.Count < MinimumSplitCount)
        {
            throw new ArgumentException($"A transaction split requires at least {MinimumSplitCount} split items.", nameof(splits));
        }

        if (splits.Count > MaximumSplitCount)
        {
            throw new ArgumentException($"Split count exceeds maximum allowed limit of {MaximumSplitCount} items.", nameof(splits));
        }

        var indices = new HashSet<int>();
        decimal sum = 0m;

        foreach (var item in splits)
        {
            if (!indices.Add(item.Index))
            {
                throw new ArgumentException($"Duplicate split item index {item.Index}.", nameof(splits));
            }

            sum += item.Amount;
        }

        if (sum != parentAmount)
        {
            throw new InvalidOperationException(
                $"Strict balance invariant violated: The sum of split amounts ({sum}) does not equal the parent transaction amount ({parentAmount}).");
        }
    }

    /// <summary>
    /// Attempts to validate split items, returning false and a descriptive error message if invalid.
    /// </summary>
    public static bool TryValidate(
        IReadOnlyList<TransactionSplitItem>? splits,
        decimal parentAmount,
        out string? errorMessage)
    {
        if (splits == null || splits.Count < MinimumSplitCount)
        {
            errorMessage = $"A transaction split requires at least {MinimumSplitCount} split items.";
            return false;
        }

        if (splits.Count > MaximumSplitCount)
        {
            errorMessage = $"Split count exceeds maximum allowed limit of {MaximumSplitCount} items.";
            return false;
        }

        var indices = new HashSet<int>();
        decimal sum = 0m;

        foreach (var item in splits)
        {
            if (!indices.Add(item.Index))
            {
                errorMessage = $"Duplicate split item index {item.Index}.";
                return false;
            }

            sum += item.Amount;
        }

        if (sum != parentAmount)
        {
            errorMessage = $"Sum of split amounts ({sum}) does not equal parent transaction amount ({parentAmount}).";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
