using ClawInv.Core;
using ClawInv.Core.Avanza;
using Spectre.Console;
using Spectre.Console.Cli;

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

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";

        [CommandOption("--nav-store <DIR>")]
        public string NavStoreDir { get; init; } = ".cache/nav";

        [CommandOption("--out <PATH>")]
        public string OutPath { get; init; } = "out/best_strategy.json";
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
        ClawInv.Core.Research.TrialResult? bestMonthEnd = null;

        const int KeepTop = 200;
        var top = new List<ClawInv.Core.Research.TrialResult>(capacity: KeepTop + 50);

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
            var p = Sample(rnd);
            var r = search.Evaluate(p);

            if (double.IsNaN(r.Score))
                continue;

            if (bestMonthEnd is null || r.Score > bestMonthEnd.Score)
                bestMonthEnd = r;

            Consider(r);

            if (i % 10_000 == 0 && bestMonthEnd is not null)
            {
                AnsiConsole.MarkupLine($"T={i:N0}: best(month-end) score={bestMonthEnd.Score:0.000} sharpe={bestMonthEnd.Sharpe:0.##} cagr={bestMonthEnd.Cagr:P2} mdd={bestMonthEnd.MaxDrawdown:P2} kind={bestMonthEnd.Params.Kind} lookback={bestMonthEnd.Params.LookbackMonths} reb={bestMonthEnd.Params.RebalanceMonths} topK={bestMonthEnd.Params.TopK} abs={bestMonthEnd.Params.UseAbsoluteMomentum} ma={bestMonthEnd.Params.TrendMaMonths} volLb={bestMonthEnd.Params.VolLookbackMonths} mddFloor={bestMonthEnd.Params.MaxDrawdownFloor:P0}" );
            }
        }

        if (bestMonthEnd is null)
        {
            AnsiConsole.MarkupLine("No valid trials.");
            return 1;
        }

        // Choose best candidate after DAILY validation under the same MDD floor.
        var ordered = top.OrderByDescending(x => x.Score).ToList();
        ClawInv.Core.Research.TrialResult? bestDailyOk = null;
        ClawInv.Core.Backtest.BacktestResult? bestDailyRes = null;

        foreach (var cand in ordered)
        {
            var daily = ValidateDaily(cand, nav, from, to);
            if (daily.MaxDrawdown >= -0.20m)
            {
                bestDailyOk = cand;
                bestDailyRes = daily;
                break;
            }
        }

        var chosen = bestDailyOk ?? bestMonthEnd;

        Directory.CreateDirectory(Path.GetDirectoryName(settings.OutPath) ?? ".");
        File.WriteAllText(settings.OutPath, System.Text.Json.JsonSerializer.Serialize(chosen, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        AnsiConsole.MarkupLine($"Wrote chosen strategy to [grey]{settings.OutPath}[/]");

        var chosenDaily = bestDailyRes ?? ValidateDaily(chosen, nav, from, to);
        AnsiConsole.MarkupLine($"Daily validation (chosen) => Sharpe: [yellow]{(chosenDaily.Sharpe?.ToString("0.##") ?? "n/a")}[/] CAGR: [green]{chosenDaily.Cagr:P2}[/] MDD: [red]{chosenDaily.MaxDrawdown:P2}[/]  ({chosenDaily.Notes})");

        if (bestDailyOk is null)
            AnsiConsole.MarkupLine("[red]No candidate passed daily MDD floor -20%.[/]");

        return 0;
    }

    private static ClawInv.Core.Backtest.BacktestResult ValidateDaily(
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
                    UseLowVolFilter: false),
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
                    UseLowVolFilter: false),
                nav, from, to).result;
        }

        if (p.Kind == ClawInv.Core.Research.ResearchStrategyKind.MeanReversion)
        {
            // Placeholder: currently uses MomentumRotationBacktester logic (positive momentum).
            // We keep mean-reversion in month-end research, but skip daily validation for now.
            return new ClawInv.Core.Backtest.BacktestResult(
                "trial_meanrev",
                "Trial MeanReversion (daily validation not implemented)",
                from,
                to,
                0,
                0m,
                0m,
                null,
                0m,
                0m,
                "Not implemented"
            );
        }

        // Momentum: month-end rebalance but simulate daily returns in-between (best match for month-end research)
        var def = new ClawInv.Core.Strategies.StrategyDefinition(
            Id: "trial_mom",
            Name: "Trial Momentum",
            Type: ClawInv.Core.Strategies.StrategyType.MomentumRotation,
            RebalanceEveryMonths: p.RebalanceMonths,
            LookbackMonths: p.LookbackMonths,
            TopK: Math.Min(2, p.TopK),
            Allocation: ClawInv.Core.Strategies.AllocationMode.EqualWeightTopK,
            UseAbsoluteMomentumFilter: p.UseAbsoluteMomentum,
            MovingAverageMonths: 0,
            VolatilityLookbackMonths: p.VolLookbackMonths,
            UseLowVolFilter: true);

        return ClawInv.Core.Backtest.MonthEndRebalanceDailyBacktester.Run(def, nav, from, to, maxDrawdownFloor: -0.20m);

    }

    private static ClawInv.Core.Research.TrialParams Sample(Random rnd)
    {
        int Pick(params int[] xs) => xs[rnd.Next(xs.Length)];
        bool Flip(double p) => rnd.NextDouble() < p;

        // Bias sampling towards momentum+trend-gate variants; they tend to reduce daily drawdowns.
        var kindRoll = rnd.NextDouble();
        var kind = kindRoll < 0.60
            ? ClawInv.Core.Research.ResearchStrategyKind.Momentum
            : (ClawInv.Core.Research.ResearchStrategyKind)rnd.Next(0, 4);

        var lookback = Pick(1, 2, 3, 4, 6, 9, 12);
        var reb = Pick(1, 2, 3);
        var topK = Pick(1, 2);

        var abs = kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum && Flip(0.75);

        var volLb = (kind == ClawInv.Core.Research.ResearchStrategyKind.LowVol || kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum)
            ? Pick(3, 6, 12)
            : 0;

        // TrendMaMonths doubles as a trend-gate for Momentum kind (if >=2)
        var ma = kind == ClawInv.Core.Research.ResearchStrategyKind.Trend
            ? Pick(6, 9, 12, 18)
            : (kind == ClawInv.Core.Research.ResearchStrategyKind.Momentum && Flip(0.70)
                ? Pick(6, 9, 12, 18)
                : 0);

        return new ClawInv.Core.Research.TrialParams(
            Kind: kind,
            LookbackMonths: lookback,
            RebalanceMonths: reb,
            TopK: topK,
            UseAbsoluteMomentum: abs,
            VolLookbackMonths: Math.Max(2, volLb),
            TrendMaMonths: Math.Max(1, ma),
            MaxDrawdownFloor: -0.20);
    }
}
