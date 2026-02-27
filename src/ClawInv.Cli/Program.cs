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

        [CommandOption("--max-requests <N>")]
        public int MaxRequests { get; init; } = 40;

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
        AnsiConsole.MarkupLine($"Generating universe: target [green]{settings.Target}[/] funds (max requests {settings.MaxRequests})...");
        var u = await gen.GenerateAsync(settings.Target, settings.MaxRequests);
        ClawInv.Core.Backtest.UniverseGenerator.Save(u, settings.OutPath);

        AnsiConsole.MarkupLine($"Wrote [green]{u.Funds.Count}[/] funds to [grey]{settings.OutPath}[/]");
        return 0;
    }
}
