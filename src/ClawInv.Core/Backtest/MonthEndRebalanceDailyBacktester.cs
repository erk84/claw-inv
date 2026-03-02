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
    public static IReadOnlyList<string> SelectHoldingsAt(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly asOf)
    {
        if (strat.TopK > 2)
            throw new ArgumentException("MonthEndRebalanceDailyBacktester supports TopK<=2.");

        var fundIndex = series.ToDictionary(s => s.OrderbookId, s => s.Points.OrderBy(p => p.Date).ToArray());
        var holdings = ChooseHoldings(strat, series, fundIndex, asOf);
        return holdings.Keys.OrderBy(x => x).ToArray();
    }

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
        if (!IsRiskOn(strat, series, fundIndex, rebalanceDate))
        {
            if (strat.RiskOffMode == RiskOffMode.DefensiveFund)
            {
                var def = SelectDefensiveFund(series, fundIndex, rebalanceDate, strat.DefensiveVolLookbackMonths);
                return def is null ? new() : EqualWeight([def]);
            }

            return new(); // CASH
        }

        return strat.Type switch
        {
            StrategyType.LowVolatilitySelection => ChooseLowVol(strat, series, fundIndex, rebalanceDate),
            StrategyType.TrendFollowing => ChooseTrend(strat, series, fundIndex, rebalanceDate),
            StrategyType.MeanReversionRotation => ChooseMeanReversion(strat, series, fundIndex, rebalanceDate),
            StrategyType.MinVariance2 => ChooseMinVariance2(strat, series, fundIndex, rebalanceDate),
            _ => ChooseMomentum(strat, series, fundIndex, rebalanceDate)
        };
    }

    private static Dictionary<string, decimal> ChooseMomentum(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        var lb = Math.Max(1, strat.LookbackMonths);
        var lbDate = rebalanceDate.AddMonths(-lb);

        var candidates = new List<(string id, decimal mom)>();

        foreach (var s in series)
        {
            var navNow = NavAtOrBefore(fundIndex[s.OrderbookId], rebalanceDate);
            var navThen = NavAtOrBefore(fundIndex[s.OrderbookId], lbDate);
            if (navNow is null || navThen is null) continue;
            if (navThen.Nav <= 0) continue;

            // optional trend-gate via fund MA
            if (strat.MovingAverageMonths >= 2)
            {
                var maVal = MovingAverageNav(fundIndex[s.OrderbookId], rebalanceDate, strat.MovingAverageMonths);
                if (maVal is null || maVal.Value <= 0) continue;
                if (navNow.Nav <= maVal.Value) continue;
            }

            var mom = navNow.Nav / navThen.Nav - 1.0m;
            candidates.Add((s.OrderbookId, mom));
        }

        if (candidates.Count == 0)
            return new();

        candidates.Sort((a, b) => b.mom.CompareTo(a.mom));

        if (strat.UseAbsoluteMomentumFilter && candidates[0].mom <= 0)
            return new();

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

    private static Dictionary<string, decimal> ChooseLowVol(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        var volLb = Math.Max(2, strat.VolatilityLookbackMonths);
        var volStart = rebalanceDate.AddMonths(-volLb);

        var vols = new List<(string id, decimal v)>();
        foreach (var s in series)
        {
            var v = VolAnnualized(fundIndex[s.OrderbookId], volStart, rebalanceDate);
            if (v.HasValue) vols.Add((s.OrderbookId, v.Value));
        }

        if (vols.Count == 0) return new();
        vols.Sort((a, b) => a.v.CompareTo(b.v));

        var k = Math.Max(1, Math.Min(strat.TopK, vols.Count));
        return EqualWeight(vols.Take(k).Select(x => x.id).ToArray());
    }

    private static Dictionary<string, decimal> ChooseTrend(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        var ma = Math.Max(2, strat.MovingAverageMonths);

        var scored = new List<(string id, decimal rel)>();
        foreach (var s in series)
        {
            var navNow = NavAtOrBefore(fundIndex[s.OrderbookId], rebalanceDate);
            if (navNow is null) continue;
            var maVal = MovingAverageNav(fundIndex[s.OrderbookId], rebalanceDate, ma);
            if (maVal is null || maVal.Value <= 0) continue;
            var rel = navNow.Nav / maVal.Value - 1m;
            if (rel > 0) scored.Add((s.OrderbookId, rel));
        }

        if (scored.Count == 0) return new();
        scored.Sort((a, b) => b.rel.CompareTo(a.rel));

        var k = Math.Max(1, Math.Min(strat.TopK, scored.Count));
        return EqualWeight(scored.Take(k).Select(x => x.id).ToArray());
    }

    private static Dictionary<string, decimal> ChooseMeanReversion(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        var lb = Math.Max(1, strat.LookbackMonths);
        var lbDate = rebalanceDate.AddMonths(-lb);

        var candidates = new List<(string id, decimal mom)>();
        foreach (var s in series)
        {
            var navNow = NavAtOrBefore(fundIndex[s.OrderbookId], rebalanceDate);
            var navThen = NavAtOrBefore(fundIndex[s.OrderbookId], lbDate);
            if (navNow is null || navThen is null) continue;
            if (navThen.Nav <= 0) continue;

            // trend gate
            if (strat.MovingAverageMonths >= 2)
            {
                var maVal = MovingAverageNav(fundIndex[s.OrderbookId], rebalanceDate, strat.MovingAverageMonths);
                if (maVal is null || maVal.Value <= 0) continue;
                if (navNow.Nav <= maVal.Value) continue;
            }

            var mom = navNow.Nav / navThen.Nav - 1.0m;
            candidates.Add((s.OrderbookId, mom));
        }

        if (candidates.Count == 0) return new();

        // mean reversion: pick WORST momentum
        candidates.Sort((a, b) => a.mom.CompareTo(b.mom));
        var k = Math.Max(1, Math.Min(strat.TopK, candidates.Count));
        return EqualWeight(candidates.Take(k).Select(x => x.id).ToArray());
    }

    private static Dictionary<string, decimal> ChooseMinVariance2(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        // Use a small pool of low-vol candidates; then pick the 2-fund equal-weight pair with min variance.
        var lb = Math.Max(6, strat.LookbackMonths);
        var volStart = rebalanceDate.AddMonths(-lb);

        var vols = new List<(string id, decimal v)>();
        foreach (var s in series)
        {
            var v = VolAnnualized(fundIndex[s.OrderbookId], volStart, rebalanceDate);
            if (v.HasValue) vols.Add((s.OrderbookId, v.Value));
        }

        if (vols.Count == 0) return new();
        vols.Sort((a, b) => a.v.CompareTo(b.v));

        var pool = vols.Take(Math.Min(20, vols.Count)).Select(x => x.id).ToArray();
        if (pool.Length == 1) return EqualWeight([pool[0]]);

        (string a, string b)? best = null;
        decimal? bestVar = null;

        for (var i = 0; i < pool.Length; i++)
        for (var j = i + 1; j < pool.Length; j++)
        {
            var v = PairVarianceDaily(fundIndex[pool[i]], fundIndex[pool[j]], volStart, rebalanceDate);
            if (v is null) continue;
            if (bestVar is null || v.Value < bestVar.Value)
            {
                bestVar = v;
                best = (pool[i], pool[j]);
            }
        }

        if (best is null) return EqualWeight([pool[0]]);
        return EqualWeight([best.Value.a, best.Value.b]);
    }

    private static string? SelectDefensiveFund(
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate,
        int volLookbackMonths)
    {
        var volLb = Math.Max(2, volLookbackMonths);
        var volStart = rebalanceDate.AddMonths(-volLb);

        string? best = null;
        decimal? bestVol = null;

        foreach (var s in series)
        {
            var v = VolAnnualized(fundIndex[s.OrderbookId], volStart, rebalanceDate);
            if (v is null) continue;

            if (bestVol is null || v.Value < bestVol.Value)
            {
                bestVol = v;
                best = s.OrderbookId;
            }
        }

        return best;
    }

    private static bool IsRiskOn(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        if (strat.Regime == Research.RegimeKind.None)
            return true;

        // Use index = equal-weight of all funds, based on NAV-at-or-before month-end dates.
        decimal? IndexNav(DateOnly d)
        {
            decimal sum = 0m;
            var n = 0;
            foreach (var s in series)
            {
                var nav = NavAtOrBefore(fundIndex[s.OrderbookId], d);
                if (nav is null) continue;
                sum += nav.Nav;
                n++;
            }
            return n > 0 ? sum / n : null;
        }

        if (strat.Regime == Research.RegimeKind.Breadth)
        {
            // breadth = fraction of funds above 12m MA
            var above = 0;
            var n = 0;
            foreach (var s in series)
            {
                var navNow = NavAtOrBefore(fundIndex[s.OrderbookId], rebalanceDate);
                if (navNow is null) continue;
                var ma = MovingAverageNav(fundIndex[s.OrderbookId], rebalanceDate, 12);
                if (ma is null || ma.Value <= 0) continue;
                n++;
                if (navNow.Nav > ma.Value) above++;
            }

            if (n == 0) return false;
            var breadth = (double)above / n;
            return breadth >= strat.RegimeThreshold;
        }

        if (strat.Regime == Research.RegimeKind.IndexTrend)
        {
            var maN = Math.Max(2, strat.RegimeMaMonths);
            var dates = Enumerable.Range(0, maN + 1).Select(i => rebalanceDate.AddMonths(-i)).ToArray();
            var now = IndexNav(rebalanceDate);
            if (now is null) return false;

            decimal sum = 0m;
            var n = 0;
            foreach (var d in dates)
            {
                var v = IndexNav(d);
                if (v is null) continue;
                sum += v.Value;
                n++;
            }

            if (n < Math.Max(2, maN / 2)) return false;
            var maVal = sum / n;
            return now.Value > maVal;
        }

        if (strat.Regime == Research.RegimeKind.IndexRsi)
        {
            // RSI(14) on monthly samples using month offsets (approx)
            var rsi = IndexRsi(IndexNav, rebalanceDate, 14);
            if (rsi is null) return false;
            return rsi.Value >= strat.RegimeThreshold;
        }

        return true;
    }

    private static double? IndexRsi(Func<DateOnly, decimal?> indexNav, DateOnly t, int period)
    {
        if (period < 2) return null;

        // Use monthly steps
        var navs = new List<decimal>();
        for (var i = period; i >= 0; i--)
        {
            var v = indexNav(t.AddMonths(-i));
            if (v is null) return null;
            navs.Add(v.Value);
        }

        decimal gain = 0m, loss = 0m;
        for (var i = 1; i < navs.Count; i++)
        {
            var chg = navs[i] - navs[i - 1];
            if (chg > 0) gain += chg;
            else loss -= chg;
        }

        if (loss == 0m) return 100.0;
        var rs = (double)(gain / loss);
        return 100.0 - (100.0 / (1.0 + rs));
    }

    private static decimal? MovingAverageNav(NavPoint[] points, DateOnly end, int months)
    {
        if (months <= 0) return null;
        decimal sum = 0m;
        var n = 0;
        for (var i = 0; i <= months; i++)
        {
            var d = end.AddMonths(-i);
            var p = NavAtOrBefore(points, d);
            if (p is null) continue;
            sum += p.Nav;
            n++;
        }
        return n > 0 ? sum / n : null;
    }

    private static decimal? PairVarianceDaily(NavPoint[] a, NavPoint[] b, DateOnly from, DateOnly to)
    {
        // Build daily returns on overlapping dates
        var datesA = a.Select(x => x.Date).Where(d => d >= from && d <= to).Distinct();
        var datesB = b.Select(x => x.Date).Where(d => d >= from && d <= to).Distinct();
        var dates = datesA.Intersect(datesB).OrderBy(d => d).ToArray();
        if (dates.Length < 8) return null;

        var ra = new List<decimal>();
        var rb = new List<decimal>();
        for (var i = 1; i < dates.Length; i++)
        {
            var d0 = dates[i - 1];
            var d1 = dates[i];
            var na0 = NavAtOrBefore(a, d0);
            var na1 = NavAtOrBefore(a, d1);
            var nb0 = NavAtOrBefore(b, d0);
            var nb1 = NavAtOrBefore(b, d1);
            if (na0 is null || na1 is null || nb0 is null || nb1 is null) continue;
            if (na0.Nav <= 0 || nb0.Nav <= 0) continue;
            ra.Add(na1.Nav / na0.Nav - 1m);
            rb.Add(nb1.Nav / nb0.Nav - 1m);
        }

        if (ra.Count < 6) return null;

        var meanA = ra.Average();
        var meanB = rb.Average();
        decimal varA = 0m, varB = 0m, cov = 0m;

        for (var i = 0; i < ra.Count; i++)
        {
            var da = ra[i] - meanA;
            var db = rb[i] - meanB;
            varA += da * da;
            varB += db * db;
            cov += da * db;
        }

        var n = ra.Count - 1;
        varA /= n;
        varB /= n;
        cov /= n;

        return 0.25m * (varA + varB + 2m * cov);
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
