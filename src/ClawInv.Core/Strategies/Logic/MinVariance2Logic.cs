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
        // Generalized min-variance (equal-weight) selection for K holdings.
        // This keeps the old behavior for K=2 but supports any K>=1.
        var k = Math.Max(1, strat.TopK);
        var lb = Math.Max(12, strat.VolatilityLookbackMonths);

        // Candidate pool: low-vol funds first (simple approx) for speed.
        // Make sure pool >= k.
        var poolSize = Math.Max(40, k * 10);
        var candidates = series
            .Select(s => (s.OrderbookId, vol: VolApprox(fundIndex, s.OrderbookId, asOf, lb)))
            .Where(x => !double.IsNaN(x.vol))
            .OrderBy(x => x.vol)
            .Take(poolSize)
            .Select(x => x.OrderbookId)
            .ToArray();

        if (candidates.Length == 0)
            return new Dictionary<string, decimal>();

        if (k == 1)
            return new Dictionary<string, decimal> { [candidates[0]] = 1.0m };

        // Precompute aligned monthly return vectors for candidates.
        var ret = candidates
            .Select(id => (id, r: MonthlyReturns(fundIndex, id, asOf, lb).ToArray()))
            .Where(x => x.r.Length >= 6)
            .ToDictionary(x => x.id, x => x.r);

        var usable = candidates.Where(ret.ContainsKey).ToArray();
        if (usable.Length < k)
            return new Dictionary<string, decimal>();

        // Start with lowest vol fund.
        var selected = new List<string> { usable[0] };

        // Greedy add: choose fund that minimizes equal-weight portfolio variance.
        while (selected.Count < k)
        {
            string bestId = "";
            double bestVar = double.PositiveInfinity;

            foreach (var id in usable)
            {
                if (selected.Contains(id)) continue;

                var trial = selected.Concat([id]).ToArray();
                var v = PortfolioVarianceEqualWeight(ret, trial);
                if (double.IsNaN(v)) continue;
                if (v < bestVar)
                {
                    bestVar = v;
                    bestId = id;
                }
            }

            if (string.IsNullOrEmpty(bestId))
                break;

            selected.Add(bestId);
        }

        if (selected.Count == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / selected.Count;
        return selected.ToDictionary(x => x, _ => w);
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

    private static double PortfolioVarianceEqualWeight(IReadOnlyDictionary<string, double[]> ret, string[] ids)
    {
        if (ids.Length < 1) return double.NaN;
        if (ids.Length == 1) return Variance(ret[ids[0]]);

        // Align by taking min length across vectors.
        var n = ids.Select(id => ret[id].Length).Min();
        if (n < 2) return double.NaN;

        var k = ids.Length;
        var w = 1.0 / k;

        // Compute portfolio return series as equal-weight sum.
        var pr = new double[n];
        for (var t = 0; t < n; t++)
        {
            var sum = 0.0;
            for (var i = 0; i < k; i++)
                sum += ret[ids[i]][t];
            pr[t] = w * sum;
        }

        return Variance(pr);
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
