using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class CorrFilteredTopKLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.CorrFilteredTopK;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        var k = Math.Max(1, strat.TopK);
        var momLb = Math.Max(1, strat.LookbackMonths);
        var corrLb = Math.Max(12, strat.VolatilityLookbackMonths);

        // 1) Candidate pool: top momentum (and optional abs momentum filter)
        var candidates = series
            .Select(s => (id: s.OrderbookId, mom: StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, asOf, momLb)))
            .Where(x => x.mom is not null && !double.IsNaN(x.mom.Value))
            .Where(x => !strat.UseAbsoluteMomentumFilter || x.mom!.Value > 0)
            .OrderByDescending(x => x.mom!.Value)
            .Take(Math.Max(60, k * 20))
            .Select(x => x.id)
            .ToArray();

        if (candidates.Length == 0)
            return new Dictionary<string, decimal>();

        // 2) Prepare monthly return vectors for correlation
        var ret = candidates
            .Select(id => (id, r: MonthlyReturns(fundIndex, id, asOf, corrLb).ToArray()))
            .Where(x => x.r.Length >= 6)
            .ToDictionary(x => x.id, x => x.r);

        var usable = candidates.Where(ret.ContainsKey).ToArray();
        if (usable.Length == 0)
            return new Dictionary<string, decimal>();

        // 3) Greedy selection: start with best momentum, then add fund minimizing avg abs corr.
        var selected = new List<string> { usable[0] };

        while (selected.Count < k)
        {
            string bestId = "";
            double bestScore = double.PositiveInfinity;

            foreach (var id in usable)
            {
                if (selected.Contains(id)) continue;

                var score = AvgAbsCorrToSet(ret, id, selected);
                if (double.IsNaN(score)) continue;

                if (score < bestScore)
                {
                    bestScore = score;
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

    private static double AvgAbsCorrToSet(IReadOnlyDictionary<string, double[]> ret, string id, IReadOnlyList<string> set)
    {
        if (set.Count == 0) return 0.0;

        var sum = 0.0;
        var n = 0;
        foreach (var s in set)
        {
            var c = Correlation(ret[id], ret[s]);
            if (double.IsNaN(c)) continue;
            sum += Math.Abs(c);
            n++;
        }

        return n == 0 ? double.NaN : sum / n;
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

    private static double Correlation(double[] a, double[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n < 2) return double.NaN;

        var xa = a.Take(n).ToArray();
        var xb = b.Take(n).ToArray();

        var ma = xa.Average();
        var mb = xb.Average();

        var cov = 0.0;
        var va = 0.0;
        var vb = 0.0;

        for (var i = 0; i < n; i++)
        {
            var da = xa[i] - ma;
            var db = xb[i] - mb;
            cov += da * db;
            va += da * da;
            vb += db * db;
        }

        if (va <= 0 || vb <= 0) return double.NaN;
        return cov / Math.Sqrt(va * vb);
    }
}
