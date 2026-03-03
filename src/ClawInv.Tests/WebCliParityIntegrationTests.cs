using ClawInv.Core.Backtest;
using ClawInv.Core.Research;
using ClawInv.Core.Strategies;
using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ClawInv.Tests;

public sealed class WebCliParityIntegrationTests
{
    [Fact]
    public async Task Web_bootstrap_equity_should_match_core_backtest_for_locked_strategies()
    {
        // Opt-in only (this hits Avanza/network unless NAV is already cached).
        if (!string.Equals(Environment.GetEnvironmentVariable("CLAWINV_RUN_INTEGRATION"), "1", StringComparison.Ordinal))
            return;

        var repoRoot = FindRepoRoot();
        var universePath = Path.Combine(repoRoot, "data", "universe.json");
        Assert.True(File.Exists(universePath), $"Missing universe.json: {universePath}");

        // Use a fixed as-of so result is deterministic given the same NAV cache.
        var asOf = new DateOnly(2026, 03, 02);
        var from = asOf.AddYears(-10);

        // Shared dirs (so Web and Core use identical NAV inputs).
        var cacheDir = Path.Combine(repoRoot, "tmp", "verify-cache");
        var navDir = Path.Combine(repoRoot, "tmp", "verify-nav");
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(navDir);

        await using var sp = BuildWebServiceProvider(repoRoot, universePath, cacheDir, navDir);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Momentum (locked best-known)
        await AssertStrategyAsync(
            scope.ServiceProvider,
            db,
            kind: ResearchStrategyKind.Momentum,
            strategyKey: "Momentum/research-default",
            def: new StrategyDefinition(
                Id: "CLI",
                Name: "CLI",
                Type: StrategyType.MomentumRotation,
                RebalanceEveryMonths: 3,
                LookbackMonths: 9,
                TopK: 2, // slots=2
                Allocation: AllocationMode.EqualWeightTopK,
                UseAbsoluteMomentumFilter: false,
                MovingAverageMonths: 12,
                VolatilityLookbackMonths: 6,
                UseLowVolFilter: false,
                Regime: RegimeKind.None,
                RegimeMaMonths: 12,
                RegimeThreshold: 0,
                RiskOffMode: RiskOffMode.Cash,
                DefensiveVolLookbackMonths: 6),
            asOf,
            from);

        // MeanReversion (locked best-known)
        await AssertStrategyAsync(
            scope.ServiceProvider,
            db,
            kind: ResearchStrategyKind.MeanReversion,
            strategyKey: "MeanReversion/research-final-10y",
            def: new StrategyDefinition(
                Id: "CLI",
                Name: "CLI",
                Type: StrategyType.MeanReversionRotation,
                RebalanceEveryMonths: 2,
                LookbackMonths: 2,
                TopK: 2, // slots=2
                Allocation: AllocationMode.EqualWeightTopK,
                UseAbsoluteMomentumFilter: false,
                MovingAverageMonths: 1,
                VolatilityLookbackMonths: 12,
                UseLowVolFilter: false,
                Regime: RegimeKind.None,
                RegimeMaMonths: 12,
                RegimeThreshold: 0,
                RiskOffMode: RiskOffMode.Cash,
                DefensiveVolLookbackMonths: 6),
            asOf,
            from);
    }

    private static async Task AssertStrategyAsync(
        IServiceProvider sp,
        AppDbContext db,
        ResearchStrategyKind kind,
        string strategyKey,
        StrategyDefinition def,
        DateOnly asOf,
        DateOnly from)
    {
        var cfgRow = new StrategyConfig
        {
            Key = strategyKey,
            DisplayName = kind.ToString(),
            Enabled = true,
            Kind = kind,
            Slots = def.TopK,
            LookbackMonths = def.LookbackMonths,
            RebalanceMonths = def.RebalanceEveryMonths,
            TopK = 1, // locked family param (not used for live holdings; Slots drives TopK in StrategyMapper)
            UseAbsoluteMomentum = def.UseAbsoluteMomentumFilter,
            UseLowVolFilter = def.UseLowVolFilter,
            VolLookbackMonths = def.VolatilityLookbackMonths,
            TrendMaMonths = def.MovingAverageMonths,
            DefaultSource = "integration-test",
            Regime = RegimeKind.None,
            RegimeMaMonths = 12,
            RegimeThreshold = 0,
            RiskOffMode = RiskOffMode.Cash,
            DefensiveVolLookbackMonths = 6,
        };

        db.StrategyConfigs.Add(cfgRow);
        await db.SaveChangesAsync();

        // WEB path: bootstrap creates trades; snapshot engine produces equity index.
        var bootstrap = sp.GetRequiredService<BootstrapEngine>();
        await bootstrap.BootstrapLast5YearsIfEmptyAsync(cfgRow.Id, asOf, CancellationToken.None);

        var portfolioId = await db.Portfolios.Where(p => p.StrategyConfigId == cfgRow.Id).Select(p => p.Id).SingleAsync();
        var webEquity = await db.PortfolioDailySnapshots.Where(s => s.PortfolioId == portfolioId)
            .OrderByDescending(s => s.Date)
            .Select(s => s.EquityIndex)
            .FirstAsync();

        // CORE path: run the same backtester on the exact same NAV series web uses.
        var navService = sp.GetRequiredService<NavService>();
        var nav = await navService.LoadUniverseNavAsync(from, asOf, CancellationToken.None);
        var cli = MonthEndRebalanceDailyBacktester.Run(def, nav, from, asOf);
        var cliEquity = (double)(1.0m + cli.TotalReturn);

        var relDiff = Math.Abs(webEquity - cliEquity) / Math.Max(1e-12, Math.Abs(cliEquity));
        Assert.True(relDiff <= 0.001,
            $"{strategyKey}: WEB={webEquity:0.000000} CLI={cliEquity:0.000000} relDiff={relDiff:P4}");
    }

    private static ServiceProvider BuildWebServiceProvider(string repoRoot, string universePath, string cacheDir, string navDir)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var dbPath = Path.Combine(repoRoot, "tmp", $"verify-{Guid.NewGuid():N}.db");
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClawInv:UniversePath"] = universePath,
            ["ClawInv:AutoRegenerateUniverse"] = "false",
            ["ClawInv:CacheDir"] = cacheDir,
            ["ClawInv:NavStoreDir"] = navDir,
        }).Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSingleton<IWebHostEnvironment>(new FakeEnv { ContentRootPath = Path.Combine(repoRoot, "src", "ClawInv.Web") });

        services.AddSingleton<NavService>();
        services.AddSingleton<NavLookupService>();
        services.AddSingleton<SnapshotEngine>();
        services.AddSingleton<RecommendationEngine>();
        services.AddSingleton<BootstrapEngine>();

        return services.BuildServiceProvider();
    }

    private static string FindRepoRoot()
    {
        var start = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "data", "universe.json")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not find repo root from {start}");
    }

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ClawInv.Web";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
