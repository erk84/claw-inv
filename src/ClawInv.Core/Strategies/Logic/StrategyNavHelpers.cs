using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal static class StrategyNavHelpers
{
    public static decimal? NavAtOrBefore(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string orderbookId, DateOnly date)
    {
        if (!fundIndex.TryGetValue(orderbookId, out var pts) || pts.Length == 0)
            return null;

        return NavAtOrBefore(pts, date);
    }

    public static decimal? NavAtOrBefore(NavPoint[] points, DateOnly date)
    {
        if (points.Length == 0) return null;

        // points are sorted; binary search for last <= date
        var lo = 0;
        var hi = points.Length - 1;
        int best = -1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].Date <= date)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best >= 0 ? points[best].Nav : null;
    }

    public static double? MonthlyReturn(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string orderbookId, DateOnly asOf, int lookbackMonths)
    {
        if (lookbackMonths <= 0) return null;

        var endNav = NavAtOrBefore(fundIndex, orderbookId, asOf);
        if (endNav is null || endNav <= 0m) return null;

        var startDate = asOf.AddMonths(-lookbackMonths);
        var startNav = NavAtOrBefore(fundIndex, orderbookId, startDate);
        if (startNav is null || startNav <= 0m) return null;

        return (double)(endNav.Value / startNav.Value - 1m);
    }
}
