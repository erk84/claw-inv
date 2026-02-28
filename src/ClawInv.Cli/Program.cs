using ClawInv.Core;
using ClawInv.Core.Avanza;
using Spectre.Console;
using Spectre.Console.Cli;
using ClawInv.Cli;

var app = new CommandApp();
app.Configure(cfg =>
{
    cfg.SetApplicationName("claw-inv");

    cfg.AddCommand<SearchCommand>("search")
        .WithDescription("Search funds by name on Avanza (public endpoint)." );

    cfg.AddCommand<MetricsCommand>("metrics")
        .WithDescription("Compute metrics from NAV CSV (Date,NAV)." );

    cfg.AddCommand<BacktestCommand>("backtest")
        .WithDescription("Backtest buy & hold using NAV CSV (Date,NAV)." );

    cfg.AddCommand<DownloadCommand>("download")
        .WithDescription("Download fund history from Avanza and cache it (outputs CSV)." );

    cfg.AddCommand<OptimizeCommand>("optimize")
        .WithDescription("Run strategy search over a fund universe and write top strategies to disk." );

    cfg.AddCommand<SearchBestCommand>("search-best")
        .WithDescription("Run 100k trial search to find a stable best strategy (research mode)." );

    cfg.AddCommand<SearchFamiliesCommand>("search-families")
        .WithDescription("Run trial search for multiple strategy families in one run (shared NAV/matrices)." );

    cfg.AddCommand<ClawInv.Cli.TraceCommand>("trace")
        .WithDescription("Trace month-end transactions for a best-strategy JSON (outputs CSV)." );

    cfg.AddCommand<GenUniverseCommand>("gen-universe")
        .WithDescription("Generate a universe file by sampling Avanza search results (rate limited)." );
});

return await app.RunAsync(args);

sealed class SearchCommand : AsyncCommand<SearchCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<query>")]
        public string Query { get; init; } = "";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        using var http = new HttpClient();
        var client = new AvanzaClient(http);
        var hits = await client.SearchFundsAsync(settings.Query);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Name");
        table.AddColumn("ISIN");
        table.AddColumn("OrderbookId");
        table.AddColumn("3y dev");

        foreach (var h in hits.Take(20))
        {
            table.AddRow(
                h.Name,
                h.Isin,
                h.OrderbookId,
                h.DevelopmentThreeYears?.ToString("0.##") ?? ""
            );
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

sealed class MetricsCommand : Command<MetricsCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--csv <PATH>")]
        public string CsvPath { get; init; } = "";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        var nav = CsvNavReader.Read(settings.CsvPath);
        var m = MetricsCalculator.Compute(nav);

        AnsiConsole.MarkupLine($"Start: [grey]{m.Start}[/]");
        AnsiConsole.MarkupLine($"End:   [grey]{m.End}[/]");
        AnsiConsole.MarkupLine($"Days:  [grey]{m.Days}[/]");
        AnsiConsole.MarkupLine($"CAGR:  [green]{m.Cagr:P2}[/]");
        AnsiConsole.MarkupLine($"Vol:   [yellow]{m.Volatility:P2}[/]");
        AnsiConsole.MarkupLine($"Sharpe:[yellow]{(m.Sharpe?.ToString("0.##") ?? "n/a")}[/]");
        AnsiConsole.MarkupLine($"MDD:   [red]{m.MaxDrawdown:P2}[/]");

        return 0;
    }
}

sealed class BacktestCommand : Command<BacktestCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--csv <PATH>")]
        public string CsvPath { get; init; } = "";

        [CommandOption("--initial-capital <AMOUNT>")]
        public decimal InitialCapital { get; init; } = 100_000m;
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        // For a single fund NAV series, buy&hold is the same as NAV tracking.
        var nav = CsvNavReader.Read(settings.CsvPath);
        var m = MetricsCalculator.Compute(nav);

        AnsiConsole.MarkupLine("[bold]Buy & hold[/]");
        AnsiConsole.MarkupLine($"Initial: [grey]{settings.InitialCapital:N0}[/]");
        AnsiConsole.MarkupLine($"CAGR:    [green]{m.Cagr:P2}[/]");
        AnsiConsole.MarkupLine($"MDD:     [red]{m.MaxDrawdown:P2}[/]");

        return 0;
    }
}

sealed class DownloadCommand : AsyncCommand<DownloadCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--orderbook <ID>")]
        public string OrderbookId { get; init; } = "";

        [CommandOption("--from <YYYY-MM-DD>")]
        public string From { get; init; } = "";

        [CommandOption("--to <YYYY-MM-DD>")]
        public string To { get; init; } = "";

        [CommandOption("--out <PATH>")]
        public string OutPath { get; init; } = "fund.csv";

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        if (!DateOnly.TryParse(settings.From, out var from))
            throw new ArgumentException("Invalid --from. Expected YYYY-MM-DD.");
        if (!DateOnly.TryParse(settings.To, out var to))
            throw new ArgumentException("Invalid --to. Expected YYYY-MM-DD.");

        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(settings.CacheDir);

        using var http = new HttpClient();
        var client = new AvanzaClient(http, cache);
        var chart = await client.GetFundChartAsync(settings.OrderbookId, from, to);

        var tz = ClawInv.Core.Avanza.AvanzaChartConverter.GetStockholmTz();
        var nav = ClawInv.Core.Avanza.AvanzaChartConverter.ToNormalizedNav(chart, tz);

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutPath) ?? ".");

        await using var writer = new StreamWriter(settings.OutPath);
        await writer.WriteLineAsync("Date,NAV");
        foreach (var p in nav)
            await writer.WriteLineAsync($"{p.Date:yyyy-MM-dd},{p.Nav}");

        AnsiConsole.MarkupLine($"Wrote [green]{nav.Count}[/] rows to [grey]{settings.OutPath}[/]");
        return 0;
    }
}

sealed class OptimizeCommand : AsyncCommand<OptimizeCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--universe <PATH>")]
        public string UniversePath { get; init; } = "data/universe.json";

        [CommandOption("--from <YYYY-MM-DD>")]
        public string From { get; init; } = "";

        [CommandOption("--to <YYYY-MM-DD>")]
        public string To { get; init; } = "";

        [CommandOption("--keep <N>")]
        public int Keep { get; init; } = 100;

        [CommandOption("--out-dir <DIR>")]
        public string OutDir { get; init; } = "strategies/top";

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";

        [CommandOption("--nav-store <DIR>")]
        public string NavStoreDir { get; init; } = ".cache/nav";

        [CommandOption("--years <N>")]
        public int Years { get; init; } = 10;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var from = !string.IsNullOrWhiteSpace(settings.From) && DateOnly.TryParse(settings.From, out var f)
            ? f
            : today.AddYears(-settings.Years);

        var to = !string.IsNullOrWhiteSpace(settings.To) && DateOnly.TryParse(settings.To, out var t)
            ? t
            : today;

        var universe = ClawInv.Core.Backtest.UniverseLoader.Load(settings.UniversePath);

        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(settings.CacheDir);
        var navStore = new ClawInv.Core.Backtest.NavDataStore(settings.NavStoreDir);

        using var http = new HttpClient();
        var avanza = new AvanzaClient(http, cache);

        var tz = ClawInv.Core.Avanza.AvanzaChartConverter.GetStockholmTz();
        var opt = new ClawInv.Core.Backtest.StrategyOptimizer(avanza, navStore, tz);

        AnsiConsole.MarkupLine($"Loading NAV for [green]{universe.Funds.Count}[/] funds ({from}..{to})...");
        var nav = await opt.LoadUniverseNavAsync(universe, from, to);

        var grid = opt.GenerateGrid();
        AnsiConsole.MarkupLine($"Generated [green]{grid.Count}[/] strategies (multiple types). Running backtests...");

        var top = opt.RankTop(grid, nav, from, to, settings.Keep);
        opt.WriteTopStrategies(settings.OutDir, top);

        var bestPerType = opt.BestPerType(grid, nav, from, to);
        opt.WriteBestPerType(settings.OutDir, bestPerType);

        ClawInv.Core.Backtest.ReportWriter.WriteMarkdown(settings.OutDir, universe, from, to, top, bestPerType);
        ClawInv.Core.Backtest.ReportWriter.WriteJson(settings.OutDir, universe, from, to, top, bestPerType);

        AnsiConsole.MarkupLine($"Wrote top [green]{top.Count}[/] strategies to [grey]{settings.OutDir}[/]");
        AnsiConsole.MarkupLine($"Wrote report: [grey]{settings.OutDir}/report.md[/]");

        foreach (var kv in bestPerType.OrderBy(k => k.Key.ToString()))
        {
            var best = kv.Value;
            AnsiConsole.MarkupLine($"[bold]{kv.Key}[/]: {best.Strategy.Name}");
            var sharpeTxt = best.Result.Sharpe?.ToString("0.##") ?? "n/a";
            AnsiConsole.MarkupLine($"  Sharpe: [yellow]{sharpeTxt}[/]  CAGR: [green]{best.Result.Cagr:P2}[/]  MDD: [red]{best.Result.MaxDrawdown:P2}[/]");
        }

        return 0;
    }
}

sealed class GenUniverseCommand : AsyncCommand<GenUniverseCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--target <N>")]
        public int Target { get; init; } = 100;

        [CommandOption("--rating-limit <N>")]
        public int RatingLimit { get; init; } = 3;

        [CommandOption("--total-fee-limit <N>")]
        public double TotalFeeLimit { get; init; } = 2.5;

        [CommandOption("--risk-limit <N>")]
        public int RiskLimit { get; init; } = 3;

        [CommandOption("--out <PATH>")]
        public string OutPath { get; init; } = "data/universe.generated.json";

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(settings.CacheDir);
        using var http = new HttpClient();
        var avanza = new AvanzaClient(http, cache);

        var gen = new ClawInv.Core.Backtest.UniverseGenerator(avanza);
        AnsiConsole.MarkupLine(
            $"Generating universe from Avanza fund list: target [green]{settings.Target}[/] funds " +
            $"(rating>={settings.RatingLimit}, totalFee<={settings.TotalFeeLimit}, risk>={settings.RiskLimit})...");

        var u = await gen.GenerateFromFundListAsync(
            settings.Target,
            settings.RatingLimit,
            settings.TotalFeeLimit,
            settings.RiskLimit);

        ClawInv.Core.Backtest.UniverseWriter.Save(u, settings.OutPath);

        AnsiConsole.MarkupLine($"Wrote [green]{u.Funds.Count}[/] funds to [grey]{settings.OutPath}[/]");
        return 0;
    }
}

sealed class SearchBestCommand : AsyncCommand<SearchBestCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--universe <PATH>")]
        public string UniversePath { get; init; } = "out/universe.json";

        [CommandOption("--years <N>")]
        public int Years { get; init; } = 10;

        [CommandOption("--trials <N>")]
        public int Trials { get; init; } = 100_000;

        [CommandOption("--kind <KIND>")]
        public string Kind { get; init; } = "";

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";

        [CommandOption("--nav-store <DIR>")]
        public string NavStoreDir { get; init; } = ".cache/nav";

        [CommandOption("--out <PATH>")]
        public string OutPath { get; init; } = "out/best_strategy.json";

        [CommandOption("--objective <OBJ>")]
        public string Objective { get; init; } = "Sharpe"; // Sharpe|Cagr|Final

        [CommandOption("--start-capital <N>")]
        public decimal StartCapital { get; init; } = 100000m;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = today.AddYears(-settings.Years);
        var to = today;

        var universe = ClawInv.Core.Backtest.UniverseLoader.Load(settings.UniversePath);

        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(settings.CacheDir);
        var navStore = new ClawInv.Core.Backtest.NavDataStore(settings.NavStoreDir);
        using var http = new HttpClient();
        var avanza = new ClawInv.Core.Avanza.AvanzaClient(http, cache);
        var tz = ClawInv.Core.Avanza.AvanzaChartConverter.GetStockholmTz();

        var opt = new ClawInv.Core.Backtest.StrategyOptimizer(avanza, navStore, tz);
        AnsiConsole.MarkupLine($"Loading NAV for [green]{universe.Funds.Count}[/] funds ({from}..{to})...");
        var nav = await opt.LoadUniverseNavAsync(universe, from, to);

        var matrices = ClawInv.Core.Research.FeatureBuilder.BuildMonthEndMatrices(nav);
        var search = new ClawInv.Core.Research.StrategySearch(matrices);

        var rnd = new Random(123);

        var obj = settings.Objective?.Trim().ToLowerInvariant() ?? "sharpe";
        if (obj is not ("sharpe" or "cagr" or "final"))
            throw new ArgumentException("Unknown --objective. Use: Sharpe|Cagr|Final");

        double ObjectiveScore(ClawInv.Core.Research.TrialResult r)
        {
            return obj switch
            {
                "cagr" => r.Cagr,
                // Maximize final capital over the backtest horizon.
                // Use log final multiple as score to keep numbers stable and monotonic.
                "final" => settings.Years * Math.Log(1.0 + r.Cagr),
                _ => r.Score
            };
        }

        decimal FinalCapital(ClawInv.Core.Research.TrialResult r)
        {
            var mult = Math.Pow(1.0 + r.Cagr, settings.Years);
            return settings.StartCapital * (decimal)mult;
        }

        ClawInv.Core.Research.ResearchStrategyKind? fixedKind = null;
        if (!string.IsNullOrWhiteSpace(settings.Kind))
        {
            if (Enum.TryParse<ClawInv.Core.Research.ResearchStrategyKind>(settings.Kind, ignoreCase: true, out var k))
                fixedKind = k;
            else
                throw new ArgumentException($"Unknown --kind: {settings.Kind}");
        }

        ClawInv.Core.Research.TrialResult? bestMonthEnd = null;

        const int KeepTop = 200;
        var top = new List<ClawInv.Core.Research.TrialResult>(capacity: KeepTop + 50);
        // Track best per regime (so gating variants are visible even if they do not win overall).
        var bestByRegime = new Dictionary<ClawInv.Core.Research.RegimeKind, ClawInv.Core.Research.TrialResult>();


        void Consider(ClawInv.Core.Research.TrialResult r)
        {
            top.Add(r);
            if (top.Count <= KeepTop) return;

            // Remove worst (small KeepTop, so O(N) is fine)
            var minIdx = 0;
            var minScore = top[0].Score;
            for (var j = 1; j < top.Count; j++)
            {
                if (top[j].Score < minScore)
                {
                    minScore = top[j].Score;
                    minIdx = j;
                }
            }
            top.RemoveAt(minIdx);
        }

        for (var i = 1; i <= settings.Trials; i++)
        {
            var p0 = Sample(rnd);
            var p = fixedKind is null ? p0 : p0 with { Kind = fixedKind.Value };
            var r0 = search.Evaluate(p);

            if (double.IsNaN(r0.Score) || double.IsNaN(r0.Cagr))
                continue;

            var r = r0 with { Score = ObjectiveScore(r0) };

            if (bestMonthEnd is null || r.Score > bestMonthEnd.Score)
                bestMonthEnd = r;
            if (!bestByRegime.TryGetValue(r.Params.Regime, out var cur) || r.Score > cur.Score)
                bestByRegime[r.Params.Regime] = r;

            Consider(r);

            if (i % 10_000 == 0 && bestMonthEnd is not null)
            {
                var final = FinalCapital(bestMonthEnd);
                AnsiConsole.MarkupLine($"T={i:N0}: best(month-end) obj={settings.Objective} score={bestMonthEnd.Score:0.000} final={final:N0} sharpe={bestMonthEnd.Sharpe:0.##} cagr={bestMonthEnd.Cagr:P2} mdd={bestMonthEnd.MaxDrawdown:P2} kind={bestMonthEnd.Params.Kind} lookback={bestMonthEnd.Params.LookbackMonths} reb={bestMonthEnd.Params.RebalanceMonths} topK={bestMonthEnd.Params.TopK} abs={bestMonthEnd.Params.UseAbsoluteMomentum} ma={bestMonthEnd.Params.TrendMaMonths} volLb={bestMonthEnd.Params.VolLookbackMonths} regime={bestMonthEnd.Params.Regime} regMA={bestMonthEnd.Params.RegimeMaMonths} breadthTh={bestMonthEnd.Params.RegimeBreadthThreshold:0.##} ddLambda={bestMonthEnd.Params.MaxDrawdownPenaltyLambda:0.##}" );
            }
        }

        if (bestMonthEnd is null)
        {
            AnsiConsole.MarkupLine("No valid trials.");
            return 1;
        }

        if (bestByRegime.Count > 0)
        {
            foreach (var kv in bestByRegime.OrderBy(k => k.Key.ToString()))
            {
                var r = kv.Value;
                AnsiConsole.WriteLine($"Best[{kv.Key}] score={r.Score:0.000} sharpe={r.Sharpe:0.##} cagr={r.Cagr:P2} mdd={r.MaxDrawdown:P2} kind={r.Params.Kind}");
            }
        }

        // Choose best candidate after DAILY validation under the same MDD floor.
        var ordered = top.OrderByDescending(x => x.Score).ToList();
        ClawInv.Core.Research.TrialResult? bestDailyOk = null;
        ClawInv.Core.Backtest.BacktestResult? bestDailyRes = null;

        foreach (var cand in ordered)
        {
            var daily = ValidateDaily(cand, nav, from, to);
            // No hard MDD cutoff anymore; we just select by best month-end score and always report daily.
            bestDailyOk = cand;
            bestDailyRes = daily;
            break;
        }

        var chosen = bestDailyOk ?? bestMonthEnd;

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutPath) ?? ".");
        File.WriteAllText(settings.OutPath, System.Text.Json.JsonSerializer.Serialize(chosen, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        AnsiConsole.MarkupLine($"Wrote chosen strategy to [grey]{settings.OutPath}[/]");

        var chosenDaily = bestDailyRes ?? ValidateDaily(chosen, nav, from, to);
        AnsiConsole.MarkupLine($"Daily validation (chosen) => Sharpe: [yellow]{(chosenDaily.Sharpe?.ToString("0.##") ?? "n/a")}[/] CAGR: [green]{chosenDaily.Cagr:P2}[/] MDD: [red]{chosenDaily.MaxDrawdown:P2}[/]  ({chosenDaily.Notes})");


        return 0;
    }

    internal static ClawInv.Core.Backtest.BacktestResult ValidateDaily(
        ClawInv.Core.Research.TrialResult best,
        IReadOnlyList<ClawInv.Core.Backtest.NavSeries> nav,
        DateOnly from,
        DateOnly to)
    {
        var p = best.Params;

        if (p.Kind == ClawInv.Core.Research.ResearchStrategyKind.LowVol)
        {
            return ClawInv.Core.Backtest.LowVolBacktester.Run(
                new ClawInv.Core.Strategies.StrategyDefinition(
                    Id: "trial_lowvol",
                    Name: "Trial LowVol",
                    Type: ClawInv.Core.Strategies.StrategyType.LowVolatilitySelection,
                    RebalanceEveryMonths: p.RebalanceMonths,
                    LookbackMonths: 0,
                    TopK: Math.Min(2, p.TopK),
                    Allocation: ClawInv.Core.Strategies.AllocationMode.EqualWeightTopK,
                    UseAbsoluteMomentumFilter: false,
                    MovingAverageMonths: 0,
                    VolatilityLookbackMonths: p.VolLookbackMonths,
                    UseLowVolFilter: false,
                    Regime: p.Regime,
                    RegimeMaMonths: p.RegimeMaMonths,
                    RegimeThreshold: p.RegimeBreadthThreshold,
                    RiskOffMode: p.RiskOffMode,
                    DefensiveVolLookbackMonths: p.DefensiveVolLookbackMonths),
                nav, from, to).result;
        }

        if (p.Kind == ClawInv.Core.Research.ResearchStrategyKind.Trend)
        {
            return ClawInv.Core.Backtest.TrendFollowingBacktester.Run(
                new ClawInv.Core.Strategies.StrategyDefinition(
                    Id: "trial_trend",
                    Name: "Trial Trend",
                    Type: ClawInv.Core.Strategies.StrategyType.TrendFollowing,
                    RebalanceEveryMonths: p.RebalanceMonths,
                    LookbackMonths: 0,
                    TopK: Math.Min(2, p.TopK),
                    Allocation: ClawInv.Core.Strategies.AllocationMode.EqualWeightTopK,
                    UseAbsoluteMomentumFilter: false,
                    MovingAverageMonths: p.TrendMaMonths,
                    VolatilityLookbackMonths: 0,
                    UseLowVolFilter: false,
                    Regime: p.Regime,
                    RegimeMaMonths: p.RegimeMaMonths,
                    RegimeThreshold: p.RegimeBreadthThreshold,
                    RiskOffMode: p.RiskOffMode,
                    DefensiveVolLookbackMonths: p.DefensiveVolLookbackMonths),
                nav, from, to).result;
        }

        // Month-end rebalance but simulate daily returns in-between (daily validation)
        var type = p.Kind switch
        {
            ClawInv.Core.Research.ResearchStrategyKind.LowVol => ClawInv.Core.Strategies.StrategyType.LowVolatilitySelection,
            ClawInv.Core.Research.ResearchStrategyKind.Trend => ClawInv.Core.Strategies.StrategyType.TrendFollowing,
            ClawInv.Core.Research.ResearchStrategyKind.MeanReversion => ClawInv.Core.Strategies.StrategyType.MeanReversionRotation,
            ClawInv.Core.Research.ResearchStrategyKind.MinVariance2 => ClawInv.Core.Strategies.StrategyType.MinVariance2,
            _ => ClawInv.Core.Strategies.StrategyType.MomentumRotation
        };

        var def = new ClawInv.Core.Strategies.StrategyDefinition(
            Id: "trial",
            Name: $"Trial {p.Kind}",
            Type: type,
            RebalanceEveryMonths: p.RebalanceMonths,
            LookbackMonths: p.LookbackMonths,
            TopK: Math.Min(2, p.TopK),
            Allocation: ClawInv.Core.Strategies.AllocationMode.EqualWeightTopK,
            UseAbsoluteMomentumFilter: p.UseAbsoluteMomentum,
            MovingAverageMonths: p.TrendMaMonths,
            VolatilityLookbackMonths: p.VolLookbackMonths,
            UseLowVolFilter: true,
            Regime: p.Regime,
            RegimeMaMonths: p.RegimeMaMonths,
            RegimeThreshold: p.RegimeBreadthThreshold,
            RiskOffMode: p.RiskOffMode,
            DefensiveVolLookbackMonths: p.DefensiveVolLookbackMonths);

        return ClawInv.Core.Backtest.MonthEndRebalanceDailyBacktester.Run(def, nav, from, to, maxDrawdownFloor: -1.0m);

    }

    internal static ClawInv.Core.Research.TrialParams Sample(Random rnd)
    {
        int Pick(params int[] xs) => xs[rnd.Next(xs.Length)];
        bool Flip(double p) => rnd.NextDouble() < p;

        // Broaden away from pure momentum while still keeping it well-sampled.
        var kindRoll = rnd.NextDouble();
        var kind = kindRoll < 0.45
            ? ClawInv.Core.Research.ResearchStrategyKind.Momentum
            : kindRoll < 0.60
                ? ClawInv.Core.Research.ResearchStrategyKind.MinVariance2
                : (ClawInv.Core.Research.ResearchStrategyKind)rnd.Next(0, 8);

        var lookback = Pick(1, 2, 3, 4, 6, 9, 12);
        var reb = Pick(1, 2, 3);
        var topK = Pick(1, 2);

        var abs = kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum && Flip(0.75);
        var useLowVol = kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum && Flip(0.50);

        var volLb = (kind == ClawInv.Core.Research.ResearchStrategyKind.LowVol || kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum)
            ? Pick(3, 6, 12)
            : 0;

        // TrendMaMonths doubles as a trend-gate for Momentum kind (if >=2)
        var ma = kind == ClawInv.Core.Research.ResearchStrategyKind.Trend
            ? Pick(6, 9, 12, 18)
            : (kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum && Flip(0.70)
                ? Pick(6, 9, 12, 18)
                : 0);

        // Regime filter (soft): None / IndexTrend / Breadth / Index RSI
        // For MinVariance2 we want to actively test regime gating + defensive mode.
        var rr = rnd.NextDouble();
        var regime = kind == ClawInv.Core.Research.ResearchStrategyKind.MinVariance2
            ? (rr < 0.10
                ? ClawInv.Core.Research.RegimeKind.None
                : rr < 0.55 ? ClawInv.Core.Research.RegimeKind.Breadth
                : ClawInv.Core.Research.RegimeKind.IndexTrend)
            : (rr < 0.20
                ? ClawInv.Core.Research.RegimeKind.None
                : rr < 0.45 ? ClawInv.Core.Research.RegimeKind.Breadth
                : rr < 0.70 ? ClawInv.Core.Research.RegimeKind.IndexTrend
                : ClawInv.Core.Research.RegimeKind.IndexRsi);

        var regimeMa = regime == ClawInv.Core.Research.RegimeKind.IndexTrend
            ? Pick(6, 9, 12, 18)
            : 0;

        // RegimeBreadthThreshold reused as threshold for Breadth (0..1) and RSI (0..100)
        var thresh = regime switch
        {
            ClawInv.Core.Research.RegimeKind.Breadth => (Flip(0.5) ? 0.60 : 0.70),
            ClawInv.Core.Research.RegimeKind.IndexRsi => (Flip(0.5) ? 50.0 : 55.0),
            _ => 0.0
        };

        var riskOffMode = kind == ClawInv.Core.Research.ResearchStrategyKind.MinVariance2
            ? (regime != ClawInv.Core.Research.RegimeKind.None && Flip(0.85)
                ? ClawInv.Core.Strategies.RiskOffMode.DefensiveFund
                : ClawInv.Core.Strategies.RiskOffMode.Cash)
            : (regime != ClawInv.Core.Research.RegimeKind.None && Flip(0.50)
                ? ClawInv.Core.Strategies.RiskOffMode.DefensiveFund
                : ClawInv.Core.Strategies.RiskOffMode.Cash);

        var defVolLb = kind == ClawInv.Core.Research.ResearchStrategyKind.MinVariance2
            ? Pick(3, 6, 12)
            : 3;

        // DD penalty strength (can be 0 now that we're not enforcing an MDD floor)
        var lambda = Pick(0, 0, 0, 25, 50, 100) / 100.0;

        return new ClawInv.Core.Research.TrialParams(
            Kind: kind,
            LookbackMonths: lookback,
            RebalanceMonths: reb,
            TopK: topK,
            UseAbsoluteMomentum: abs,
            UseLowVolFilter: useLowVol,
            VolLookbackMonths: Math.Max(2, volLb),
            TrendMaMonths: Math.Max(1, ma),
            Regime: regime,
            RegimeMaMonths: Math.Max(1, regimeMa),
            RegimeBreadthThreshold: thresh,
            RiskOffMode: riskOffMode,
            DefensiveVolLookbackMonths: defVolLb,
            MaxDrawdownPenaltyLambda: lambda);
    }
}
