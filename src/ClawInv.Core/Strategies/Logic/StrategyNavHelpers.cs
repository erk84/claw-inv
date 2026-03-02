using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal static class StrategyNavHelpers
{
    public static decimal? NavAtOrBefore(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string orderbookId, DateOnly date)
    {
        if (!fundIndex.TryGetValue(orderbookId, out var pts) || pts.Length == 0)
            return null;

        // points are sorted ascending by date
        for (var i = pts.Length - 1; i >= 0; i--)
        {
            if (pts[i].Date <= date)
                return pts[i].Nav;
        }

        return null;
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
