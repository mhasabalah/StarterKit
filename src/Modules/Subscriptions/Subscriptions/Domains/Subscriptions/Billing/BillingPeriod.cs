namespace Subscriptions.Domains.Subscriptions.Billing;

/// <summary>
/// Billing-calendar math. Renewal dates are always computed from the original ANCHOR (activation date) so month-end
/// clamping never drifts: an anchor of Jan 31 renews Feb 28 (Feb 29 in leap years), Mar 31, Apr 30, ...
/// Day math is decimal (no <c>double</c>) and uses the actual period length (no 30-day months).
/// </summary>
public static class BillingPeriod
{
    // DB round trips truncate to microseconds; treat dates within this tolerance as equal.
    private static readonly TimeSpan GridTolerance = TimeSpan.FromSeconds(1);

    /// <summary>First renewal date after activation at <paramref name="start"/>.</summary>
    public static DateTimeOffset FirstRenewal(DateTimeOffset start, int cycleMonths)
    {
        EnsureCycle(cycleMonths);
        return start.AddMonths(cycleMonths);
    }

    /// <summary>
    /// The renewal date that follows <paramref name="currentRenewal"/>. When the current renewal lies on the anchor's
    /// grid (the normal case) the next grid date is returned, which keeps month-end clamping stable. Legacy dates that
    /// drifted off the grid simply advance by one cycle (never shortening a period).
    /// </summary>
    public static DateTimeOffset NextRenewal(DateTimeOffset anchor, DateTimeOffset currentRenewal, int cycleMonths)
    {
        EnsureCycle(cycleMonths);

        var monthsBetween = (currentRenewal.Year - anchor.Year) * 12 + (currentRenewal.Month - anchor.Month);
        var cycles = Math.Max(1, monthsBetween / cycleMonths);
        var onGrid = anchor.AddMonths(cycles * cycleMonths);

        if ((onGrid - currentRenewal).Duration() <= GridTolerance)
        {
            return anchor.AddMonths((cycles + 1) * cycleMonths);
        }

        return currentRenewal.AddMonths(cycleMonths);
    }

    /// <summary>Start of the period ending at <paramref name="periodEnd"/> (fallback when it was not recorded).</summary>
    public static DateTimeOffset PeriodStart(DateTimeOffset? anchor, DateTimeOffset periodEnd, int cycleMonths)
    {
        EnsureCycle(cycleMonths);

        if (anchor is { } a)
        {
            var monthsBetween = (periodEnd.Year - a.Year) * 12 + (periodEnd.Month - a.Month);
            var cycles = monthsBetween / cycleMonths;
            if (cycles >= 1 && (a.AddMonths(cycles * cycleMonths) - periodEnd).Duration() <= GridTolerance)
            {
                return a.AddMonths((cycles - 1) * cycleMonths);
            }
        }

        return periodEnd.AddMonths(-cycleMonths);
    }

    /// <summary>Exact number of days (fractional) between two instants.</summary>
    public static decimal Days(DateTimeOffset from, DateTimeOffset to) =>
        (decimal)(to - from).Ticks / TimeSpan.TicksPerDay;

    /// <summary>Unused share of a period at <paramref name="at"/>, clamped to [0, 1].</summary>
    public static decimal RemainingFraction(DateTimeOffset periodStart, DateTimeOffset periodEnd, DateTimeOffset at)
    {
        var total = Days(periodStart, periodEnd);
        if (total <= 0m)
        {
            return 0m;
        }

        var remaining = Days(at, periodEnd);
        return Math.Clamp(remaining / total, 0m, 1m);
    }

    private static void EnsureCycle(int cycleMonths)
    {
        if (cycleMonths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleMonths), cycleMonths, "Billing cycle must be at least one month.");
        }
    }
}
