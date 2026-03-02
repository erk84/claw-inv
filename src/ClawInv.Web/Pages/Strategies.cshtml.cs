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

            // Soft-change: mark pending when settings changed.
            // (We'll use this later in the rebalance recommendation logic.)
            row.PendingChangesAtUtc = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);

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
}
