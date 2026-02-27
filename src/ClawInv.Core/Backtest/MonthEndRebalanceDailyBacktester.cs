using ClawInv.Core.Strategies;
using ClawInv.Core;

namespace ClawInv.Core.Backtest;

/// <summary>
/// Daily backtester that only rebalances on month-end (last available NAV date each month).
/// This is meant as a daily validation for strategies researched on month-end data.
/// 
/// Supports max 2 holdings (TopK<=2) and CASH risk-off.
/// </summary>
public static class MonthEndRebalanceDailyBacktester
{
    public static BacktestResult Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to,
        decimal maxDrawdownFloor = -1.0m)
    {
        if (strat.TopK > 2)
            throw new ArgumentException("MonthEndRebalanceDailyBacktester supports TopK<=2.");

        // Build dense daily calendar (UTC date grid). We then use nav-at-or-before per fund.
        var allDates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .ToArray();

        if (allDates.Length < 30)
            return new BacktestResult(strat.Id, strat.Name + " (daily month-end rebalance)", from, to, 0, 0m, 0m, null, 0m, 0m, "Insufficient data");

        // Skip weekends to avoid flat/duplicated nav-at-or-before effects
        allDates = allDates.Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).ToArray();

        var monthEnds = GetMonthEndDates(allDates);
        if (monthEnds.Count < 3)
            return new BacktestResult(strat.Id, strat.Name + " (daily month-end rebalance)", from, to, 0, 0m, 0m, null, 0m, 0m, "Insufficient month-ends");

        // Pre-index each fund NAV series for nav-at-or-before lookup
        var fundIndex = series.ToDictionary(s => s.OrderbookId, s => s.Points.OrderBy(p => p.Date).ToArray());

        decimal equity = 1.0m;
        decimal peak = 1.0m;
        decimal mdd = 0.0m;
        var dailyReturns = new List<double>(allDates.Length);

        // Holdings as fundId -> weight
        Dictionary<string, decimal> holdings = new();

        // Rebalance schedule: every N month-ends
        var rebN = Math.Max(1, strat.RebalanceEveryMonths);

        // iterate over month-end periods
        for (var mi = 0; mi < monthEnds.Count - 1; mi++)
        {
            var periodStart = monthEnds[mi];
            var periodEnd = monthEnds[mi + 1];

            if (periodEnd <= from || periodStart >= to)
                continue;

            // rebalance at periodStart if aligned and we have enough lookback history
            var shouldRebalance = (mi % rebN) == 0;
            if (shouldRebalance)
            {
                holdings = ChooseHoldings(strat, series, fundIndex, periodStart);
            }

            // Apply daily returns from day after periodStart up to periodEnd (inclusive)
            var days = allDates.Where(d => d > periodStart && d <= periodEnd).ToArray();
            foreach (var d in days)
            {
                var r = PortfolioDailyReturn(holdings, fundIndex, d);
                dailyReturns.Add((double)r);
                equity *= (1.0m + r);

                if (equity > peak) peak = equity;
                var dd = equity / peak - 1.0m;
                if (dd < mdd) mdd = dd;

                if (maxDrawdownFloor > -0.99m && mdd < maxDrawdownFloor)
                {
                    return new BacktestResult(
                        strat.Id,
                        strat.Name + " (daily month-end rebalance)",
                        from,
                        to,
                        allDates[^1].DayNumber - allDates[0].DayNumber,
                        Cagr(from, d, 1.0m, equity),
                        0m,
                        null,
                        mdd,
                        equity - 1.0m,
                        $"Breached MDD floor {maxDrawdownFloor:P0}"
                    );
                }
            }
        }

        var cagr = Cagr(allDates[0], allDates[^1], 1.0m, equity);
        var sharpe = SharpeDaily(dailyReturns);
        var vol = VolDaily(dailyReturns);

        return new BacktestResult(
            strat.Id,
            strat.Name + " (daily month-end rebalance)",
            allDates[0],
            allDates[^1],
            allDates[^1].DayNumber - allDates[0].DayNumber,
            cagr,
            (decimal)vol,
            sharpe is null ? null : (decimal?)sharpe,
            mdd,
            equity - 1.0m,
            "OK"
        );
    }

    private static List<DateOnly> GetMonthEndDates(DateOnly[] allDates)
    {
        var res = new List<DateOnly>();
        var g = allDates.GroupBy(d => (d.Year, d.Month)).OrderBy(x => x.Key.Year).ThenBy(x => x.Key.Month);
        foreach (var m in g)
            res.Add(m.Max());
        return res;
    }

    private static Dictionary<string, decimal> ChooseHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        // Compute momentum over lookback months (month-end style) using NAV at or before dates.
        var lb = Math.Max(1, strat.LookbackMonths);
        var lbDate = rebalanceDate.AddMonths(-lb);

        var candidates = new List<(string id, decimal mom)>();

        foreach (var s in series)
        {
            var navNow = NavAtOrBefore(fundIndex[s.OrderbookId], rebalanceDate);
            var navThen = NavAtOrBefore(fundIndex[s.OrderbookId], lbDate);
            if (navNow is null || navThen is null) continue;
            if (navThen.Nav <= 0) continue;

            var mom = navNow.Nav / navThen.Nav - 1.0m;
            candidates.Add((s.OrderbookId, mom));
        }

        if (candidates.Count == 0)
            return new();

        candidates.Sort((a, b) => b.mom.CompareTo(a.mom));

        if (strat.UseAbsoluteMomentumFilter && candidates[0].mom <= 0)
            return new(); // CASH

        // low-vol filter: sort by vol among top momentum names (keep it simple: just sort all candidates by vol if enabled)
        if (strat.UseLowVolFilter && strat.VolatilityLookbackMonths >= 2)
        {
            var volLb = strat.VolatilityLookbackMonths;
            var volStart = rebalanceDate.AddMonths(-volLb);
            var vols = new List<(string id, decimal v)>();

            foreach (var c in candidates)
            {
                var v = VolAnnualized(fundIndex[c.id], volStart, rebalanceDate);
                if (v.HasValue) vols.Add((c.id, v.Value));
            }

            if (vols.Count > 0)
            {
                vols.Sort((a, b) => a.v.CompareTo(b.v));
                var k2 = Math.Max(1, Math.Min(strat.TopK, vols.Count));
                var ids = vols.Take(k2).Select(x => x.id).ToArray();
                return EqualWeight(ids);
            }
        }

        var k = Math.Max(1, Math.Min(strat.TopK, candidates.Count));
        return EqualWeight(candidates.Take(k).Select(x => x.id).ToArray());
    }

    private static Dictionary<string, decimal> EqualWeight(string[] ids)
    {
        if (ids.Length == 0) return new();
        var w = 1.0m / ids.Length;
        return ids.Distinct().ToDictionary(x => x, _ => w);
    }

    private static decimal PortfolioDailyReturn(
        IReadOnlyDictionary<string, decimal> holdings,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly d)
    {
        if (holdings.Count == 0) return 0m;

        decimal sum = 0m;
        decimal wsum = 0m;

        foreach (var (id, w) in holdings)
        {
            var navToday = NavAtOrBefore(fundIndex[id], d);
            var navYday = NavAtOrBefore(fundIndex[id], d.AddDays(-1));
            if (navToday is null || navYday is null) continue;
            if (navYday.Nav <= 0) continue;

            var r = navToday.Nav / navYday.Nav - 1.0m;
            sum += w * r;
            wsum += w;
        }

        if (wsum <= 0) return 0m;
        return sum / wsum;
    }

    private static NavPoint? NavAtOrBefore(NavPoint[] points, DateOnly date)
    {
        // points are sorted; binary search for last <= date
        int lo = 0, hi = points.Length - 1;
        NavPoint? best = null;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var p = points[mid];
            if (p.Date <= date)
            {
                best = p;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }

    private static decimal? VolAnnualized(NavPoint[] points, DateOnly from, DateOnly to)
    {
        // Build daily returns from nav-at-or-before dates in range
        var dates = points.Select(p => p.Date).Where(d => d >= from && d <= to).Distinct().OrderBy(d => d).ToArray();
        if (dates.Length < 5) return null;

        var rets = new List<decimal>();
        for (var i = 1; i < dates.Length; i++)
        {
            var a = NavAtOrBefore(points, dates[i - 1]);
            var b = NavAtOrBefore(points, dates[i]);
            if (a is null || b is null) continue;
            if (a.Nav <= 0) continue;
            rets.Add(b.Nav / a.Nav - 1.0m);
        }
        if (rets.Count < 4) return null;

        var mean = rets.Average();
        var varSum = rets.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (rets.Count - 1);
        var stdev = (decimal)Math.Sqrt((double)variance);
        return stdev * (decimal)Math.Sqrt(252.0);
    }

    private static double? SharpeDaily(IReadOnlyList<double> dailyReturns)
    {
        if (dailyReturns.Count < 252) return null;
        var mean = dailyReturns.Average();
        var varSum = dailyReturns.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (dailyReturns.Count - 1);
        var stdev = Math.Sqrt(variance);
        if (stdev == 0) return null;
        return (mean / stdev) * Math.Sqrt(252.0);
    }

    private static double VolDaily(IReadOnlyList<double> dailyReturns)
    {
        if (dailyReturns.Count < 20) return double.NaN;
        var mean = dailyReturns.Average();
        var varSum = dailyReturns.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (dailyReturns.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(252.0);
    }

    private static decimal Cagr(DateOnly start, DateOnly end, decimal startValue, decimal endValue)
    {
        var days = end.DayNumber - start.DayNumber;
        if (days <= 0 || startValue <= 0 || endValue <= 0) return 0m;
        var years = (decimal)days / 365.2425m;
        if (years <= 0) return 0m;
        var ratio = endValue / startValue;
        return (decimal)Math.Pow((double)ratio, (double)(1m / years)) - 1m;
    }
}
