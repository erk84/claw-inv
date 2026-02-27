using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class LowVolBacktester
{
    /// <summary>
    /// Select TopK funds with lowest realized volatility over lookback window (months). Equal weight.
    /// </summary>
    public static (BacktestResult result, IReadOnlyList<PortfolioPoint> curve) Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to,
        decimal initialCapital = 100_000m)
    {
        static decimal? NavAtOrBefore(NavSeries s, DateOnly date)
        {
            for (var i = s.Points.Count - 1; i >= 0; i--)
                if (s.Points[i].Date <= date)
                    return s.Points[i].Nav;
            return null;
        }

        static double? Vol(NavSeries s, DateOnly start, DateOnly end)
        {
            var pts = s.Points.Where(p => p.Date >= start && p.Date <= end).ToList();
            if (pts.Count < 3) return null;
            var rets = new List<double>();
            for (var i = 1; i < pts.Count; i++)
            {
                var r = (double)((pts[i].Nav / pts[i - 1].Nav) - 1m);
                rets.Add(r);
            }
            var mean = rets.Average();
            var varSum = rets.Sum(r => (r - mean) * (r - mean));
            var variance = varSum / (rets.Count - 1);
            return Math.Sqrt(variance) * Math.Sqrt(252.0);
        }

        IReadOnlyList<string> Choose(DateOnly rb, IReadOnlyList<NavSeries> ss)
        {
            var start = rb.AddMonths(-strat.VolatilityLookbackMonths);
            var scored = new List<(string id, double vol)>();
            foreach (var s in ss)
            {
                var nav = NavAtOrBefore(s, rb);
                if (nav is null) continue;
                var v = Vol(s, start, rb);
                if (v is null) continue;
                scored.Add((s.OrderbookId, v.Value));
            }

            if (scored.Count == 0) return ["CASH"];

            var ranked = scored.OrderBy(x => x.vol).ToList();
            var k = Math.Max(1, Math.Min(strat.TopK, ranked.Count));
            return ranked.Take(k).Select(x => x.id).ToList();
        }

        return RebalanceEngine.Run(strat, series, from, to, Choose, initialCapital);
    }
}
