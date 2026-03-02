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
        // Mean reversion in an uptrend: short-term reversal + long-term momentum context.
        // Score = 1M return - 0.3 * 12M return (more negative = recent pullback within stronger trend).
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

            var r1 = MonthlyReturn(pts, asOf, 1);
            var r12 = MonthlyReturn(pts, asOf, 12);
            if (r1 is null || r12 is null) continue;

            // More negative => worse recent month but strong 12M trend => classic pullback-in-uptrend setup
            var score = r1.Value - 0.3 * r12.Value;
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

    private static double? MonthlyReturn(NavPoint[] points, DateOnly end, int months)
    {
        if (months <= 0) return null;
        var a = StrategyNavHelpers.NavAtOrBefore(points, end.AddMonths(-months));
        var b = StrategyNavHelpers.NavAtOrBefore(points, end);
        if (a is null || b is null) return null;
        if (a.Value <= 0m) return null;
        return (double)(b.Value / a.Value - 1m);
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
