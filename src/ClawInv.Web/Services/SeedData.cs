using ClawInv.Core.Strategies;
using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public static class SeedData
{
    public static async Task EnsureSeededAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Ensure default universe settings row exists.
        var us = await db.UniverseSettings.SingleOrDefaultAsync(x => x.Key == "default", ct);
        if (us is null)
        {
            db.UniverseSettings.Add(new UniverseSettings { Key = "default" });
            await db.SaveChangesAsync(ct);
        }

        if (await db.StrategyConfigs.AnyAsync(ct))
            return;

        // Seed strategy configs. Default: disabled and slots=2.
        // Sources are displayed in UI so you can see where defaults come from.
        var list = new List<StrategyConfig>();

        // From BestStrategies.cs (legacy best_v1)
        var v1 = BestStrategies.BestStrategyV1;
        list.Add(new StrategyConfig
        {
            Key = "BestStrategyV1/default",
            DisplayName = v1.Name,
            Enabled = false,
            Slots = v1.TopK,
            Kind = ClawInv.Core.Research.ResearchStrategyKind.Momentum,
            LookbackMonths = v1.LookbackMonths,
            RebalanceMonths = v1.RebalanceEveryMonths,
            TopK = v1.TopK,
            UseAbsoluteMomentum = v1.UseAbsoluteMomentumFilter,
            UseLowVolFilter = v1.UseLowVolFilter,
            VolLookbackMonths = v1.VolatilityLookbackMonths,
            TrendMaMonths = Math.Max(1, v1.MovingAverageMonths),
            DefaultSource = "BestStrategies.cs: BestStrategyV1",
        });

        // From research best (10y final-cap runs): MeanReversion ~1.305M
        list.Add(new StrategyConfig
        {
            Key = "MeanReversion/research-final-10y",
            DisplayName = "MeanReversion (research default)",
            Enabled = false,
            Slots = 2, // user default; live slots are configurable
            Kind = ClawInv.Core.Research.ResearchStrategyKind.MeanReversion,
            LookbackMonths = 3,
            RebalanceMonths = 2,
            TopK = 1,
            UseAbsoluteMomentum = false,
            UseLowVolFilter = false,
            VolLookbackMonths = 12,
            TrendMaMonths = 1,
            DefaultSource = "Research: best_MeanReversion.json (~1.305M final over 10y)",
        });

        // Add a few baseline configs for other kinds (disabled). These are safe starting points;
        // they'll be tuned later via UI and/or research.
        list.Add(new StrategyConfig
        {
            Key = "Momentum/research-default",
            DisplayName = "Momentum (research default)",
            Enabled = false,
            Slots = 2,
            Kind = ClawInv.Core.Research.ResearchStrategyKind.Momentum,
            LookbackMonths = 12,
            RebalanceMonths = 3,
            TopK = 2,
            UseAbsoluteMomentum = true,
            UseLowVolFilter = false,
            VolLookbackMonths = 6,
            TrendMaMonths = 18,
            DefaultSource = "Research: best_Momentum.json (seeded)",
        });

        list.Add(new StrategyConfig
        {
            Key = "Trend/default",
            DisplayName = "Trend (default)",
            Enabled = false,
            Slots = 2,
            Kind = ClawInv.Core.Research.ResearchStrategyKind.Trend,
            LookbackMonths = 3,
            RebalanceMonths = 1,
            TopK = 2,
            UseAbsoluteMomentum = true,
            UseLowVolFilter = false,
            VolLookbackMonths = 12,
            TrendMaMonths = 1,
            DefaultSource = "Baseline defaults",
        });

        list.Add(new StrategyConfig
        {
            Key = "LowVol/default",
            DisplayName = "LowVol (default)",
            Enabled = false,
            Slots = 2,
            Kind = ClawInv.Core.Research.ResearchStrategyKind.LowVol,
            LookbackMonths = 2,
            RebalanceMonths = 2,
            TopK = 2,
            UseAbsoluteMomentum = false,
            UseLowVolFilter = false,
            VolLookbackMonths = 2,
            TrendMaMonths = 1,
            DefaultSource = "Baseline defaults",
        });

        db.StrategyConfigs.AddRange(list);
        await db.SaveChangesAsync(ct);
    }
}
