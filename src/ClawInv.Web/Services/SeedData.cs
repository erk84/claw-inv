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

        // Ensure all strategy kinds exist as selectable configs.
        // We do this even if the table already has rows (upsert missing ones).

        // BestStrategyV1 is legacy and effectively duplicates Momentum now.
        // Keep it in DB if it exists (for historical data), but force-disable it.
        var legacy = await db.StrategyConfigs.SingleOrDefaultAsync(x => x.Key == "BestStrategyV1/default", ct);
        if (legacy is not null && legacy.Enabled)
        {
            legacy.Enabled = false;
            await db.SaveChangesAsync(ct);
        }

        var existingKeys = await db.StrategyConfigs
            .Select(x => x.Key)
            .ToListAsync(ct);
        var keySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        var toAdd = new List<StrategyConfig>();

        foreach (var kind in Enum.GetValues<ClawInv.Core.Research.ResearchStrategyKind>())
        {
            // Skip legacy BestStrategyV1 row entirely (we don't want it selectable).
            // Note: it's not an enum kind; it's a separate key.

            var cfg = CreateLockedDefault(kind);
            if (!keySet.Contains(cfg.Key))
                toAdd.Add(cfg);
        }

        if (toAdd.Count > 0)
        {
            db.StrategyConfigs.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static StrategyConfig CreateLockedDefault(ClawInv.Core.Research.ResearchStrategyKind kind)
    {
        // Default: disabled and slots=2 (user-configurable).
        // All other params are locked by Strategies page save logic.

        var cfg = new StrategyConfig
        {
            Key = $"{kind}/default",
            DisplayName = kind.ToString(),
            Enabled = false,
            Slots = 2,
            Kind = kind,
            DefaultSource = "Locked defaults (seed)",
        };

        // Provide best-known defaults for a few important kinds.
        switch (kind)
        {
            case ClawInv.Core.Research.ResearchStrategyKind.MeanReversion:
                cfg.Key = "MeanReversion/research-final-10y";
                cfg.DisplayName = "MeanReversion";
                cfg.LookbackMonths = 3;
                cfg.RebalanceMonths = 2;
                cfg.TopK = 1;
                cfg.UseAbsoluteMomentum = false;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 12;
                cfg.TrendMaMonths = 1;
                cfg.DefaultSource = "Research: best_MeanReversion.json (~1.305M final over 10y)";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.Momentum:
                cfg.Key = "Momentum/research-default";
                cfg.DisplayName = "Momentum";
                cfg.LookbackMonths = 12;
                cfg.RebalanceMonths = 3;
                cfg.TopK = 2;
                cfg.UseAbsoluteMomentum = true;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 6;
                cfg.TrendMaMonths = 18;
                cfg.DefaultSource = "Research: best_Momentum.json (seeded)";
                break;

            // Baselines for a few common families
            case ClawInv.Core.Research.ResearchStrategyKind.Trend:
                cfg.DisplayName = "Trend";
                cfg.LookbackMonths = 3;
                cfg.RebalanceMonths = 1;
                cfg.TopK = 2;
                cfg.UseAbsoluteMomentum = true;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 12;
                cfg.TrendMaMonths = 12;
                cfg.DefaultSource = "Baseline defaults";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.LowVol:
                cfg.DisplayName = "LowVol";
                cfg.LookbackMonths = 2;
                cfg.RebalanceMonths = 2;
                cfg.TopK = 2;
                cfg.UseAbsoluteMomentum = false;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 2;
                cfg.TrendMaMonths = 1;
                cfg.DefaultSource = "Baseline defaults";
                break;
        }

        return cfg;
    }
}
