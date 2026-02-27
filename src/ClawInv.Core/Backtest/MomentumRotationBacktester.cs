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
        if (series.Count == 0) throw new ArgumentException("No series");

        var allDates = series
            .SelectMany(s => s.Points)
            .Select(p => p.Date)
            .Where(d => d >= from && d <= to)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        if (allDates.Count < 40)
            return (new BacktestResult(strat.Id, strat.Name, from, to, 0, 0, 0, null, 0, 0, "Not enough data"),
                Array.Empty<PortfolioPoint>());

        var dateIndex = allDates
            .Select((d, i) => (d, i))
            .ToDictionary(x => x.d, x => x.i);

        var dicts = series.ToDictionary(
            s => s.OrderbookId,
            s => s.Points.Where(p => p.Date >= from && p.Date <= to)
                         .ToDictionary(p => p.Date, p => p.Nav));

        decimal? NavAtOrBefore(string id, DateOnly date)
        {
            var d = dicts[id];
            if (d.TryGetValue(date, out var nav)) return nav;

            var idx = dateIndex.TryGetValue(date, out var di)
                ? di
                : ~allDates.BinarySearch(date) - 1;

            for (var i = idx; i >= 0; i--)
            {
                var dd = allDates[i];
                if (d.TryGetValue(dd, out nav)) return nav;
            }

            return null;
        }

        var rebalances = allDates
            .GroupBy(d => new { d.Year, d.Month })
            .Select(g => g.First())
            .Where(d => d >= from && d <= to)
            .ToList();

        var equity = initialCapital;
        var curve = new List<PortfolioPoint>(allDates.Count);

        for (var rbIndex = 0; rbIndex < rebalances.Count; rbIndex += strat.RebalanceEveryMonths)
        {
            var rb = rebalances[rbIndex];
            var lbDate = rb.AddMonths(-strat.LookbackMonths);

            var scores = new List<(string id, decimal ret)>();
            foreach (var s in series)
            {
                var navNow = NavAtOrBefore(s.OrderbookId, rb);
                var navThen = NavAtOrBefore(s.OrderbookId, lbDate);
                if (navNow is null || navThen is null) continue;
                scores.Add((s.OrderbookId, (navNow.Value / navThen.Value) - 1m));
            }

            if (scores.Count == 0)
                continue;

            var ranked = scores
                .OrderByDescending(x => x.ret)
                .Select(x => x.id)
                .ToList();

            var holdings = strat.Allocation switch
            {
                AllocationMode.Top1 => ranked.Take(1).ToList(),
                AllocationMode.EqualWeightTopK => ranked.Take(Math.Min(strat.TopK, ranked.Count)).ToList(),
                _ => ranked.Take(1).ToList(),
            };

            var nextRb = rbIndex + strat.RebalanceEveryMonths < rebalances.Count
                ? rebalances[rbIndex + strat.RebalanceEveryMonths]
                : to;

            var segmentDates = allDates.Where(d => d >= rb && d <= nextRb).ToList();
            if (segmentDates.Count == 0) continue;

            // Track last NAV per holding to compute daily returns per asset
            var lastNav = new Dictionary<string, decimal>();
            foreach (var h in holdings)
            {
                var startNav = NavAtOrBefore(h, segmentDates[0]);
                if (startNav is not null) lastNav[h] = startNav.Value;
            }

            foreach (var d in segmentDates)
            {
                // equal weight daily return
                decimal sumR = 0m;
                var n = 0;

                foreach (var h in holdings)
                {
                    var navD = NavAtOrBefore(h, d);
                    if (navD is null || !lastNav.TryGetValue(h, out var prev))
                        continue;

                    var r = (navD.Value / prev) - 1m;
                    sumR += r;
                    n++;
                    lastNav[h] = navD.Value;
                }

                if (n == 0)
                    continue;

                var dailyR = sumR / n;
                equity *= (1m + dailyR);

                curve.Add(new PortfolioPoint(d, equity, string.Join("|", holdings)));
            }
        }

        if (curve.Count < 2)
            return (new BacktestResult(strat.Id, strat.Name, from, to, 0, 0, 0, null, 0, 0, "No trades"), curve);

        var navLike = curve
            .GroupBy(p => p.Date)
            .Select(g => g.Last())
            .OrderBy(p => p.Date)
            .Select(p => new NavPoint(p.Date, p.Equity))
            .ToList();

        var m = MetricsCalculator.Compute(navLike);
        var totalReturn = (navLike[^1].Nav / navLike[0].Nav) - 1m;

        var res = new BacktestResult(
            strat.Id,
            strat.Name,
            m.Start,
            m.End,
            m.Days,
            m.Cagr,
            m.Volatility,
            m.Sharpe,
            m.MaxDrawdown,
            totalReturn,
            "Monthly momentum rotation"
        );

        return (res, curve);
    }
}
