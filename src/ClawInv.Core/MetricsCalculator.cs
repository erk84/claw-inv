namespace ClawInv.Core;

public static class MetricsCalculator
{
    public static Metrics Compute(IReadOnlyList<NavPoint> nav)
    {
        if (nav.Count < 2)
            throw new ArgumentException("Need at least 2 NAV points", nameof(nav));

        var start = nav[0].Date;
        var end = nav[^1].Date;
        var days = end.DayNumber - start.DayNumber;
        if (days <= 0)
            throw new InvalidOperationException("Non-positive date range");

        var startNav = nav[0].Nav;
        var endNav = nav[^1].Nav;

        var years = (double)days / 365.25;
        var cagr = (decimal)(Math.Pow((double)(endNav / startNav), 1.0 / years) - 1.0);

        // daily returns
        var rets = new List<double>(nav.Count - 1);
        for (var i = 1; i < nav.Count; i++)
        {
            var r = (double)((nav[i].Nav / nav[i - 1].Nav) - 1m);
            rets.Add(r);
        }

        var vol = AnnualizedVolatility(rets);
        decimal? sharpe = vol > 0 ? cagr / vol : null;

        var mdd = MaxDrawdown(nav.Select(p => p.Nav).ToList());

        return new Metrics(start, end, days, cagr, vol, sharpe, mdd);
    }

    private static decimal AnnualizedVolatility(IReadOnlyList<double> dailyReturns)
    {
        if (dailyReturns.Count < 2) return 0m;

        var mean = dailyReturns.Average();
        var varSum = 0.0;
        foreach (var r in dailyReturns)
            varSum += (r - mean) * (r - mean);

        var variance = varSum / (dailyReturns.Count - 1);
        var stdev = Math.Sqrt(variance);
        var annualized = stdev * Math.Sqrt(252.0);
        return (decimal)annualized;
    }

    private static decimal MaxDrawdown(IReadOnlyList<decimal> equity)
    {
        decimal peak = decimal.MinValue;
        decimal mdd = 0m;

        foreach (var x in equity)
        {
            peak = Math.Max(peak, x);
            var dd = (x / peak) - 1m;
            mdd = Math.Min(mdd, dd);
        }

        return mdd;
    }
}
