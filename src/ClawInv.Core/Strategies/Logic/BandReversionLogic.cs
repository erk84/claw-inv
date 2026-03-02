using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class BandReversionLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.BandReversion;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Pick funds most below their MA(window), but only if NAV above longer MA(trend gate).
        var window = Math.Max(6, strat.LookbackMonths);
        var trendMa = Math.Max(6, strat.MovingAverageMonths);

        var scored = new List<(string id, double score)>();

        foreach (var s in series)
        {
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            var navNow = StrategyNavHelpers.NavAtOrBefore(pts, asOf);
            if (navNow is null) continue;

            var trend = MovingAverageNav(pts, asOf, trendMa);
            if (trend is null || trend.Value <= 0m) continue;
            if (navNow.Value <= trend.Value) continue;

            var band = MovingAverageNav(pts, asOf, window);
            if (band is null || band.Value <= 0m) continue;

            var rel = (double)(navNow.Value / band.Value - 1m);
            // More negative rel => better mean reversion candidate.
            scored.Add((s.OrderbookId, -rel));
        }

        if (scored.Count == 0)
            return new Dictionary<string, decimal>();

        scored.Sort((a, b) => b.score.CompareTo(a.score));
        var k = Math.Max(1, Math.Min(strat.TopK, scored.Count));
        var chosen = scored.Take(k).Select(x => x.id).ToArray();

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
