namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Deterministic calculation of scheduled recurrence dates for SEPA standing orders.
/// Accurately handles variable month lengths, leap years, start dates, and optional expiration dates.
/// </summary>
public static class StandingOrderScheduleCalculator
{
    /// <summary>
    /// Calculates the next execution date strictly after the given reference date.
    /// Returns null if the standing order has expired (exceeded LastExecutionDate).
    /// </summary>
    public static DateOnly? CalculateNextExecutionDate(
        StandingOrderFrequency frequency,
        int executionDay,
        DateOnly firstExecutionDate,
        DateOnly? lastExecutionDate,
        DateOnly referenceDate)
    {
        if (executionDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(executionDay), "Execution day must be between 1 and 31.");
        }

        int stepMonths = (int)frequency;
        if (stepMonths is not (1 or 2 or 3 or 6 or 12))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency), "Unsupported standing order frequency.");
        }

        // Start search at the first execution date
        DateOnly candidate = ResolveDateInMonth(firstExecutionDate.Year, firstExecutionDate.Month, executionDay);
        if (candidate < firstExecutionDate)
        {
            // If the day in firstExecutionDate's month is earlier than firstExecutionDate, move one cycle forward
            candidate = AdvanceByMonths(candidate, stepMonths, executionDay);
        }

        // Advance candidate until it is strictly after referenceDate
        while (candidate <= referenceDate)
        {
            candidate = AdvanceByMonths(candidate, stepMonths, executionDay);
        }

        // Check against LastExecutionDate
        if (lastExecutionDate.HasValue && candidate > lastExecutionDate.Value)
        {
            return null;
        }

        return candidate;
    }

    /// <summary>
    /// Generates up to <paramref name="count"/> upcoming execution dates on or after <paramref name="referenceDate"/>.
    /// </summary>
    public static IReadOnlyList<DateOnly> CalculateUpcomingDates(
        StandingOrder order,
        DateOnly referenceDate,
        int count)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (count <= 0)
        {
            return Array.Empty<DateOnly>();
        }

        List<DateOnly> dates = new(count);
        DateOnly searchRef = referenceDate.AddDays(-1);

        while (dates.Count < count)
        {
            DateOnly? next = CalculateNextExecutionDate(
                order.Frequency,
                order.ExecutionDay,
                order.FirstExecutionDate,
                order.LastExecutionDate,
                searchRef);

            if (!next.HasValue)
            {
                break;
            }

            dates.Add(next.Value);
            searchRef = next.Value;
        }

        return dates.AsReadOnly();
    }

    private static DateOnly AdvanceByMonths(DateOnly current, int months, int targetDay)
    {
        int totalMonths = (current.Year * 12) + (current.Month - 1) + months;
        int nextYear = totalMonths / 12;
        int nextMonth = (totalMonths % 12) + 1;

        return ResolveDateInMonth(nextYear, nextMonth, targetDay);
    }

    private static DateOnly ResolveDateInMonth(int year, int month, int targetDay)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);
        int day = Math.Min(targetDay, daysInMonth);
        return new DateOnly(year, month, day);
    }
}
