namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Domain model for an expected recurring financial obligation (e.g. rent, salary, subscription).
/// May be auto-detected (as a candidate) or user-confirmed.
/// </summary>
public sealed record RecurringObligation
{
    public const int MaxNameLength = 128;
    public const int MaxCategoryLength = 64;

    public ulong Id { get; init; }
    public ulong AccountId { get; init; }
    public string CounterpartyName { get; init; }
    public string? CounterpartyIban { get; init; }
    public decimal ExpectedAmount { get; init; }
    public string Currency { get; init; }
    public bool IsCredit { get; init; }
    public RecurringCadence Cadence { get; init; }
    public int ExpectedDayOfMonth { get; init; }
    public string? Category { get; init; }
    public bool IsUserConfirmed { get; init; }
    public bool IsActive { get; init; }
    public DateOnly? LastObservedDate { get; init; }
    public DateOnly NextExpectedDate { get; init; }

    public RecurringObligation(
        ulong id,
        ulong accountId,
        string counterpartyName,
        decimal expectedAmount,
        string currency = "EUR",
        bool isCredit = false,
        RecurringCadence cadence = RecurringCadence.Monthly,
        int? expectedDayOfMonth = null,
        string? counterpartyIban = null,
        string? category = null,
        bool isUserConfirmed = false,
        bool isActive = true,
        DateOnly? lastObservedDate = null,
        DateOnly? nextExpectedDate = null)
    {
        if (id == 0UL)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Obligation ID must be greater than zero.");
        }

        if (accountId == 0UL)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Account ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(counterpartyName))
        {
            throw new ArgumentException("Counterparty name cannot be empty.", nameof(counterpartyName));
        }

        string trimmedName = counterpartyName.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Counterparty name exceeds {MaxNameLength} characters.", nameof(counterpartyName));
        }

        if (expectedAmount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedAmount), "Expected amount must be strictly positive.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a valid 3-letter ISO code.", nameof(currency));
        }

        int day = expectedDayOfMonth ?? (lastObservedDate?.Day ?? 1);
        if (day < 1 || day > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedDayOfMonth), "Expected day of month must be between 1 and 31.");
        }

        DateOnly next = nextExpectedDate ?? CalculateNextDate(lastObservedDate ?? DateOnly.FromDateTime(DateTime.UtcNow), cadence, day);

        Id = id;
        AccountId = accountId;
        CounterpartyName = trimmedName;
        CounterpartyIban = string.IsNullOrWhiteSpace(counterpartyIban) ? null : counterpartyIban.Trim();
        ExpectedAmount = expectedAmount;
        Currency = currency.Trim().ToUpperInvariant();
        IsCredit = isCredit;
        Cadence = cadence;
        ExpectedDayOfMonth = day;
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        IsUserConfirmed = isUserConfirmed;
        IsActive = isActive;
        LastObservedDate = lastObservedDate;
        NextExpectedDate = next;
    }

    /// <summary>
    /// Calculates the next occurrence of this obligation strictly after the given date.
    /// </summary>
    public DateOnly CalculateNextOccurrence(DateOnly afterDate)
    {
        DateOnly current = NextExpectedDate;
        while (current <= afterDate)
        {
            current = AdvanceByCadence(current, Cadence, ExpectedDayOfMonth);
        }
        return current;
    }

    private static DateOnly CalculateNextDate(DateOnly baseDate, RecurringCadence cadence, int targetDay)
    {
        return AdvanceByCadence(baseDate, cadence, targetDay);
    }

    private static DateOnly AdvanceByCadence(DateOnly date, RecurringCadence cadence, int targetDay)
    {
        return cadence switch
        {
            RecurringCadence.Weekly => date.AddDays(7),
            RecurringCadence.BiWeekly => date.AddDays(14),
            RecurringCadence.Monthly => AddClampedMonths(date, 1, targetDay),
            RecurringCadence.BiMonthly => AddClampedMonths(date, 2, targetDay),
            RecurringCadence.Quarterly => AddClampedMonths(date, 3, targetDay),
            RecurringCadence.SemiAnnually => AddClampedMonths(date, 6, targetDay),
            RecurringCadence.Annually => AddClampedYears(date, 1, targetDay),
            _ => AddClampedMonths(date, 1, targetDay),
        };
    }

    private static DateOnly AddClampedMonths(DateOnly date, int monthsToAdd, int targetDay)
    {
        DateTime dt = date.ToDateTime(TimeOnly.MinValue).AddMonths(monthsToAdd);
        int daysInMonth = DateTime.DaysInMonth(dt.Year, dt.Month);
        int day = Math.Min(targetDay, daysInMonth);
        return new DateOnly(dt.Year, dt.Month, day);
    }

    private static DateOnly AddClampedYears(DateOnly date, int yearsToAdd, int targetDay)
    {
        int targetYear = date.Year + yearsToAdd;
        int daysInMonth = DateTime.DaysInMonth(targetYear, date.Month);
        int day = Math.Min(targetDay, daysInMonth);
        return new DateOnly(targetYear, date.Month, day);
    }
}
