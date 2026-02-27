using ClawInv.Core.Backtest;

namespace ClawInv.Core.Research;

public static class MonthlySeriesBuilder
{
    /// <summary>
    /// Converts irregular/daily NAV points to month-end NAV series.
    /// Picks last available NAV in each month.
    /// </summary>
    public static IReadOnlyList<NavPoint> ToMonthEnd(IReadOnlyList<NavPoint> daily)
    {
        return daily
            .GroupBy(p => new { p.Date.Year, p.Date.Month })
            .Select(g => g.OrderBy(x => x.Date).Last())
            .OrderBy(x => x.Date)
            .ToList();
    }
}
