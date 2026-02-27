using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class TrendFollowingBacktester
{
    /// <summary>
    /// Trend following filter: hold equal-weight TopK funds whose NAV is above MA(months).
    /// Selection is based on (nav / movingAverage - 1) ranking; if none above -> CASH.
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

        decimal? MovingAverage(NavSeries s, DateOnly date, int months)
        {
            var start = date.AddMonths(-months);
            var vals = s.Points.Where(p => p.Date >= start && p.Date <= date).Select(p => p.Nav).ToList();
            if (vals.Count == 0) return null;
            return vals.Average();
        }

        IReadOnlyList<string> Choose(DateOnly rb, IReadOnlyList<NavSeries> ss)
        {
            var scored = new List<(string id, decimal score)>();
            foreach (var s in ss)
            {
                var nav = NavAtOrBefore(s, rb);
                var ma = MovingAverage(s, rb, strat.MovingAverageMonths);
                if (nav is null || ma is null || ma.Value == 0m) continue;
                var rel = (nav.Value / ma.Value) - 1m;
                if (rel > 0m)
                    scored.Add((s.OrderbookId, rel));
            }

            if (scored.Count == 0) return ["CASH"];

            var ranked = scored.OrderByDescending(x => x.score).ToList();
            var k = Math.Max(1, Math.Min(strat.TopK, ranked.Count));
            return ranked.Take(k).Select(x => x.id).ToList();
        }

        return RebalanceEngine.Run(strat, series, from, to, Choose, initialCapital);
    }
}
