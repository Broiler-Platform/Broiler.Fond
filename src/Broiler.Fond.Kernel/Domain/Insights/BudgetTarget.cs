namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// User-configured monthly spending allowance for a specific category and currency.
/// </summary>
public sealed record BudgetTarget
{
    public const int MaxCategoryLength = 64;
    public const int MaxNotesLength = 512;

    public ulong Id { get; init; }
    public string Category { get; init; }
    public decimal MonthlyLimit { get; init; }
    public string Currency { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public bool AllowRollover { get; init; }
    public bool IsActive { get; init; }
    public string? Notes { get; init; }

    public BudgetTarget(
        ulong id,
        string category,
        decimal monthlyLimit,
        string currency = "EUR",
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        bool allowRollover = false,
        bool isActive = true,
        string? notes = null)
    {
        if (id == 0UL)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Budget target ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Budget category cannot be empty.", nameof(category));
        }

        string trimmedCategory = category.Trim();
        if (trimmedCategory.Length > MaxCategoryLength)
        {
            throw new ArgumentException($"Budget category exceeds {MaxCategoryLength} characters.", nameof(category));
        }

        if (monthlyLimit <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyLimit), "Monthly budget limit must be strictly positive.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a valid 3-letter ISO code.", nameof(currency));
        }

        DateOnly from = effectiveFrom.HasValue
            ? new DateOnly(effectiveFrom.Value.Year, effectiveFrom.Value.Month, 1)
            : new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        DateOnly? to = null;
        if (effectiveTo.HasValue)
        {
            // Normalize to last day of the specified month
            int daysInMonth = DateTime.DaysInMonth(effectiveTo.Value.Year, effectiveTo.Value.Month);
            to = new DateOnly(effectiveTo.Value.Year, effectiveTo.Value.Month, daysInMonth);

            if (to.Value < from)
            {
                throw new ArgumentException("Effective to month cannot precede effective from month.", nameof(effectiveTo));
            }
        }

        string? trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes?.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Notes exceed maximum length of {MaxNotesLength} characters.", nameof(notes));
        }

        Id = id;
        Category = trimmedCategory;
        MonthlyLimit = monthlyLimit;
        Currency = currency.Trim().ToUpperInvariant();
        EffectiveFrom = from;
        EffectiveTo = to;
        AllowRollover = allowRollover;
        IsActive = isActive;
        Notes = trimmedNotes;
    }

    /// <summary>
    /// Checks whether this budget target is in effect for a given calendar month.
    /// </summary>
    public bool IsEffectiveForMonth(int year, int month)
    {
        if (!IsActive)
        {
            return false;
        }

        DateOnly monthStart = new(year, month, 1);
        int daysInMonth = DateTime.DaysInMonth(year, month);
        DateOnly monthEnd = new(year, month, daysInMonth);

        if (monthEnd < EffectiveFrom)
        {
            return false;
        }

        if (EffectiveTo.HasValue && monthStart > EffectiveTo.Value)
        {
            return false;
        }

        return true;
    }
}
