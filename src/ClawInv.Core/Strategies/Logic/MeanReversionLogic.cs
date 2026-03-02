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
        // Mean reversion: pick assets that are far below a moving average (deviation-from-mean).
        // This is a common MR principle: reversion toward a smoothed mean.
        var scored = new List<(string id, double dev)>();

        var maMonths = Math.Max(2, strat.LookbackMonths);

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

            var navNow = StrategyNavHelpers.NavAtOrBefore(pts, asOf);
            var ma = MovingAverageNav(pts, asOf, maMonths);
            if (navNow is null || ma is null || ma.Value <= 0m) continue;

            // Deviation: current / MA - 1 (more negative = more oversold)
            var dev = (double)(navNow.Value / ma.Value - 1m);
            scored.Add((s.OrderbookId, dev));
        }

        var chosen = scored
            .OrderBy(x => x.dev)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
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
