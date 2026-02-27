using ClawInv.Core.Backtest;

namespace ClawInv.Core.Research;

public sealed class StrategySearch
{
    private readonly FeatureMatrices _m;

    public StrategySearch(FeatureMatrices m)
    {
        _m = m;
    }

    public TrialResult Evaluate(TrialParams p)
    {
        // Use monthly returns for speed.
        // Portfolio: equal weight across selected funds each rebalance.
        // Filters:
        // - absolute momentum: if best momentum <= 0 -> CASH (0 return)
        // - trend filter: only consider funds with nav > MA
        // - low vol filter: optionally restrict to lowest-vol subset

        var T = _m.Dates.Length;
        var F = _m.FundIds.Length;

        int lb = p.LookbackMonths;
        int volLb = p.VolLookbackMonths;
        int ma = p.TrendMaMonths;

        // start index after max lookbacks
        var warmup = Math.Max(lb, Math.Max(volLb, ma)) + 1;
        if (warmup >= T) return new TrialResult(p, double.NaN, double.NaN, double.NaN, double.NegativeInfinity);

        var equity = new double[T];
        equity[0] = 1.0;

        // track peak for MDD
        var peak = 1.0;
        var mdd = 0.0;

        // selection for each period
        var holdings = new List<int>();

        for (var t = 1; t < T; t++)
        {
            // rebalance monthly at t when (t % rebalance == 0)
            // IMPORTANT: avoid lookahead. We rebalance *at the end of month t-1* and apply returns from t-1->t.
            if (t >= warmup && (t % Math.Max(1, p.RebalanceMonths) == 0))
            {
                holdings.Clear();

                var infoT = t - 1;
                if (infoT - lb < 0) continue;

                // compute momentum per fund using information available at infoT
                var arr = new (int f, double mom)[F];
                var scoreCount = 0;

                for (var f = 0; f < F; f++)
                {
                    var navNow = _m.Nav[infoT, f];
                    var navThen = _m.Nav[infoT - lb, f];
                    if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;

                    // trend filter
                    if (p.UseTrendFilter)
                    {
                        var maVal = MovingAverage(_m.Nav, infoT, f, ma);
                        if (double.IsNaN(maVal) || navNow <= maVal) continue;
                    }

                    var mom = navNow / navThen - 1.0;
                    arr[scoreCount++] = (f, mom);
                }

                if (scoreCount == 0)
                {
                    // no holdings => CASH
                }
                else
                {
                    Array.Sort(arr, 0, scoreCount, Comparer<(int f, double mom)>.Create((a, b) => b.mom.CompareTo(a.mom)));

                    if (p.UseAbsoluteMomentum && arr[0].mom <= 0)
                    {
                        // CASH
                    }
                    else
                    {
                        // optional low-vol filter: restrict to lowest vol among candidates
                        var candidates = arr.Take(scoreCount).Select(x => x.f).ToList();

                        // when using low-vol, also avoid lookahead: vol measured up to infoT

                        if (p.UseLowVolFilter)
                        {
                            // compute vol per candidate using monthly returns
                            var vols = new List<(int f, double v)>();
                            foreach (var f in candidates)
                            {
                                var v = Volatility(_m.Ret1M, infoT, f, volLb);
                                if (!double.IsNaN(v)) vols.Add((f, v));
                            }
                            vols.Sort((a, b) => a.v.CompareTo(b.v));
                            candidates = vols.Select(x => x.f).ToList();
                        }

                        var k = Math.Max(1, Math.Min(p.TopK, candidates.Count));
                        holdings.AddRange(candidates.Take(k));
                    }
                }
            }

            // compute portfolio return for month t
            var r = 0.0;
            if (holdings.Count > 0)
            {
                var sum = 0.0;
                var n = 0;
                foreach (var f in holdings)
                {
                    var rr = _m.Ret1M[t, f];
                    if (double.IsNaN(rr)) continue;
                    sum += rr;
                    n++;
                }
                if (n > 0) r = sum / n;
            }

            equity[t] = equity[t - 1] * (1.0 + r);
            peak = Math.Max(peak, equity[t]);
            var dd = equity[t] / peak - 1.0;
            mdd = Math.Min(mdd, dd);
        }

        // metrics from equity curve monthly
        var start = equity[warmup];
        var end = equity[T - 1];
        var months = (T - 1 - warmup);
        var years = months / 12.0;
        var cagr = years > 0 ? Math.Pow(end / start, 1.0 / years) - 1.0 : double.NaN;

        // sharpe: mean monthly / std monthly * sqrt(12)
        var rets = new List<double>();
        for (var t = warmup + 1; t < T; t++)
        {
            var rr = equity[t] / equity[t - 1] - 1.0;
            if (!double.IsNaN(rr) && !double.IsInfinity(rr)) rets.Add(rr);
        }

        var sharpe = SharpeMonthly(rets);

        // score: sharpe - penalty*|mdd|
        var score = sharpe - p.ScoreMddPenalty * Math.Abs(mdd);

        return new TrialResult(p, sharpe, cagr, mdd, score);
    }

    private static double MovingAverage(double[,] nav, int t, int f, int months)
    {
        if (months <= 0) return double.NaN;
        var start = t - months;
        if (start < 0) return double.NaN;
        var sum = 0.0;
        var n = 0;
        for (var i = start; i <= t; i++)
        {
            var v = nav[i, f];
            if (double.IsNaN(v)) continue;
            sum += v;
            n++;
        }
        return n > 0 ? sum / n : double.NaN;
    }

    private static double Volatility(double[,] ret1M, int t, int f, int months)
    {
        if (months <= 1) return double.NaN;
        var start = t - months;
        if (start < 1) return double.NaN;
        var vals = new List<double>();
        for (var i = start; i <= t; i++)
        {
            var v = ret1M[i, f];
            if (double.IsNaN(v)) continue;
            vals.Add(v);
        }
        if (vals.Count < 2) return double.NaN;
        var mean = vals.Average();
        var varSum = vals.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (vals.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(12.0);
    }

    private static double SharpeMonthly(IReadOnlyList<double> monthlyReturns)
    {
        if (monthlyReturns.Count < 3) return double.NaN;
        var mean = monthlyReturns.Average();
        var varSum = monthlyReturns.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (monthlyReturns.Count - 1);
        var stdev = Math.Sqrt(variance);
        if (stdev == 0) return double.NaN;
        return (mean / stdev) * Math.Sqrt(12.0);
    }
}
