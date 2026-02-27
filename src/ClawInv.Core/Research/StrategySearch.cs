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

                if (!IsRiskOn(p, infoT))
                {
                    if (p.RiskOffMode == ClawInv.Core.Strategies.RiskOffMode.DefensiveFund)
                        holdings.AddRange(SelectDefensive(infoT, p.DefensiveVolLookbackMonths));
                    // else CASH
                }
                else
                {
                    var selected = p.Kind switch
                    {
                        ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                        ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                        ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb, ma),
                        ResearchStrategyKind.MinVariance2 => SelectMinVariance2(p, infoT, lb),
                        ResearchStrategyKind.SharpeProxy => SelectSharpeProxy(p, infoT, lb),
                        ResearchStrategyKind.CorrFilteredTop2 => SelectCorrFilteredTop2(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.BandReversion => SelectBandReversion(p, infoT, lb, ma),
                        _ => Array.Empty<int>()
                    };

                    holdings.AddRange(selected);
                }
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

            // no hard constraint: we penalize drawdown in score instead (more robust search)
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

                if (!IsRiskOn(p, infoT))
                {
                    if (p.RiskOffMode == ClawInv.Core.Strategies.RiskOffMode.DefensiveFund)
                        holdings.AddRange(SelectDefensive(infoT, p.DefensiveVolLookbackMonths));
                    // else CASH
                }
                else
                {
                    var selected = p.Kind switch
                    {
                        ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                        ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                        ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb, ma),
                        ResearchStrategyKind.MinVariance2 => SelectMinVariance2(p, infoT, lb),
                        ResearchStrategyKind.SharpeProxy => SelectSharpeProxy(p, infoT, lb),
                        ResearchStrategyKind.CorrFilteredTop2 => SelectCorrFilteredTop2(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.BandReversion => SelectBandReversion(p, infoT, lb, ma),
                        _ => Array.Empty<int>()
                    };
                    holdings.AddRange(selected);
                }
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

        // Score = Sharpe penalized by drawdown (soft constraint)
        var score = sharpe - p.MaxDrawdownPenaltyLambda * Math.Abs(mdd);
        return new TrialResult(p, sharpe, cagr, mdd, score);
    }

    private bool IsRiskOn(TrialParams p, int infoT)
    {
        if (p.Regime == RegimeKind.None)
            return true;

        if (p.Regime == RegimeKind.IndexTrend)
        {
            var ma = Math.Max(2, p.RegimeMaMonths);
            if (infoT - ma < 0) return false;

            var now = _m.IndexNav[infoT];
            if (double.IsNaN(now)) return false;

            var sum = 0.0;
            var n = 0;
            for (var i = infoT - ma; i <= infoT; i++)
            {
                var v = _m.IndexNav[i];
                if (double.IsNaN(v)) continue;
                sum += v;
                n++;
            }

            if (n < ma / 2) return false;
            var maVal = sum / n;
            return now > maVal;
        }

        if (p.Regime == RegimeKind.Breadth)
        {
            var b = _m.Breadth12[infoT];
            if (double.IsNaN(b)) return false;
            return b >= p.RegimeBreadthThreshold;
        }

        if (p.Regime == RegimeKind.IndexRsi)
        {
            var rsi = Rsi(_m.IndexNav, infoT, 14);
            if (double.IsNaN(rsi)) return false;
            return rsi >= p.RegimeBreadthThreshold; // reuse threshold as RSI level (e.g. 50/55)
        }

        return true;
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
        if (p.UseLowVolFilter && p.VolLookbackMonths >= 2)
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

    private int[] SelectMeanReversion(TrialParams p, int infoT, int lb, int ma)
    {
        var F = _m.FundIds.Length;
        var arr = new (int f, double mom)[F];
        var count = 0;

        for (var f = 0; f < F; f++)
        {
            var navNow = _m.Nav[infoT, f];
            var navThen = _m.Nav[infoT - lb, f];
            if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;

            // Trend gate for mean reversion as well (helps avoid catching falling knives)
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

        // mean reversion: pick the WORST performers (most negative momentum)
        Array.Sort(arr, 0, count, Comparer<(int f, double mom)>.Create((a, b) => a.mom.CompareTo(b.mom)));
        var k = Math.Max(1, Math.Min(p.TopK, count));
        return arr.Take(k).Select(x => x.f).ToArray();
    }

    private int[] SelectDefensive(int infoT, int volLookbackMonths)
    {
        // Defensive = lowest volatility fund (monthly returns) over volLookbackMonths.
        var volLb = Math.Max(2, volLookbackMonths);
        var F = _m.FundIds.Length;

        var bestF = -1;
        var bestVol = double.PositiveInfinity;

        for (var f = 0; f < F; f++)
        {
            var v = Volatility(_m.Ret1M, infoT, f, volLb);
            if (double.IsNaN(v)) continue;
            if (v < bestVol)
            {
                bestVol = v;
                bestF = f;
            }
        }

        return bestF >= 0 ? [bestF] : [];
    }

    private int[] SelectMinVariance2(TrialParams p, int infoT, int lb)
    {
        // Choose 2-fund equal-weight portfolio with minimum variance using monthly returns.
        // To keep it fast, we only consider a small pool of low-vol candidates.
        var F = _m.FundIds.Length;
        var volLb = Math.Max(6, lb);

        var vols = new List<(int f, double v)>(F);
        for (var f = 0; f < F; f++)
        {
            var v = Volatility(_m.Ret1M, infoT, f, volLb);
            if (!double.IsNaN(v)) vols.Add((f, v));
        }

        if (vols.Count == 0) return [];
        vols.Sort((a, b) => a.v.CompareTo(b.v));

        var pool = vols.Take(Math.Min(20, vols.Count)).Select(x => x.f).ToArray();
        if (pool.Length == 1) return [pool[0]];

        (int a, int b)? best = null;
        var bestVar = double.PositiveInfinity;

        for (var i = 0; i < pool.Length; i++)
        for (var j = i + 1; j < pool.Length; j++)
        {
            var a = pool[i];
            var b = pool[j];
            var v = PairVariance(_m.Ret1M, infoT, a, b, lb);
            if (double.IsNaN(v)) continue;
            if (v < bestVar)
            {
                bestVar = v;
                best = (a, b);
            }
        }

        if (best is null)
            return [pool[0]];

        return [best.Value.a, best.Value.b];
    }

    private static double PairVariance(double[,] ret1M, int t, int fa, int fb, int months)
    {
        var start = t - Math.Max(2, months);
        if (start < 1) return double.NaN;

        var valsA = new List<double>();
        var valsB = new List<double>();
        for (var i = start; i <= t; i++)
        {
            var a = ret1M[i, fa];
            var b = ret1M[i, fb];
            if (double.IsNaN(a) || double.IsNaN(b)) continue;
            valsA.Add(a);
            valsB.Add(b);
        }
        if (valsA.Count < 4) return double.NaN;

        var meanA = valsA.Average();
        var meanB = valsB.Average();
        double varA = 0, varB = 0, cov = 0;

        for (var i = 0; i < valsA.Count; i++)
        {
            var da = valsA[i] - meanA;
            var db = valsB[i] - meanB;
            varA += da * da;
            varB += db * db;
            cov += da * db;
        }

        var n = valsA.Count - 1;
        varA /= n;
        varB /= n;
        cov /= n;

        // Equal-weight portfolio variance: 0.25*(varA + varB + 2*cov)
        return 0.25 * (varA + varB + 2.0 * cov);
    }

    private int[] SelectSharpeProxy(TrialParams p, int infoT, int lb)
    {
        // Pick funds with highest mean/stdev of monthly returns over lookback.
        var months = Math.Max(6, lb);
        var F = _m.FundIds.Length;

        var scored = new List<(int f, double s)>(F);
        for (var f = 0; f < F; f++)
        {
            var start = infoT - months;
            if (start < 1) continue;

            var vals = new List<double>();
            for (var i = start; i <= infoT; i++)
            {
                var r = _m.Ret1M[i, f];
                if (!double.IsNaN(r)) vals.Add(r);
            }
            if (vals.Count < Math.Max(4, months / 2)) continue;

            var mean = vals.Average();
            var varSum = vals.Sum(x => (x - mean) * (x - mean));
            var stdev = Math.Sqrt(varSum / (vals.Count - 1));
            if (stdev <= 0) continue;

            scored.Add((f, mean / stdev));
        }

        if (scored.Count == 0) return [];
        scored.Sort((a, b) => b.s.CompareTo(a.s));

        var k = Math.Max(1, Math.Min(p.TopK, scored.Count));
        return scored.Take(k).Select(x => x.f).ToArray();
    }

    private int[] SelectCorrFilteredTop2(TrialParams p, int infoT, int lb, int volLb, int ma)
    {
        // Step 1: pick best momentum fund (with same filters as momentum kind)
        var first = SelectMomentum(p, infoT, lb, volLb, ma);
        if (first.Length == 0) return [];
        if (p.TopK <= 1) return [first[0]];

        var f1 = first[0];

        // Step 2: choose second among top momentum candidates that has lowest correlation to first
        var F = _m.FundIds.Length;
        var arr = new (int f, double mom)[F];
        var count = 0;

        for (var f = 0; f < F; f++)
        {
            if (f == f1) continue;
            var navNow = _m.Nav[infoT, f];
            var navThen = _m.Nav[infoT - lb, f];
            if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;
            var mom = navNow / navThen - 1.0;
            arr[count++] = (f, mom);
        }

        if (count == 0) return [f1];
        Array.Sort(arr, 0, count, Comparer<(int f, double mom)>.Create((a, b) => b.mom.CompareTo(a.mom)));

        var pool = arr.Take(Math.Min(25, count)).Select(x => x.f).ToArray();

        var bestF2 = -1;
        var bestCorr = double.PositiveInfinity;

        foreach (var f2 in pool)
        {
            var corr = Correlation(_m.Ret1M, infoT, f1, f2, Math.Max(6, lb));
            if (double.IsNaN(corr)) continue;
            if (corr < bestCorr)
            {
                bestCorr = corr;
                bestF2 = f2;
            }
        }

        return bestF2 >= 0 ? [f1, bestF2] : [f1];
    }

    private int[] SelectBandReversion(TrialParams p, int infoT, int lb, int ma)
    {
        // Pick funds most below their MA (z-ish), but only if long-term trend is up (trend gate).
        var window = Math.Max(6, lb);
        var maN = Math.Max(6, ma);
        var F = _m.FundIds.Length;

        var scored = new List<(int f, double score)>(F);
        for (var f = 0; f < F; f++)
        {
            var navNow = _m.Nav[infoT, f];
            if (double.IsNaN(navNow)) continue;

            // trend gate: NAV above longer MA
            var trendMa = MovingAverage(_m.Nav, infoT, f, maN);
            if (double.IsNaN(trendMa) || trendMa <= 0) continue;
            if (navNow <= trendMa) continue;

            // short MA band
            var bandMa = MovingAverage(_m.Nav, infoT, f, window);
            if (double.IsNaN(bandMa) || bandMa <= 0) continue;

            var rel = navNow / bandMa - 1.0;
            // mean reversion: prefer most negative rel (furthest below MA)
            scored.Add((f, -rel));
        }

        if (scored.Count == 0) return [];
        scored.Sort((a, b) => b.score.CompareTo(a.score));
        var k = Math.Max(1, Math.Min(p.TopK, scored.Count));
        return scored.Take(k).Select(x => x.f).ToArray();
    }

    private static double Correlation(double[,] ret1M, int t, int fa, int fb, int months)
    {
        var start = t - Math.Max(2, months);
        if (start < 1) return double.NaN;

        var aVals = new List<double>();
        var bVals = new List<double>();
        for (var i = start; i <= t; i++)
        {
            var a = ret1M[i, fa];
            var b = ret1M[i, fb];
            if (double.IsNaN(a) || double.IsNaN(b)) continue;
            aVals.Add(a);
            bVals.Add(b);
        }

        if (aVals.Count < 6) return double.NaN;
        var meanA = aVals.Average();
        var meanB = bVals.Average();

        double varA = 0, varB = 0, cov = 0;
        for (var i = 0; i < aVals.Count; i++)
        {
            var da = aVals[i] - meanA;
            var db = bVals[i] - meanB;
            varA += da * da;
            varB += db * db;
            cov += da * db;
        }

        var n = aVals.Count - 1;
        varA /= n;
        varB /= n;
        cov /= n;

        var denom = Math.Sqrt(varA) * Math.Sqrt(varB);
        if (denom <= 0) return double.NaN;
        return cov / denom;
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


    private static double Rsi(double[] series, int t, int period)
    {
        if (period < 2) return double.NaN;
        if (t - period < 1) return double.NaN;

        double gain = 0.0, loss = 0.0;
        for (var i = t - period + 1; i <= t; i++)
        {
            var a = series[i - 1];
            var b = series[i];
            if (double.IsNaN(a) || double.IsNaN(b)) return double.NaN;
            var d = b - a;
            if (d >= 0) gain += d;
            else loss += -d;
        }

        if (loss == 0) return 100.0;
        var rs = gain / loss;
        return 100.0 - (100.0 / (1.0 + rs));
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

                int[] selected;

                if (!IsRiskOn(p, infoT))
                {
                    selected = p.RiskOffMode == ClawInv.Core.Strategies.RiskOffMode.DefensiveFund
                        ? SelectDefensive(infoT, p.DefensiveVolLookbackMonths)
                        : Array.Empty<int>();
                }
                else
                {
                    selected = p.Kind switch
                    {
                        ResearchStrategyKind.Momentum => SelectMomentum(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.LowVol => SelectLowVol(p, infoT, volLb),
                        ResearchStrategyKind.Trend => SelectTrend(p, infoT, ma),
                        ResearchStrategyKind.MeanReversion => SelectMeanReversion(p, infoT, lb, ma),
                        ResearchStrategyKind.MinVariance2 => SelectMinVariance2(p, infoT, lb),
                        ResearchStrategyKind.SharpeProxy => SelectSharpeProxy(p, infoT, lb),
                        ResearchStrategyKind.CorrFilteredTop2 => SelectCorrFilteredTop2(p, infoT, lb, volLb, ma),
                        ResearchStrategyKind.BandReversion => SelectBandReversion(p, infoT, lb, ma),
                        _ => Array.Empty<int>()
                    };
                }

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
