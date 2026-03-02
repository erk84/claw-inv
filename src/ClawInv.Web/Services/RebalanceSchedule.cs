namespace ClawInv.Web.Services;

public static class RebalanceSchedule
{
    public static bool IsRebalanceDue(DateOnly asOfDate, DateOnly anchorDate, int rebalanceMonths)
    {
        if (rebalanceMonths <= 0)
            return false;

        if (asOfDate < anchorDate)
            return false;

        // Rebalance on month boundaries based on anchorDate's day-of-month.
        var months = MonthsBetween(anchorDate, asOfDate);
        return months % rebalanceMonths == 0;
    }

    private static int MonthsBetween(DateOnly a, DateOnly b)
    {
        // a <= b
        return (b.Year - a.Year) * 12 + (b.Month - a.Month);
    }
}
