namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Enforces statutory deadlines for SEPA direct-debit returns under European and German payment laws
/// (§§ 675x, 676b BGB; Directive (EU) 2015/2366 Articles 71 and 76).
/// </summary>
public static class DirectDebitStatutoryDeadline
{
    /// <summary>
    /// Statutory 8-week unconditional return window for authorized direct debits (8 * 7 = 56 calendar days).
    /// </summary>
    public const int AuthorizedReturnLimitDays = 56;

    /// <summary>
    /// Statutory 13-month claim window for unauthorized direct debits (approx. 396 calendar days).
    /// </summary>
    public const int UnauthorizedReturnLimitDays = 396;

    /// <summary>
    /// Returns the maximum allowed days from the debit booking date for the given return reason.
    /// </summary>
    public static int GetAllowedDays(DirectDebitReturnReason reason) =>
        reason == DirectDebitReturnReason.NoValidMandate
            ? UnauthorizedReturnLimitDays
            : AuthorizedReturnLimitDays;

    /// <summary>
    /// Calculates the statutory deadline date (inclusive) for returning a direct debit.
    /// </summary>
    public static DateOnly GetDeadlineDate(DirectDebitReturnReason reason, DateOnly bookingDate)
    {
        if (reason == DirectDebitReturnReason.NoValidMandate)
        {
            // 13 calendar months from booking date
            return bookingDate.AddMonths(13);
        }

        // 8 weeks (56 calendar days)
        return bookingDate.AddDays(AuthorizedReturnLimitDays);
    }

    /// <summary>
    /// Evaluates whether the requested return is within the statutory deadline as of the specified evaluation date.
    /// </summary>
    public static bool IsWithinStatutoryDeadline(
        DirectDebitReturnReason reason,
        DateOnly bookingDate,
        DateOnly evaluationDate)
    {
        // A future booking date cannot be refunded before it occurs.
        if (bookingDate > evaluationDate)
        {
            return false;
        }

        DateOnly deadlineDate = GetDeadlineDate(reason, bookingDate);
        return evaluationDate <= deadlineDate;
    }
}
