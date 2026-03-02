using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class MeanReversionLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.MeanReversionRotation;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Mean reversion: pick assets that are most drawn down from a recent peak.
        // This can be more robust than just last month's return.
        var scored = new List<(string id, double dd)>();

        var windowMonths = Math.Max(3, strat.LookbackMonths);

        foreach (var s in series)
        {
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            if (strat.MovingAverageMonths > 0)
            {
                var endNav = StrategyNavHelpers.NavAtOrBefore(pts, asOf);
                var maNav = StrategyNavHelpers.NavAtOrBefore(pts, asOf.AddMonths(-Math.Max(1, strat.MovingAverageMonths)));
                if (endNav is null || maNav is null || maNav <= 0m) continue;
                if (endNav.Value < maNav.Value) continue;
            }

            var dd = DrawdownFromRecentHigh(pts, asOf, windowMonths);
            if (dd is null || double.IsNaN(dd.Value))
                continue;

            scored.Add((s.OrderbookId, dd.Value));
        }

        var chosen = scored
            .OrderBy(x => x.dd) // most negative drawdown = most beaten down
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }

    private static double? DrawdownFromRecentHigh(NavPoint[] points, DateOnly t, int windowMonths)
    {
        var navNow = StrategyNavHelpers.NavAtOrBefore(points, t);
        if (navNow is null || navNow.Value <= 0m) return null;

        decimal peak = 0m;
        for (var i = 0; i <= windowMonths; i++)
        {
            var d = t.AddMonths(-i);
            var v = StrategyNavHelpers.NavAtOrBefore(points, d);
            if (v is null) continue;
            if (v.Value > peak) peak = v.Value;
        }

        if (peak <= 0m) return null;
        return (double)(navNow.Value / peak - 1m);
    }
}
