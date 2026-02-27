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

        return new FeatureMatrices
        {
            Dates = dates,
            FundIds = fundIds,
            FundIndex = fundIndex,
            Nav = nav,
            Ret1M = ret1,
        };
    }
}
