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

        decimal? VolAnnualized(string id, DateOnly end, int months)
        {
            if (months <= 1) return null;

            var s = series.FirstOrDefault(x => x.OrderbookId == id);
            if (s is null) return null;

            var start = end.AddMonths(-months);
            var pts = s.Points.Where(p => p.Date >= start && p.Date <= end).OrderBy(p => p.Date).ToList();
            if (pts.Count < 3) return null;

            // daily returns stdev annualized
            var rets = new List<double>(pts.Count - 1);
            for (var i = 1; i < pts.Count; i++)
            {
                var r = (double)((pts[i].Nav / pts[i - 1].Nav) - 1m);
                rets.Add(r);
            }

            if (rets.Count < 2) return null;
            var mean = rets.Average();
            var varSum = rets.Sum(r => (r - mean) * (r - mean));
            var variance = varSum / (rets.Count - 1);
            return (decimal)(Math.Sqrt(variance) * Math.Sqrt(252.0));
        }

        IReadOnlyList<string> Choose(DateOnly rb, IReadOnlyList<NavSeries> ss)
        {
            var lbDate = rb.AddMonths(-strat.LookbackMonths);

            var scores = new List<(string id, decimal mom)>();
            foreach (var s in ss)
            {
                var navNow = NavAtOrBefore(s, rb);
                var navThen = NavAtOrBefore(s, lbDate);
                if (navNow is null || navThen is null) continue;
                scores.Add((s.OrderbookId, (navNow.Value / navThen.Value) - 1m));
            }

            if (scores.Count == 0) return Array.Empty<string>();

            var ranked = scores.OrderByDescending(x => x.mom).ToList();

            if (strat.UseAbsoluteMomentumFilter && ranked[0].mom <= 0m)
                return ["CASH"];

            // candidate set: take top momentum list then optionally low-vol filter
            var candidates = ranked.Select(x => x.id).ToList();

            if (strat.UseLowVolFilter && strat.VolatilityLookbackMonths > 1)
            {
                var vols = new List<(string id, decimal vol)>();
                foreach (var id in candidates)
                {
                    var v = VolAnnualized(id, rb, strat.VolatilityLookbackMonths);
                    if (v is not null)
                        vols.Add((id, v.Value));
                }

                if (vols.Count > 0)
                {
                    vols.Sort((a, b) => a.vol.CompareTo(b.vol));
                    candidates = vols.Select(x => x.id).ToList();
                }
            }

            var k = Math.Max(1, Math.Min(strat.TopK, candidates.Count));

            if (strat.Allocation == AllocationMode.Top1)
                return [candidates[0]];

            return candidates.Take(k).ToList();
        }

        return RebalanceEngine.Run(strat, series, from, to, Choose, initialCapital);
    }
}
