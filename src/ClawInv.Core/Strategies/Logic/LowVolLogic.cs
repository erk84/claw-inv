using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class LowVolLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.LowVolatilitySelection;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Prefer using daily vol over the lookback window.
        var lb = Math.Max(2, strat.VolatilityLookbackMonths);
        var from = asOf.AddMonths(-lb);

        var vols = new List<(string id, double vol)>();

        foreach (var s in series)
        {
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            var v = VolAnnualized(pts, from, asOf);
            if (v is null) continue;
            vols.Add((s.OrderbookId, (double)v.Value));
        }

        var chosen = vols
            .OrderBy(x => x.vol)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }

    private static decimal? VolAnnualized(NavPoint[] points, DateOnly from, DateOnly to)
    {
        // Daily returns from available NAV points within range.
        var dates = points.Select(p => p.Date)
            .Where(d => d >= from && d <= to)
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        if (dates.Length < 5) return null;

        var rets = new List<decimal>();
        for (var i = 1; i < dates.Length; i++)
        {
            var a = StrategyNavHelpers.NavAtOrBefore(points, dates[i - 1]);
            var b = StrategyNavHelpers.NavAtOrBefore(points, dates[i]);
            if (a is null || b is null) continue;
            if (a.Value <= 0) continue;
            rets.Add(b.Value / a.Value - 1.0m);
        }

        if (rets.Count < 4) return null;

        var mean = rets.Average();
        var varSum = rets.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (rets.Count - 1);
        var stdev = (decimal)Math.Sqrt((double)variance);
        return stdev * (decimal)Math.Sqrt(252.0);
    }
}
