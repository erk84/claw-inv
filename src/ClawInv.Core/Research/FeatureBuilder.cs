using ClawInv.Core.Backtest;

namespace ClawInv.Core.Research;

public static class FeatureBuilder
{
    public static FeatureMatrices BuildMonthEndMatrices(IReadOnlyList<NavSeries> series)
    {
        var fundIds = series.Select(s => s.OrderbookId).ToArray();
        var fundIndex = fundIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        // month-end points per fund
        var perFund = series.ToDictionary(
            s => s.OrderbookId,
            s => MonthlySeriesBuilder.ToMonthEnd(s.Points));

        // common calendar = union of all month-end dates
        var dates = perFund.Values
            .SelectMany(x => x.Select(p => p.Date))
            .Distinct()
            .OrderBy(d => d)
            .ToArray();

        var T = dates.Length;
        var F = fundIds.Length;

        var nav = new double[T, F];
        var ret1 = new double[T, F];

        for (var f = 0; f < F; f++)
        {
            var id = fundIds[f];
            var pts = perFund[id];
            var dict = pts.ToDictionary(p => p.Date, p => (double)p.Nav);

            double last = double.NaN;
            for (var t = 0; t < T; t++)
            {
                var d = dates[t];
                if (dict.TryGetValue(d, out var v))
                    last = v;

                nav[t, f] = last;

                if (t == 0 || double.IsNaN(nav[t - 1, f]) || double.IsNaN(nav[t, f]))
                    ret1[t, f] = double.NaN;
                else
                    ret1[t, f] = nav[t, f] / nav[t - 1, f] - 1.0;
            }
        }

        // Build equal-weight index NAV (normalized to 1 at first valid point)
        var indexNav = new double[T];
        var idxBase = double.NaN;
        for (var t = 0; t < T; t++)
        {
            var sum = 0.0;
            var n = 0;
            for (var f = 0; f < F; f++)
            {
                var v = nav[t, f];
                if (double.IsNaN(v)) continue;
                sum += v;
                n++;
            }

            var avg = n > 0 ? sum / n : double.NaN;
            if (double.IsNaN(idxBase) && !double.IsNaN(avg)) idxBase = avg;
            indexNav[t] = (!double.IsNaN(avg) && idxBase > 0) ? (avg / idxBase) : double.NaN;
        }

        // Breadth: fraction of funds above their 12-month MA
        var breadth12 = new double[T];
        for (var t = 0; t < T; t++)
        {
            if (t < 12) { breadth12[t] = double.NaN; continue; }

            var ok = 0;
            var tot = 0;
            for (var f = 0; f < F; f++)
            {
                var now = nav[t, f];
                if (double.IsNaN(now)) continue;

                var sumMa = 0.0;
                var nMa = 0;
                for (var i = t - 12; i <= t; i++)
                {
                    var vv = nav[i, f];
                    if (double.IsNaN(vv)) continue;
                    sumMa += vv;
                    nMa++;
                }

                if (nMa < 6) continue;
                var maVal = sumMa / nMa;
                tot++;
                if (now > maVal) ok++;
            }

            breadth12[t] = tot > 0 ? (double)ok / tot : double.NaN;
        }

        return new FeatureMatrices
        {
            Dates = dates,
            FundIds = fundIds,
            FundIndex = fundIndex,
            Nav = nav,
            Ret1M = ret1,
            IndexNav = indexNav,
            Breadth12 = breadth12,
        };
    }
}
