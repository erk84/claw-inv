using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class MomentumRotationBacktester
{
    public static (BacktestResult result, IReadOnlyList<PortfolioPoint> curve) Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to,
        decimal initialCapital = 100_000m)
    {
        decimal? NavAtOrBefore(NavSeries s, DateOnly date)
        {
            // forward-fill within series
            var pts = s.Points;
            for (var i = pts.Count - 1; i >= 0; i--)
            {
                if (pts[i].Date <= date)
                    return pts[i].Nav;
            }
            return null;
        }

        IReadOnlyList<string> Choose(DateOnly rb, IReadOnlyList<NavSeries> ss)
        {
            var lbDate = rb.AddMonths(-strat.LookbackMonths);

            var scores = new List<(string id, decimal ret)>();
            foreach (var s in ss)
            {
                var navNow = NavAtOrBefore(s, rb);
                var navThen = NavAtOrBefore(s, lbDate);
                if (navNow is null || navThen is null) continue;
                scores.Add((s.OrderbookId, (navNow.Value / navThen.Value) - 1m));
            }

            if (scores.Count == 0) return Array.Empty<string>();

            var ranked = scores.OrderByDescending(x => x.ret).ToList();

            if (strat.UseAbsoluteMomentumFilter)
            {
                var best = ranked[0];
                if (best.ret <= 0m)
                    return ["CASH"];
            }

            if (strat.Allocation == AllocationMode.Top1)
                return [ranked[0].id];

            return ranked.Take(Math.Min(strat.TopK, ranked.Count)).Select(x => x.id).ToList();
        }

        return RebalanceEngine.Run(strat, series, from, to, Choose, initialCapital);
    }
}
