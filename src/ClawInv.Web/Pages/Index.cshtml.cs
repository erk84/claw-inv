using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class IndexModel(AppDbContext db) : PageModel
{
    public List<StrategyConfig> EnabledStrategies { get; private set; } = new();

    public Dictionary<int, RecommendationRun> LatestRunByStrategyId { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        EnabledStrategies = await db.StrategyConfigs
            .Where(x => x.Enabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);

        var ids = EnabledStrategies.Select(x => x.Id).ToList();

        var runs = await db.RecommendationRuns
            .Include(r => r.Trades)
            .Where(r => ids.Contains(r.StrategyConfigId))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        LatestRunByStrategyId = runs
            .GroupBy(r => r.StrategyConfigId)
            .ToDictionary(g => g.Key, g => g.First());
    }
}
