using System.Text.Json;
using System.Text.Json.Serialization;
using ClawInv.Core.Backtest;
using ClawInv.Core.Research;
using ClawInv.Core.Strategies;

namespace ClawInv.Cli;

public static class BacktestStrategy
{
    public static int Run(BacktestStrategyCommand.Settings s)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOpts.Converters.Add(new JsonStringEnumConverter());

        var universe = JsonSerializer.Deserialize<Universe>(File.ReadAllText(s.UniversePath), jsonOpts);
        if (universe is null) throw new ArgumentException($"Could not read universe: {s.UniversePath}");

        var to = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var from = to.AddYears(-s.Years);

        var nav = LoadNav(universe, from, to);

        var type = Enum.Parse<StrategyType>(s.Type, ignoreCase: true);
        var regime = Enum.Parse<RegimeKind>(s.Regime, ignoreCase: true);
        var riskOff = Enum.Parse<RiskOffMode>(s.RiskOffMode, ignoreCase: true);

        var def = new StrategyDefinition(
            Id: "CLI",
            Name: "CLI",
            Type: type,
            RebalanceEveryMonths: Math.Max(1, s.RebalanceEveryMonths),
            LookbackMonths: Math.Max(1, s.LookbackMonths),
            TopK: Math.Max(1, s.Slots),
            Allocation: AllocationMode.EqualWeightTopK,
            UseAbsoluteMomentumFilter: s.UseAbsoluteMomentum,
            MovingAverageMonths: Math.Max(0, s.MovingAverageMonths),
            VolatilityLookbackMonths: Math.Max(2, s.VolatilityLookbackMonths),
            UseLowVolFilter: false,
            Regime: regime,
            RegimeMaMonths: Math.Max(1, s.RegimeMaMonths),
            RegimeThreshold: s.RegimeThreshold,
            RiskOffMode: riskOff,
            DefensiveVolLookbackMonths: Math.Max(1, s.DefensiveVolLookbackMonths)
        );

        var r = MonthEndRebalanceDailyBacktester.Run(def, nav, from, to);
        var finalValue = s.StartCapital * (1.0m + r.TotalReturn);
        var finalEquityX = 1.0m + r.TotalReturn;

        Console.WriteLine($"Final value: {finalValue:0.##}");
        Console.WriteLine($"Final equity: {finalEquityX:0.###}x  CAGR: {r.Cagr:P2}  MDD: {r.MaxDrawdown:P2}  Sharpe: {(r.Sharpe?.ToString("0.##") ?? "n/a")}");
        return 0;
    }

    private static IReadOnlyList<NavSeries> LoadNav(Universe universe, DateOnly from, DateOnly to)
    {
        var cacheDir = Path.Combine(Environment.CurrentDirectory, ".cache", "avanza");
        Directory.CreateDirectory(cacheDir);
        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(cacheDir);
        var navStore = new NavDataStore(Path.Combine(Environment.CurrentDirectory, ".cache", "nav"));
        using var http = new HttpClient();
        var avanza = new ClawInv.Core.Avanza.AvanzaClient(http, cache);
        var tz = ClawInv.Core.Avanza.AvanzaChartConverter.GetStockholmTz();
        var opt = new StrategyOptimizer(avanza, navStore, tz);
        return opt.LoadUniverseNavAsync(universe, from, to).GetAwaiter().GetResult();
    }
}
