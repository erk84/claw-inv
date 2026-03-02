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

        public int LookbackMonths { get; set; }
        public int RebalanceMonths { get; set; }
        public int TopK { get; set; }
        public bool UseAbsoluteMomentum { get; set; }
        public bool UseLowVolFilter { get; set; }

        public int Regime { get; set; }
        public int RegimeMaMonths { get; set; }
        public double RegimeThreshold { get; set; }

        public int RiskOffMode { get; set; }
        public int DefensiveVolLookbackMonths { get; set; }

        public string DefaultSource { get; set; } = "";
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Items = await db.StrategyConfigs
            .OrderBy(x => x.DisplayName)
            .Select(x => new Item
            {
                Id = x.Id,
                Enabled = x.Enabled,
                DisplayName = x.DisplayName,
                Slots = x.Slots,
                Kind = x.Kind.ToString(),
                LookbackMonths = x.LookbackMonths,
                RebalanceMonths = x.RebalanceMonths,
                TopK = x.TopK,
                UseAbsoluteMomentum = x.UseAbsoluteMomentum,
                UseLowVolFilter = x.UseLowVolFilter,
                Regime = (int)x.Regime,
                RegimeMaMonths = x.RegimeMaMonths,
                RegimeThreshold = x.RegimeThreshold,
                RiskOffMode = (int)x.RiskOffMode,
                DefensiveVolLookbackMonths = x.DefensiveVolLookbackMonths,
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
            row.Enabled = i.Enabled;
            row.DisplayName = i.DisplayName;
            row.Slots = Math.Clamp(i.Slots, 1, 50);

            row.Regime = (ClawInv.Core.Research.RegimeKind)Math.Clamp(i.Regime, 0, 3);
            row.RegimeMaMonths = Math.Clamp(i.RegimeMaMonths, 1, 24);
            row.RegimeThreshold = Math.Clamp(i.RegimeThreshold, -1.0, 1.0);

            row.RiskOffMode = (ClawInv.Core.Strategies.RiskOffMode)Math.Clamp(i.RiskOffMode, 0, 1);
            row.DefensiveVolLookbackMonths = Math.Clamp(i.DefensiveVolLookbackMonths, 1, 24);

            if (!wasEnabled && row.Enabled)
                newlyEnabled.Add(row.Id);

            if (wasEnabled && !row.Enabled)
                newlyDisabled.Add(row.Id);

            // Soft-change: mark pending when settings changed.
            // (We'll use this later in the rebalance recommendation logic.)
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
