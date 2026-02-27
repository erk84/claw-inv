using Spectre.Console;
using Spectre.Console.Cli;

namespace ClawInv.Cli;

/// <summary>
/// Runs multiple family searches in a single run (load NAV + build matrices once).
/// This matches the user's intent: one program execution backtests multiple strategy families.
/// </summary>
public sealed class SearchFamiliesCommand : AsyncCommand<SearchFamiliesCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--universe <PATH>")]
        public string UniversePath { get; init; } = "out/universe.json";

        [CommandOption("--years <N>")]
        public int Years { get; init; } = 10;

        [CommandOption("--trials <N>")]
        public int TrialsPerFamily { get; init; } = 50_000;

        [CommandOption("--kinds <CSV>")]
        public string KindsCsv { get; init; } = "";

        [CommandOption("--cache-dir <DIR>")]
        public string CacheDir { get; init; } = ".cache/avanza";

        [CommandOption("--nav-store <DIR>")]
        public string NavStoreDir { get; init; } = ".cache/nav";

        [CommandOption("--out-dir <DIR>")]
        public string OutDir { get; init; } = "out/families";

        [CommandOption("--seed <N>")]
        public int Seed { get; init; } = 123;
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

        var kinds = ParseKinds(settings.KindsCsv);
        Directory.CreateDirectory(settings.OutDir);

        AnsiConsole.MarkupLine($"Running [bold]{kinds.Length}[/] families, trials per family: [bold]{settings.TrialsPerFamily:N0}[/]");

        var results = new List<(ClawInv.Core.Research.ResearchStrategyKind kind, ClawInv.Core.Research.TrialResult best, ClawInv.Core.Backtest.BacktestResult daily)>(kinds.Length);

        for (var ki = 0; ki < kinds.Length; ki++)
        {
            var kind = kinds[ki];
            var rnd = new Random(unchecked(settings.Seed + ki * 10_000 + (int)kind * 17));

            ClawInv.Core.Research.TrialResult? best = null;

            for (var i = 1; i <= settings.TrialsPerFamily; i++)
            {
                var p0 = SearchBestCommand.Sample(rnd);
                var p = p0 with { Kind = kind, TopK = Math.Min(2, p0.TopK) };
                var r = search.Evaluate(p);
                if (double.IsNaN(r.Score)) continue;

                if (best is null || r.Score > best.Score)
                    best = r;

                if (i % 10_000 == 0 && best is not null)
                {
                    var kindTxt = Markup.Escape(kind.ToString());
                    AnsiConsole.MarkupLine($"[grey]{kindTxt}[/] T={i:N0}: best score={best.Score:0.000} sharpe={best.Sharpe:0.##} cagr={best.Cagr:P2} mdd={best.MaxDrawdown:P2}");
                }
            }

            if (best is null)
            {
                var kindTxt = Markup.Escape(kind.ToString());
                AnsiConsole.MarkupLine($"[grey]{kindTxt}[/] No valid trials.");
                continue;
            }

            var daily = SearchBestCommand.ValidateDaily(best, nav, from, to);
            results.Add((kind, best, daily));

            var outPath = Path.Combine(settings.OutDir, $"best_{kind}.json");
            File.WriteAllText(outPath, System.Text.Json.JsonSerializer.Serialize(best, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            {
                var kindTxt = Markup.Escape(kind.ToString());
                AnsiConsole.MarkupLine($"[grey]{kindTxt}[/] Wrote [grey]{outPath}[/]  | daily Sharpe={(daily.Sharpe?.ToString("0.##") ?? "n/a")} CAGR={daily.Cagr:P2} MDD={daily.MaxDrawdown:P2}");
            }
        }

        // Summary table
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Kind");
        table.AddColumn("ME Sharpe");
        table.AddColumn("ME CAGR");
        table.AddColumn("ME MDD");
        table.AddColumn("Daily Sharpe");
        table.AddColumn("Daily CAGR");
        table.AddColumn("Daily MDD");

        foreach (var r in results.OrderByDescending(x => x.best.Score))
        {
            table.AddRow(
                r.kind.ToString(),
                r.best.Sharpe.ToString("0.##"),
                (r.best.Cagr * 100.0).ToString("0.##") + "%",
                (r.best.MaxDrawdown * 100.0).ToString("0.##") + "%",
                r.daily.Sharpe?.ToString("0.##") ?? "n/a",
                (r.daily.Cagr * 100m).ToString("0.##") + "%",
                (r.daily.MaxDrawdown * 100m).ToString("0.##") + "%"
            );
        }

        AnsiConsole.Write(table);

        return 0;
    }

    private static ClawInv.Core.Research.ResearchStrategyKind[] ParseKinds(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return Enum.GetValues<ClawInv.Core.Research.ResearchStrategyKind>();

        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<ClawInv.Core.Research.ResearchStrategyKind>();
        foreach (var p in parts)
        {
            if (Enum.TryParse<ClawInv.Core.Research.ResearchStrategyKind>(p, ignoreCase: true, out var k))
                list.Add(k);
            else
                throw new ArgumentException($"Unknown kind: {p}");
        }
        return list.Distinct().ToArray();
    }
}
