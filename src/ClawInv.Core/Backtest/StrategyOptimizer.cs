using System.Text.Json;
using ClawInv.Core.Avanza;
using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public sealed class StrategyOptimizer
{
    private readonly AvanzaClient _avanza;
    private readonly NavDataStore _navStore;
    private readonly TimeZoneInfo _tz;

    public StrategyOptimizer(AvanzaClient avanza, NavDataStore navStore, TimeZoneInfo tz)
    {
        _avanza = avanza;
        _navStore = navStore;
        _tz = tz;
    }

    public async Task<IReadOnlyList<NavSeries>> LoadUniverseNavAsync(
        Universe universe,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var list = new List<NavSeries>();

        foreach (var f in universe.Funds)
        {
            if (_navStore.TryRead(f.OrderbookId, out var cached) && cached.Count > 0)
            {
                var cachedFrom = cached[0].Date;
                var cachedTo = cached[^1].Date;

                // If cache fully covers requested range, use it.
                if (cachedFrom <= from && cachedTo >= to)
                {
                    list.Add(new NavSeries(f.Name, f.OrderbookId, cached));
                    continue;
                }

                // Otherwise fetch a window that overlaps the cached data so NavDataStore can rescale+merge.
                var overlapDays = 45;
                var fetchFrom = from;
                var fetchTo = to;

                if (to > cachedTo)
                    fetchFrom = cachedTo.AddDays(-overlapDays);

                if (from < cachedFrom)
                    fetchTo = cachedFrom.AddDays(overlapDays);

                // Clamp
                if (fetchFrom < from) fetchFrom = from;
                if (fetchTo > to) fetchTo = to;

                var chart = await _avanza.GetFundChartAsync(f.OrderbookId, fetchFrom, fetchTo, ct);
                var nav = AvanzaChartConverter.ToNormalizedNav(chart, _tz);
                _navStore.Write(f.OrderbookId, nav);

                // Read back merged series for use.
                _navStore.TryRead(f.OrderbookId, out var merged);
                list.Add(new NavSeries(f.Name, f.OrderbookId, merged ?? nav));
                continue;
            }

            var chart2 = await _avanza.GetFundChartAsync(f.OrderbookId, from, to, ct);
            var nav2 = AvanzaChartConverter.ToNormalizedNav(chart2, _tz);
            _navStore.Write(f.OrderbookId, nav2);
            list.Add(new NavSeries(f.Name, f.OrderbookId, nav2));
        }

        return list;
    }

    public IReadOnlyList<StrategyDefinition> GenerateGrid(int maxStrategiesPerType = 1000)
    {
        var defs = new List<StrategyDefinition>();

        // 1) Momentum rotation + optional absolute filter
        {
            var lookbacks = Enumerable.Range(1, 18).ToArray();
            var topKs = new[] { 1, 2, 3, 5 };
            var rebals = new[] { 1, 2, 3 };
            var allocs = new[] { AllocationMode.Top1, AllocationMode.EqualWeightTopK };
            var absFilters = new[] { false, true };

            var count = 0;
            foreach (var lb in lookbacks)
            foreach (var k in topKs)
            foreach (var rm in rebals)
            foreach (var a in allocs)
            foreach (var abs in absFilters)
            {
                var allocName = a == AllocationMode.Top1 ? "top1" : $"eq{k}";
                var absName = abs ? "abs" : "rel";

                var id = $"mom_{absName}_lb{lb}_rm{rm}_{allocName}";
                var name = $"Momentum ({absName}) LB={lb}m Rebal={rm}m Alloc={allocName}";

                defs.Add(new StrategyDefinition(
                    Id: id,
                    Name: name,
                    Type: StrategyType.MomentumRotation,
                    RebalanceEveryMonths: rm,
                    LookbackMonths: lb,
                    TopK: k,
                    Allocation: a,
                    UseAbsoluteMomentumFilter: abs,
                    MovingAverageMonths: 0,
                    VolatilityLookbackMonths: 0,
                    UseLowVolFilter: false,
                    Regime: ClawInv.Core.Research.RegimeKind.None,
                    RegimeMaMonths: 1,
                    RegimeThreshold: 0.0,
                    RiskOffMode: RiskOffMode.Cash,
                    DefensiveVolLookbackMonths: 6));

                if (++count >= maxStrategiesPerType)
                    break;
            }
        }

        // 2) Trend following
        {
            var maMonths = new[] { 6, 9, 12, 18 };
            var topKs = new[] { 1, 2, 3, 5 };
            var rebals = new[] { 1, 2, 3 };

            var count = 0;
            foreach (var ma in maMonths)
            foreach (var k in topKs)
            foreach (var rm in rebals)
            {
                var id = $"trend_ma{ma}_rm{rm}_k{k}";
                var name = $"Trend MA={ma}m Rebal={rm}m TopK={k}";

                defs.Add(new StrategyDefinition(
                    Id: id,
                    Name: name,
                    Type: StrategyType.TrendFollowing,
                    RebalanceEveryMonths: rm,
                    LookbackMonths: 0,
                    TopK: k,
                    Allocation: AllocationMode.EqualWeightTopK,
                    UseAbsoluteMomentumFilter: false,
                    MovingAverageMonths: ma,
                    VolatilityLookbackMonths: 0,
                    UseLowVolFilter: false,
                    Regime: ClawInv.Core.Research.RegimeKind.None,
                    RegimeMaMonths: 1,
                    RegimeThreshold: 0.0,
                    RiskOffMode: RiskOffMode.Cash,
                    DefensiveVolLookbackMonths: 6));

                if (++count >= maxStrategiesPerType)
                    break;
            }
        }

        // 3) Low volatility selection
        {
            var volLookback = new[] { 3, 6, 12 };
            var topKs = new[] { 1, 2, 3, 5 };
            var rebals = new[] { 1, 2, 3 };

            var count = 0;
            foreach (var vlb in volLookback)
            foreach (var k in topKs)
            foreach (var rm in rebals)
            {
                var id = $"lowvol_lb{vlb}_rm{rm}_k{k}";
                var name = $"LowVol LB={vlb}m Rebal={rm}m TopK={k}";

                defs.Add(new StrategyDefinition(
                    Id: id,
                    Name: name,
                    Type: StrategyType.LowVolatilitySelection,
                    RebalanceEveryMonths: rm,
                    LookbackMonths: 0,
                    TopK: k,
                    Allocation: AllocationMode.EqualWeightTopK,
                    UseAbsoluteMomentumFilter: false,
                    MovingAverageMonths: 0,
                    VolatilityLookbackMonths: vlb,
                    UseLowVolFilter: false,
                    Regime: ClawInv.Core.Research.RegimeKind.None,
                    RegimeMaMonths: 1,
                    RegimeThreshold: 0.0,
                    RiskOffMode: RiskOffMode.Cash,
                    DefensiveVolLookbackMonths: 6));

                if (++count >= maxStrategiesPerType)
                    break;
            }
        }

        // 4) Equal-weight buy & hold baseline
        {
            defs.Add(new StrategyDefinition(
                Id: "ew_buyhold_rm1",
                Name: "EqualWeight buy&hold (monthly rebalance)",
                Type: StrategyType.EqualWeightBuyAndHold,
                RebalanceEveryMonths: 1,
                LookbackMonths: 0,
                TopK: int.MaxValue,
                Allocation: AllocationMode.EqualWeightTopK,
                UseAbsoluteMomentumFilter: false,
                MovingAverageMonths: 0,
                VolatilityLookbackMonths: 0,
                UseLowVolFilter: false,
                Regime: ClawInv.Core.Research.RegimeKind.None,
                RegimeMaMonths: 1,
                RegimeThreshold: 0.0,
                RiskOffMode: RiskOffMode.Cash,
                DefensiveVolLookbackMonths: 6));
        }

        // 5) BestStrategyV1 (explicit)
        {
            defs.Add(BestStrategies.BestStrategyV1);
        }

        return defs;
    }

    public IReadOnlyList<StrategyResult> RankTop(
        IReadOnlyList<StrategyDefinition> strategies,
        IReadOnlyList<NavSeries> nav,
        DateOnly from,
        DateOnly to,
        int keepTop = 100)
    {
        var scored = new List<StrategyResult>(strategies.Count);

        foreach (var s in strategies)
        {
            var (res, _) = s.Type switch
            {
                StrategyType.MomentumRotation => MomentumRotationBacktester.Run(s, nav, from, to),
                StrategyType.TrendFollowing => TrendFollowingBacktester.Run(s, nav, from, to),
                StrategyType.LowVolatilitySelection => LowVolBacktester.Run(s, nav, from, to),
                StrategyType.EqualWeightBuyAndHold => EqualWeightBuyHoldBacktester.Run(s, nav, from, to),
                StrategyType.BestStrategyV1MonthEnd => (MonthEndBestV1Backtester.Run(s, nav, from, to), Array.Empty<PortfolioPoint>()),
                _ => MomentumRotationBacktester.Run(s, nav, from, to),
            };

            scored.Add(new StrategyResult(s, res));
        }

        return scored
            .OrderByDescending(x => x.Result.Sharpe ?? decimal.MinValue)
            .ThenByDescending(x => x.Result.Cagr)
            .Take(keepTop)
            .ToList();
    }

    public IReadOnlyDictionary<StrategyType, StrategyResult> BestPerType(
        IReadOnlyList<StrategyDefinition> strategies,
        IReadOnlyList<NavSeries> nav,
        DateOnly from,
        DateOnly to)
    {
        return strategies
            .GroupBy(s => s.Type)
            .ToDictionary(
                g => g.Key,
                g => RankTop(g.ToList(), nav, from, to, keepTop: 1).Single());
    }

    public void WriteTopStrategies(string outDir, IReadOnlyList<StrategyResult> top)
    {
        Directory.CreateDirectory(outDir);

        foreach (var s in top)
        {
            var path = Path.Combine(outDir, $"{s.Strategy.Id}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }

        var indexPath = Path.Combine(outDir, "top100.json");
        File.WriteAllText(indexPath, JsonSerializer.Serialize(top, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void WriteBestPerType(string outDir, IReadOnlyDictionary<StrategyType, StrategyResult> best)
    {
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "best_per_type.json");
        File.WriteAllText(path, JsonSerializer.Serialize(best, new JsonSerializerOptions { WriteIndented = true }));
    }
}
