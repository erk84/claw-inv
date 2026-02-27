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
