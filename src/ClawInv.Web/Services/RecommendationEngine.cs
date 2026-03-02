using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class RecommendationEngine(ILogger<RecommendationEngine> log, AppDbContext db)
{
    public async Task<RecommendationRun> ComputeAsync(int strategyConfigId, CancellationToken ct)
    {
        var strategy = await db.StrategyConfigs.SingleAsync(x => x.Id == strategyConfigId, ct);

        // NOTE: placeholder: we will plug in ClawInv.Core backtest/daily logic here.
        // For now we emit a run record so UI + scheduling works end-to-end.
        var run = new RecommendationRun
        {
            StrategyConfigId = strategy.Id,
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "TODO: compute based on daily backtest; placeholder run created",
            Trades = new List<TradeRecommendation>()
        };

        log.LogInformation("Computed placeholder recommendation for {Key}", strategy.Key);
        db.RecommendationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        return run;
    }
}
