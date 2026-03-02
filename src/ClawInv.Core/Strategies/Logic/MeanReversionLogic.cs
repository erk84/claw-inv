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
        // Mean reversion: dual-timeframe deviation-from-mean.
        // Principle: use a short-term mean for sensitivity and a longer-term mean for robustness.
        var scored = new List<(string id, double score)>();

        var maShort = Math.Max(2, strat.LookbackMonths);
        var maLong = Math.Max(maShort + 2, maShort * 2);

        foreach (var s in series)
        {
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            // Optional trend gate still applies if configured.
            if (strat.MovingAverageMonths > 0)
            {
                var endNav = StrategyNavHelpers.NavAtOrBefore(pts, asOf);
                var maNavGate = StrategyNavHelpers.NavAtOrBefore(pts, asOf.AddMonths(-Math.Max(1, strat.MovingAverageMonths)));
                if (endNav is null || maNavGate is null || maNavGate <= 0m) continue;
                if (endNav.Value < maNavGate.Value) continue;
            }

            var devShort = DeviationFromMa(pts, asOf, maShort);
            var devLong = DeviationFromMa(pts, asOf, maLong);
            if (devShort is null || devLong is null) continue;

            // Weighted combo: emphasize short-term, keep long-term context.
            var score = devShort.Value + 0.5 * devLong.Value;
            scored.Add((s.OrderbookId, score));
        }

        var chosen = scored
            .OrderBy(x => x.score)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }

    private static double? DeviationFromMa(NavPoint[] points, DateOnly end, int months)
    {
        var navNow = StrategyNavHelpers.NavAtOrBefore(points, end);
        var ma = MovingAverageNav(points, end, months);
        if (navNow is null || ma is null || ma.Value <= 0m) return null;
        return (double)(navNow.Value / ma.Value - 1m);
    }

    private static decimal? MovingAverageNav(NavPoint[] points, DateOnly end, int months)
    {
        if (months <= 0) return null;
        decimal sum = 0m;
        var n = 0;
        for (var i = 0; i <= months; i++)
        {
            var d = end.AddMonths(-i);
            var p = StrategyNavHelpers.NavAtOrBefore(points, d);
            if (p is null) continue;
            sum += p.Value;
            n++;
        }
        return n > 0 ? sum / n : null;
    }
}
