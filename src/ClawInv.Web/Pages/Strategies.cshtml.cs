using ClawInv.Web.Data;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class StrategiesModel(AppDbContext db, BackgroundTaskWorker tasks) : PageModel
{
    [BindProperty]
    public List<Item> Items { get; set; } = new();

    public sealed class Item
    {
        public int Id { get; set; }
        public bool Enabled { get; set; }
        public string DisplayName { get; set; } = "";
        public int Slots { get; set; }

        public string Kind { get; set; } = "";

        public string DefaultSource { get; set; } = "";
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await db.StrategyConfigs
            .Where(x => x.Key != "BestStrategyV1/default")
            .OrderBy(x => x.DisplayName)
            .Select(x => new Item
            {
                Id = x.Id,
                Enabled = x.Enabled,
                DisplayName = x.DisplayName,
                Slots = x.Slots,
                Kind = x.Kind.ToString(),
                DefaultSource = x.DefaultSource,
            })
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var ids = Items.Select(x => x.Id).ToHashSet();
        var rows = await db.StrategyConfigs.Where(x => ids.Contains(x.Id)).ToListAsync(ct);

        var newlyEnabled = new List<int>();
        var newlyDisabled = new List<int>();

        foreach (var row in rows)
        {
            var i = Items.Single(x => x.Id == row.Id);

            var wasEnabled = row.Enabled;
            var wasSlots = row.Slots;

            row.Enabled = i.Enabled;
            row.Slots = Math.Clamp(i.Slots, 1, 50);

            // All other parameters are locked to code defaults ("best known" from backtests).
            ApplyOptimalDefaults(row);

            if (!wasEnabled && row.Enabled)
                newlyEnabled.Add(row.Id);

            if (wasEnabled && !row.Enabled)
                newlyDisabled.Add(row.Id);

            // Soft-change only when something relevant changed.
            if (wasEnabled != row.Enabled || wasSlots != row.Slots)
                row.PendingChangesAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        // If a strategy is disabled, clear its model history so re-enabling starts clean.
        // (This is destructive by design.)
        if (newlyDisabled.Count > 0)
        {
            foreach (var id in newlyDisabled)
                await ClearStrategyHistoryAsync(id, ct);
        }

        // Bootstrap any newly enabled strategies (non-destructive: only if empty)
        // Done in background so the UI stays responsive (especially on a Raspberry Pi).
        if (newlyEnabled.Count > 0)
        {
            var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            foreach (var id in newlyEnabled)
                await tasks.EnqueueBootstrapAsync(id, asOf, ct);
        }

        return RedirectToPage();
    }

    private static void ApplyOptimalDefaults(Data.Entities.StrategyConfig row)
    {
        // These defaults represent "best known" settings from backtests/research.
        // UI intentionally cannot modify them; only Slots is configurable.

        // Reset regime/risk-off unless you explicitly want them enabled per-strategy.
        row.Regime = ClawInv.Core.Research.RegimeKind.None;
        row.RegimeMaMonths = 10;
        row.RegimeThreshold = 0.0;
        row.RiskOffMode = ClawInv.Core.Strategies.RiskOffMode.Cash;
        row.DefensiveVolLookbackMonths = 12;

        switch (row.Kind)
        {
            case ClawInv.Core.Research.ResearchStrategyKind.MeanReversion:
                // Best-known (10y grid): lb=2 reb=2 topK=1, slots=2
                row.LookbackMonths = 2;
                row.RebalanceMonths = 2;
                row.TopK = 1;
                row.UseAbsoluteMomentum = false;
                row.UseLowVolFilter = false;
                row.VolLookbackMonths = 12;
                row.TrendMaMonths = 1;
                row.DefaultSource = "Best-known grid (locked): lb=2 reb=2 topK=1 (10y, slots=2)";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.Momentum:
                // Best-known after impl #7: dual-horizon momentum.
                // Best grid: lb=9 reb=3 topK=1 (10y, slots=2). Note: abs-mom is OFF in CLI unless explicitly set.
                row.LookbackMonths = 9;
                row.RebalanceMonths = 3;
                row.TopK = 1;
                row.UseAbsoluteMomentum = false;
                row.UseLowVolFilter = false;
                row.VolLookbackMonths = 6;
                row.TrendMaMonths = 12;
                row.DefaultSource = "Best-known grid (locked): lb=9 reb=3 topK=1 (10y, slots=2)";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.Trend:
                row.LookbackMonths = 3;
                row.RebalanceMonths = 1;
                row.TopK = 2;
                row.UseAbsoluteMomentum = true;
                row.UseLowVolFilter = false;
                row.VolLookbackMonths = 12;
                row.TrendMaMonths = 12;
                row.DefaultSource = "Baseline (locked)";
                break;

            case ClawInv.Core.Research.ResearchStrategyKind.LowVol:
                row.LookbackMonths = 2;
                row.RebalanceMonths = 2;
                row.TopK = 2;
                row.UseAbsoluteMomentum = false;
                row.UseLowVolFilter = false;
                row.VolLookbackMonths = 2;
                row.TrendMaMonths = 1;
                row.DefaultSource = "Baseline (locked)";
                break;

            // Other kinds: keep existing params but ensure sane values.
            default:
                row.LookbackMonths = Math.Clamp(row.LookbackMonths, 1, 24);
                row.RebalanceMonths = Math.Clamp(row.RebalanceMonths, 1, 6);
                row.TopK = Math.Clamp(row.TopK, 1, 10);
                row.VolLookbackMonths = Math.Clamp(row.VolLookbackMonths, 1, 24);
                row.TrendMaMonths = Math.Clamp(row.TrendMaMonths, 1, 24);
                row.DefaultSource = string.IsNullOrWhiteSpace(row.DefaultSource) ? "Locked (unspecified)" : row.DefaultSource;
                break;
        }
    }

    private async Task ClearStrategyHistoryAsync(int strategyConfigId, CancellationToken ct)
    {
        // Delete model data for this strategy.
        // Note: ExecuteDelete requires EF Core 7+ (we're on 8).

        // Find portfolio id(s) for this strategy.
        var portfolioIds = await db.Portfolios
            .Where(p => p.StrategyConfigId == strategyConfigId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (portfolioIds.Count > 0)
        {
            await db.PortfolioDailySnapshots
                .Where(s => portfolioIds.Contains(s.PortfolioId))
                .ExecuteDeleteAsync(ct);

            await db.TradeEvents
                .Where(t => portfolioIds.Contains(t.PortfolioId))
                .ExecuteDeleteAsync(ct);

            await db.PortfolioHoldings
                .Where(h => portfolioIds.Contains(h.PortfolioId))
                .ExecuteDeleteAsync(ct);

            await db.Portfolios
                .Where(p => portfolioIds.Contains(p.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Recommendation runs + trade recommendations
        var runIds = await db.RecommendationRuns
            .Where(r => r.StrategyConfigId == strategyConfigId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (runIds.Count > 0)
        {
            await db.TradeRecommendations
                .Where(t => runIds.Contains(t.RecommendationRunId))
                .ExecuteDeleteAsync(ct);

            await db.RecommendationRuns
                .Where(r => runIds.Contains(r.Id))
                .ExecuteDeleteAsync(ct);
        }

        // Background tasks
        await db.BackgroundTasks
            .Where(t => t.StrategyConfigId == strategyConfigId)
            .ExecuteDeleteAsync(ct);
    }
}
