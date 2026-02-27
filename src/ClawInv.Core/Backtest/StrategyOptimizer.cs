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
            if (_navStore.TryRead(f.OrderbookId, out var cached))
            {
                list.Add(new NavSeries(f.Name, f.OrderbookId, cached));
                continue;
            }

            var chart = await _avanza.GetFundChartAsync(f.OrderbookId, from, to, ct);
            var nav = AvanzaChartConverter.ToNormalizedNav(chart, _tz);
            _navStore.Write(f.OrderbookId, nav);
            list.Add(new NavSeries(f.Name, f.OrderbookId, nav));
        }

        return list;
    }

    public IReadOnlyList<StrategyDefinition> GenerateGrid(int maxStrategies = 1000)
    {
        var defs = new List<StrategyDefinition>();

        var lookbacks = Enumerable.Range(1, 18).ToArray();          // 1..18 months
        var topKs = new[] { 1, 2, 3, 5 };
        var rebals = new[] { 1, 2, 3 };
        var allocs = new[] { AllocationMode.Top1, AllocationMode.EqualWeightTopK };

        foreach (var lb in lookbacks)
        foreach (var k in topKs)
        foreach (var rm in rebals)
        foreach (var a in allocs)
        {
            var allocName = a == AllocationMode.Top1 ? "top1" : $"eq{k}";
            var id = $"mom_lb{lb}_rm{rm}_{allocName}";
            var name = $"Momentum rotation (LB={lb}m, Rebal={rm}m, Alloc={allocName})";

            defs.Add(new StrategyDefinition(
                Id: id,
                Name: name,
                Type: "momentum_rotation",
                LookbackMonths: lb,
                TopK: k,
                RebalanceEveryMonths: rm,
                Allocation: a));

            if (defs.Count >= maxStrategies)
                return defs;
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
            var (res, _) = MomentumRotationBacktester.Run(s, nav, from, to);
            scored.Add(new StrategyResult(s, res));
        }

        var top = scored
            .OrderByDescending(x => x.Result.Sharpe ?? decimal.MinValue)
            .ThenByDescending(x => x.Result.Cagr)
            .Take(keepTop)
            .ToList();

        return top;
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
}
