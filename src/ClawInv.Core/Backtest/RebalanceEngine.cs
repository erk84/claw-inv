using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class RebalanceEngine
{
    /// <summary>
    /// Generic monthly rebalance engine. Strategy chooses a set of holdings with equal weights.
    /// Includes a synthetic CASH holding (constant NAV=1) when needed.
    /// </summary>
    public static (BacktestResult result, IReadOnlyList<PortfolioPoint> curve) Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to,
        Func<DateOnly, IReadOnlyList<NavSeries>, IReadOnlyList<string>> chooseHoldings,
        decimal initialCapital = 100_000m)
    {
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

        var dateIndex = allDates.Select((d, i) => (d, i)).ToDictionary(x => x.d, x => x.i);

        var dicts = series.ToDictionary(
            s => s.OrderbookId,
            s => s.Points.Where(p => p.Date >= from && p.Date <= to)
                         .ToDictionary(p => p.Date, p => p.Nav));

        decimal? NavAtOrBefore(string id, DateOnly date)
        {
            if (id == "CASH") return 1m;

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

        for (var rbIndex = 0; rbIndex < rebalances.Count; rbIndex += Math.Max(1, strat.RebalanceEveryMonths))
        {
            var rb = rebalances[rbIndex];
            var nextRb = rbIndex + strat.RebalanceEveryMonths < rebalances.Count
                ? rebalances[rbIndex + strat.RebalanceEveryMonths]
                : to;

            var holdings = chooseHoldings(rb, series);
            if (holdings.Count == 0)
                holdings = ["CASH"];

            var segmentDates = allDates.Where(d => d >= rb && d <= nextRb).ToList();
            if (segmentDates.Count == 0) continue;

            var lastNav = new Dictionary<string, decimal>();
            foreach (var h in holdings)
            {
                var startNav = NavAtOrBefore(h, segmentDates[0]);
                if (startNav is not null) lastNav[h] = startNav.Value;
            }

            foreach (var d in segmentDates)
            {
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

                if (n == 0) continue;

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
            "Monthly rebalance engine"
        );

        return (res, curve);
    }
}
