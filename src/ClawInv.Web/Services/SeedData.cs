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

        // BestStrategyV1 is legacy and effectively duplicates Momentum now.
        // Remove it entirely if present (including dependent history).
        await RemoveLegacyBestStrategyV1Async(db, ct);

        // Ensure all strategy kinds exist as selectable configs.
        var existingKeys = await db.StrategyConfigs
            .Select(x => x.Key)
            .ToListAsync(ct);
        var keySet = existingKeys.ToHashSet(StringComparer.Ordinal);

        var toAdd = new List<StrategyConfig>();

        foreach (var kind in Enum.GetValues<ClawInv.Core.Research.ResearchStrategyKind>())
        {
            var cfg = CreateLockedDefault(kind);
            if (!keySet.Contains(cfg.Key))
                toAdd.Add(cfg);
        }

        if (toAdd.Count > 0)
        {
            db.StrategyConfigs.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }

        // Enforce locked defaults for existing rows too (so web + CLI stay consistent).
        // Preserve user-controlled fields: Enabled + Slots.
        // If locked params change for an enabled strategy, we must rebuild its history so UI matches.
        var rows = await db.StrategyConfigs.ToListAsync(ct);
        var needsRebootstrap = new List<int>();

        foreach (var row in rows)
        {
            var desired = CreateLockedDefault(row.Kind);

            var enabled = row.Enabled;
            var slots = row.Slots <= 0 ? 2 : row.Slots;
            var pending = row.PendingChangesAtUtc;

            var lockedChanged =
                row.LookbackMonths != desired.LookbackMonths ||
                row.RebalanceMonths != desired.RebalanceMonths ||
                row.TopK != desired.TopK ||
                row.UseAbsoluteMomentum != desired.UseAbsoluteMomentum ||
                row.UseLowVolFilter != desired.UseLowVolFilter ||
                row.VolLookbackMonths != desired.VolLookbackMonths ||
                row.TrendMaMonths != desired.TrendMaMonths ||
                row.Regime != desired.Regime ||
                row.RegimeMaMonths != desired.RegimeMaMonths ||
                Math.Abs(row.RegimeThreshold - desired.RegimeThreshold) > 1e-12 ||
                row.RiskOffMode != desired.RiskOffMode ||
                row.DefensiveVolLookbackMonths != desired.DefensiveVolLookbackMonths;

            row.Key = desired.Key;
            row.DisplayName = desired.DisplayName;
            row.Kind = desired.Kind;

            row.Enabled = enabled;
            row.Slots = slots;
            row.PendingChangesAtUtc = pending;

            row.LookbackMonths = desired.LookbackMonths;
            row.RebalanceMonths = desired.RebalanceMonths;
            row.TopK = desired.TopK;
            row.UseAbsoluteMomentum = desired.UseAbsoluteMomentum;
            row.UseLowVolFilter = desired.UseLowVolFilter;
            row.VolLookbackMonths = desired.VolLookbackMonths;
            row.TrendMaMonths = desired.TrendMaMonths;
            row.DefaultSource = desired.DefaultSource;

            row.Regime = desired.Regime;
            row.RegimeMaMonths = desired.RegimeMaMonths;
            row.RegimeThreshold = desired.RegimeThreshold;
            row.RiskOffMode = desired.RiskOffMode;
            row.DefensiveVolLookbackMonths = desired.DefensiveVolLookbackMonths;

            if (enabled && lockedChanged)
                needsRebootstrap.Add(row.Id);
        }

        await db.SaveChangesAsync(ct);

        if (needsRebootstrap.Count > 0)
        {
            var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            var bootstrap = scope.ServiceProvider.GetRequiredService<BootstrapEngine>();

            foreach (var id in needsRebootstrap.Distinct())
            {
                await ClearStrategyHistoryAsync(db, id, ct);
                await bootstrap.BootstrapLast5YearsIfEmptyAsync(id, asOf, ct);
            }
        }

        // Ensure enabled strategies eventually get history even if they were enabled long ago
        // (and thus do not trigger OnPost newlyEnabled). We enqueue a durable DB task when
        // there are no snapshots yet.
        var enabledIds = await db.StrategyConfigs.Where(s => s.Enabled).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in enabledIds)
        {
            var hasSnapshots = await db.Portfolios
                .Where(p => p.StrategyConfigId == id)
                .AnyAsync(p => db.PortfolioDailySnapshots.Any(s => s.PortfolioId == p.Id), ct);

            if (hasSnapshots)
                continue;

            var hasPending = await db.BackgroundTasks.AnyAsync(t => t.StrategyConfigId == id && t.Type == BackgroundTaskType.BootstrapStrategy && t.Status == BackgroundTaskStatus.Pending, ct);
            if (hasPending)
                continue;

            db.BackgroundTasks.Add(new BackgroundTask
            {
                Type = BackgroundTaskType.BootstrapStrategy,
                Status = BackgroundTaskStatus.Pending,
                StrategyConfigId = id,
                Message = $"Bootstrap last 5y as-of {DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1))}"
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task ClearStrategyHistoryAsync(AppDbContext db, int strategyConfigId, CancellationToken ct)
    {
        var portfolioIds = await db.Portfolios
            .Where(p => p.StrategyConfigId == strategyConfigId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (portfolioIds.Count > 0)
        {
            await db.PortfolioDailySnapshots.Where(s => portfolioIds.Contains(s.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.TradeEvents.Where(t => portfolioIds.Contains(t.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.PortfolioHoldings.Where(h => portfolioIds.Contains(h.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.Portfolios.Where(p => portfolioIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
        }

        var runIds = await db.RecommendationRuns
            .Where(r => r.StrategyConfigId == strategyConfigId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (runIds.Count > 0)
        {
            await db.TradeRecommendations.Where(t => runIds.Contains(t.RecommendationRunId)).ExecuteDeleteAsync(ct);
            await db.RecommendationRuns.Where(r => runIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
        }

        await db.BackgroundTasks.Where(t => t.StrategyConfigId == strategyConfigId).ExecuteDeleteAsync(ct);
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
                cfg.LookbackMonths = 2;
                cfg.RebalanceMonths = 2;
                cfg.TopK = 1;
                cfg.UseAbsoluteMomentum = false;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 12;
                cfg.TrendMaMonths = 1;
                cfg.DefaultSource = "Best-known grid (seeded): lb=2 reb=2 topK=1 (10y, slots=2)";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.Momentum:
                cfg.Key = "Momentum/research-default";
                cfg.DisplayName = "Momentum";
                cfg.LookbackMonths = 9;
                cfg.RebalanceMonths = 3;
                cfg.TopK = 1;
                cfg.UseAbsoluteMomentum = false;
                cfg.UseLowVolFilter = false;
                cfg.VolLookbackMonths = 6;
                cfg.TrendMaMonths = 12;
                cfg.DefaultSource = "Best-known grid (seeded): lb=9 reb=3 topK=1 (10y, slots=2)";
                break;

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

    private static async Task RemoveLegacyBestStrategyV1Async(AppDbContext db, CancellationToken ct)
    {
        var legacy = await db.StrategyConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == "BestStrategyV1/default", ct);

        if (legacy is null)
            return;

        var id = legacy.Id;

        // Clear dependent model/history data first.
        var portfolioIds = await db.Portfolios
            .Where(p => p.StrategyConfigId == id)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (portfolioIds.Count > 0)
        {
            await db.PortfolioDailySnapshots.Where(s => portfolioIds.Contains(s.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.TradeEvents.Where(t => portfolioIds.Contains(t.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.PortfolioHoldings.Where(h => portfolioIds.Contains(h.PortfolioId)).ExecuteDeleteAsync(ct);
            await db.Portfolios.Where(p => portfolioIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
        }

        var runIds = await db.RecommendationRuns
            .Where(r => r.StrategyConfigId == id)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (runIds.Count > 0)
        {
            await db.TradeRecommendations.Where(t => runIds.Contains(t.RecommendationRunId)).ExecuteDeleteAsync(ct);
            await db.RecommendationRuns.Where(r => runIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
        }

        await db.BackgroundTasks.Where(t => t.StrategyConfigId == id).ExecuteDeleteAsync(ct);

        // Finally remove the legacy config row.
        await db.StrategyConfigs.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
    }
}
