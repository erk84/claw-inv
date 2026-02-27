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
        var T = _m.Dates.Length;
        var F = _m.FundIds.Length;

        int lb = Math.Max(1, p.LookbackMonths);
        int volLb = Math.Max(2, p.VolLookbackMonths);
        int ma = Math.Max(1, p.TrendMaMonths);

        var warmup = Math.Max(lb, Math.Max(volLb, ma)) + 2;
        if (warmup >= T)
            return new TrialResult(p, double.NaN, double.NaN, double.NaN, double.NegativeInfinity);

        var equity = 1.0;
        var peak = 1.0;
        var mdd = 0.0;

        var holdings = new List<int>();

        for (var t = 1; t < T; t++)
        {
            // Rebalance at month t using info at t-1 (avoid lookahead)
            if (t >= warmup && (t % Math.Max(1, p.RebalanceMonths) == 0))
            {
                holdings.Clear();

                var infoT = t - 1;

                var selected = p.Kind switch
                {
                    ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                    ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                    ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                    ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb),
                    _ => Array.Empty<int>()
                };

                holdings.AddRange(selected);
            }

            // apply return from t-1 -> t on current holdings
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

            equity *= (1.0 + r);
            peak = Math.Max(peak, equity);
            var dd = equity / peak - 1.0;
            mdd = Math.Min(mdd, dd);

            // hard constraint early exit
            if (mdd < p.MaxDrawdownFloor)
                return new TrialResult(p, double.NaN, double.NaN, mdd, double.NegativeInfinity);
        }

        // compute metrics using monthly equity returns
        var rets = new List<double>();
        // approximate: use last (T-warmup) months
        var equitySeries = new List<double> { 1.0 };
        // We didn't store full series for speed; recompute monthly returns from selection is costly.
        // Instead: approximate Sharpe from realized monthly returns computed above by replaying quickly.
        // Simpler: compute with a second pass but only for warmup..end.
        // (acceptable, still fast)

        equity = 1.0;
        holdings.Clear();
        for (var t = 1; t < T; t++)
        {
            if (t >= warmup && (t % Math.Max(1, p.RebalanceMonths) == 0))
            {
                holdings.Clear();
                var infoT = t - 1;
                var selected = p.Kind switch
                {
                    ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                    ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                    ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                    ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb),
                    _ => Array.Empty<int>()
                };
                holdings.AddRange(selected);
            }

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

            if (t >= warmup)
                rets.Add(r);

            equity *= (1.0 + r);
        }

        var sharpe = SharpeMonthly(rets);

        var months = rets.Count;
        var years = months / 12.0;
        var cagr = years > 0 ? Math.Pow(equity, 1.0 / years) - 1.0 : double.NaN;

        // Score = Sharpe (since MDD is a hard constraint)
        return new TrialResult(p, sharpe, cagr, mdd, sharpe);
    }

    private int[] SelectMomentum(TrialParams p, int infoT, int lb, int volLb, int ma)
    {
        var F = _m.FundIds.Length;
        var arr = new (int f, double mom)[F];
        var count = 0;

        for (var f = 0; f < F; f++)
        {
            var navNow = _m.Nav[infoT, f];
            var navThen = _m.Nav[infoT - lb, f];
            if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;

            // Optional trend gate for momentum: require NAV above MA.
            // If ma < 2 => no trend gate.
            if (ma >= 2)
            {
                var maVal = MovingAverage(_m.Nav, infoT, f, ma);
                if (double.IsNaN(maVal) || maVal <= 0) continue;
                if (navNow <= maVal) continue;
            }

            var mom = navNow / navThen - 1.0;
            arr[count++] = (f, mom);
        }

        if (count == 0) return [];

        Array.Sort(arr, 0, count, Comparer<(int f, double mom)>.Create((a, b) => b.mom.CompareTo(a.mom)));

        if (p.UseAbsoluteMomentum && arr[0].mom <= 0)
            return []; // CASH

        var candidates = arr.Take(count).Select(x => x.f).ToList();

        // low-vol as secondary filter: choose lowest vol among momentum-ranked list
        if (p.VolLookbackMonths >= 2)
        {
            var vols = new List<(int f, double v)>();
            foreach (var f in candidates)
            {
                var v = Volatility(_m.Ret1M, infoT, f, volLb);
                if (!double.IsNaN(v)) vols.Add((f, v));
            }
            if (vols.Count > 0)
            {
                vols.Sort((a, b) => a.v.CompareTo(b.v));
                candidates = vols.Select(x => x.f).ToList();
            }
        }

        var k = Math.Max(1, Math.Min(p.TopK, candidates.Count));
        return candidates.Take(k).ToArray();
    }

    private int[] SelectLowVol(TrialParams p, int infoT, int volLb)
    {
        var F = _m.FundIds.Length;
        var vols = new List<(int f, double v)>(F);
        for (var f = 0; f < F; f++)
        {
            var v = Volatility(_m.Ret1M, infoT, f, volLb);
            if (!double.IsNaN(v)) vols.Add((f, v));
        }

        if (vols.Count == 0) return [];
        vols.Sort((a, b) => a.v.CompareTo(b.v));

        var k = Math.Max(1, Math.Min(p.TopK, vols.Count));
        return vols.Take(k).Select(x => x.f).ToArray();
    }

    private int[] SelectTrend(TrialParams p, int infoT, int ma)
    {
        var F = _m.FundIds.Length;
        var scored = new List<(int f, double score)>(F);

        for (var f = 0; f < F; f++)
        {
            var navNow = _m.Nav[infoT, f];
            if (double.IsNaN(navNow)) continue;
            var maVal = MovingAverage(_m.Nav, infoT, f, ma);
            if (double.IsNaN(maVal) || maVal == 0) continue;
            var rel = navNow / maVal - 1.0;
            if (rel > 0) scored.Add((f, rel));
        }

        if (scored.Count == 0) return [];
        scored.Sort((a, b) => b.score.CompareTo(a.score));

        var k = Math.Max(1, Math.Min(p.TopK, scored.Count));
        return scored.Take(k).Select(x => x.f).ToArray();
    }

    private int[] SelectMeanReversion(TrialParams p, int infoT, int lb)
    {
        var F = _m.FundIds.Length;
        var arr = new (int f, double mom)[F];
        var count = 0;

        for (var f = 0; f < F; f++)
        {
            var navNow = _m.Nav[infoT, f];
            var navThen = _m.Nav[infoT - lb, f];
            if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;
            var mom = navNow / navThen - 1.0;
            arr[count++] = (f, mom);
        }

        if (count == 0) return [];

        // mean reversion: pick the WORST performers (most negative mom)
        Array.Sort(arr, 0, count, Comparer<(int f, double mom)>.Create((a, b) => a.mom.CompareTo(b.mom)));
        var k = Math.Max(1, Math.Min(p.TopK, count));
        return arr.Take(k).Select(x => x.f).ToArray();
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
        if (monthlyReturns.Count < 6) return double.NaN;
        var mean = monthlyReturns.Average();
        var varSum = monthlyReturns.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (monthlyReturns.Count - 1);
        var stdev = Math.Sqrt(variance);
        if (stdev == 0) return double.NaN;
        return (mean / stdev) * Math.Sqrt(12.0);
    }


    public TrialTrace Trace(TrialParams p)
    {
        var T = _m.Dates.Length;
        var F = _m.FundIds.Length;

        int lb = Math.Max(1, p.LookbackMonths);
        int volLb = Math.Max(2, p.VolLookbackMonths);
        int ma = Math.Max(1, p.TrendMaMonths);

        var warmup = Math.Max(lb, Math.Max(volLb, ma)) + 2;
        if (warmup >= T)
            return new TrialTrace(p, Array.Empty<RebalanceEvent>(), 1.0, double.NaN, double.NaN);

        var equity = 1.0;
        var peak = 1.0;
        var mdd = 0.0;

        var holdings = new List<int>();
        var events = new List<RebalanceEvent>();

        for (var t = 1; t < T; t++)
        {
            if (t >= warmup && (t % Math.Max(1, p.RebalanceMonths) == 0))
            {
                holdings.Clear();
                var infoT = t - 1;

                var selected = p.Kind switch
                {
                    ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                    ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                    ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                    ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb),
                    _ => Array.Empty<int>()
                };

                holdings.AddRange(selected);

                double? bestMom = null;
                if (p.Kind == ResearchStrategyKind.Momentum)
                {
                    // compute best momentum for transparency
                    var best = double.NegativeInfinity;
                    for (var f = 0; f < F; f++)
                    {
                        var navNow = _m.Nav[infoT, f];
                        var navThen = _m.Nav[infoT - lb, f];
                        if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;
                        var mom = navNow / navThen - 1.0;
                        if (mom > best) best = mom;
                    }
                    if (!double.IsNegativeInfinity(best)) bestMom = best;
                }

                events.Add(new RebalanceEvent(
                    Date: _m.Dates[t],
                    Kind: "REBALANCE",
                    Holdings: holdings.Select(i => _m.FundIds[i]).ToArray(),
                    BestMomentum: bestMom,
                    AppliedReturn: null,
                    Equity: equity));
            }

            // apply return t-1->t
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

            equity *= (1.0 + r);
            peak = Math.Max(peak, equity);
            var dd = equity / peak - 1.0;
            mdd = Math.Min(mdd, dd);

            // annotate last event with applied return when month matches
            if (events.Count > 0 && events[^1].Date == _m.Dates[t] && events[^1].AppliedReturn is null)
            {
                var last = events[^1];
                events[^1] = last with { AppliedReturn = r, Equity = equity };
            }
        }

        var years = (_m.Dates[^1].DayNumber - _m.Dates[0].DayNumber) / 365.2425;
        var cagr = years > 0 ? Math.Pow(equity, 1.0 / years) - 1.0 : double.NaN;

        return new TrialTrace(p, events, equity, cagr, mdd);
    }
}
