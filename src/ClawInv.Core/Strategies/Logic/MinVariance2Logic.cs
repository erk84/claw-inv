using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class MinVariance2Logic : IStrategyLogic
{
    public StrategyType Type => StrategyType.MinVariance2;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Keep behavior: choose pair with minimum variance based on 1M returns over lookback window.
        // NOTE: This remains a "2" strategy for now; generalized K will be implemented next.
        if (strat.TopK != 2)
            throw new ArgumentException("MinVariance2 requires TopK=2 (for now)");

        var lb = Math.Max(12, strat.VolatilityLookbackMonths);

        // Candidate pool: low-vol funds first (simple approx)
        var candidates = series
            .Select(s => (s.OrderbookId, vol: VolApprox(fundIndex, s.OrderbookId, asOf, lb)))
            .Where(x => !double.IsNaN(x.vol))
            .OrderBy(x => x.vol)
            .Take(40)
            .Select(x => x.OrderbookId)
            .ToArray();

        if (candidates.Length < 2)
            return new Dictionary<string, decimal>();

        (string a, string b, double v) best = ("", "", double.PositiveInfinity);

        for (var i = 0; i < candidates.Length; i++)
        {
            for (var j = i + 1; j < candidates.Length; j++)
            {
                var v = PairVariance(fundIndex, candidates[i], candidates[j], asOf, lb);
                if (double.IsNaN(v)) continue;
                if (v < best.v) best = (candidates[i], candidates[j], v);
            }
        }

        if (double.IsInfinity(best.v))
            return new Dictionary<string, decimal>();

        return new Dictionary<string, decimal>
        {
            [best.a] = 0.5m,
            [best.b] = 0.5m
        };
    }

    private static double VolApprox(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string id, DateOnly asOf, int months)
    {
        var vals = new List<double>();
        for (var i = months; i >= 1; i--)
        {
            var d = asOf.AddMonths(-i + 1);
            var r = StrategyNavHelpers.MonthlyReturn(fundIndex, id, d, 1);
            if (r is not null && !double.IsNaN(r.Value))
                vals.Add(r.Value);
        }
        if (vals.Count < 2) return double.NaN;
        var mean = vals.Average();
        var varSum = vals.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (vals.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(12.0);
    }

    private static double PairVariance(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string a, string b, DateOnly asOf, int months)
    {
        var ra = MonthlyReturns(fundIndex, a, asOf, months);
        var rb = MonthlyReturns(fundIndex, b, asOf, months);
        if (ra.Count < 2 || rb.Count < 2) return double.NaN;

        var n = Math.Min(ra.Count, rb.Count);
        var xa = ra.Take(n).ToArray();
        var xb = rb.Take(n).ToArray();

        var va = Variance(xa);
        var vb = Variance(xb);
        var cov = Covariance(xa, xb);

        // equal weight portfolio variance
        return 0.25 * va + 0.25 * vb + 0.5 * cov;
    }

    private static List<double> MonthlyReturns(IReadOnlyDictionary<string, NavPoint[]> fundIndex, string id, DateOnly asOf, int months)
    {
        var vals = new List<double>();
        for (var i = months; i >= 1; i--)
        {
            var d = asOf.AddMonths(-i + 1);
            var r = StrategyNavHelpers.MonthlyReturn(fundIndex, id, d, 1);
            if (r is not null && !double.IsNaN(r.Value))
                vals.Add(r.Value);
        }
        return vals;
    }

    private static double Variance(double[] x)
    {
        if (x.Length < 2) return double.NaN;
        var mean = x.Average();
        var varSum = x.Sum(v => (v - mean) * (v - mean));
        return varSum / (x.Length - 1);
    }

    private static double Covariance(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n < 2) return double.NaN;
        var ma = a.Take(n).Average();
        var mb = b.Take(n).Average();
        var sum = 0.0;
        for (var i = 0; i < n; i++)
            sum += (a[i] - ma) * (b[i] - mb);
        return sum / (n - 1);
    }
}
