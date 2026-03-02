using ClawInv.Core.Backtest;
using ClawInv.Core.Research;

namespace ClawInv.Core.Strategies.Logic;

internal static class RegimeRiskOff
{
    public static bool IsRiskOn(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly rebalanceDate)
    {
        if (strat.Regime == RegimeKind.None)
            return true;

        // Index = equal-weight of all funds using NAV-at-or-before monthly samples.
        decimal? IndexNav(DateOnly d)
        {
            decimal sum = 0m;
            var n = 0;
            foreach (var s in series)
            {
                if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                    continue;

                var nav = StrategyNavHelpers.NavAtOrBefore(pts, d);
                if (nav is null) continue;
                sum += nav.Value;
                n++;
            }
            return n > 0 ? sum / n : null;
        }

        if (strat.Regime == RegimeKind.Breadth)
        {
            // Breadth = fraction of funds above 12m MA.
            var above = 0;
            var n = 0;
            foreach (var s in series)
            {
                if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                    continue;

                var navNow = StrategyNavHelpers.NavAtOrBefore(pts, rebalanceDate);
                if (navNow is null) continue;

                var ma = MovingAverageNav(pts, rebalanceDate, 12);
                if (ma is null || ma.Value <= 0m) continue;

                n++;
                if (navNow.Value > ma.Value) above++;
            }

            if (n == 0) return false;
            var breadth = (double)above / n;
            return breadth >= strat.RegimeThreshold;
        }

        if (strat.Regime == RegimeKind.IndexTrend)
        {
            var maN = Math.Max(2, strat.RegimeMaMonths);
            var now = IndexNav(rebalanceDate);
            if (now is null) return false;

            decimal sum = 0m;
            var n = 0;
            for (var i = 0; i <= maN; i++)
            {
                var v = IndexNav(rebalanceDate.AddMonths(-i));
                if (v is null) continue;
                sum += v.Value;
                n++;
            }

            if (n < Math.Max(2, maN / 2)) return false;
            var maVal = sum / n;
            return now.Value > maVal;
        }

        if (strat.Regime == RegimeKind.IndexRsi)
        {
            var rsi = IndexRsi(IndexNav, rebalanceDate, 14);
            if (rsi is null) return false;
            return rsi.Value >= strat.RegimeThreshold;
        }

        return true;
    }

    public static string? SelectDefensiveFund(
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
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            var v = VolAnnualized(pts, volStart, rebalanceDate);
            if (v is null) continue;

            if (bestVol is null || v.Value < bestVol.Value)
            {
                bestVol = v.Value;
                best = s.OrderbookId;
            }
        }

        return best;
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

    private static double? IndexRsi(Func<DateOnly, decimal?> indexNav, DateOnly t, int period)
    {
        if (period < 2) return null;

        // Monthly steps
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

    private static decimal? VolAnnualized(NavPoint[] points, DateOnly from, DateOnly to)
    {
        // Daily returns based on available NAV points in range.
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
