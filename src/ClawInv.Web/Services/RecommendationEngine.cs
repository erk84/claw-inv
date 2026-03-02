using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class RecommendationEngine(ILogger<RecommendationEngine> log, AppDbContext db)
{
    public async Task<RecommendationRun?> ComputeIfDueAsync(int strategyConfigId, DateOnly asOfDate, CancellationToken ct)
    {
        var strategy = await db.StrategyConfigs.SingleAsync(x => x.Id == strategyConfigId, ct);

        // Ensure portfolio exists (1:1 per strategy for now).
        var portfolio = await db.Portfolios.SingleOrDefaultAsync(p => p.StrategyConfigId == strategy.Id, ct);
        if (portfolio is null)
        {
            // Use today as anchor. In the next step we will set this based on the first available NAV date.
            portfolio = new Portfolio
            {
                StrategyConfigId = strategy.Id,
                StartDate = asOfDate,
                LastRebalanceDate = null
            };
            db.Portfolios.Add(portfolio);
            await db.SaveChangesAsync(ct);
        }

        var anchor = portfolio.LastRebalanceDate ?? portfolio.StartDate;
        var due = RebalanceSchedule.IsRebalanceDue(asOfDate, anchor, strategy.RebalanceMonths);

        if (!due)
        {
            log.LogInformation("No rebalance due for {Key} as-of {AsOf}", strategy.Key, asOfDate);
            return null;
        }

        // TODO: plug in ClawInv.Core daily selection logic + compute diff vs current holdings.
        // For now we emit a run record indicating a rebalance would be evaluated.
        var run = new RecommendationRun
        {
            StrategyConfigId = strategy.Id,
            AsOfDate = asOfDate,
            Notes = "Rebalance due. TODO compute target holdings + trade diff.",
            Trades = new List<TradeRecommendation>()
        };

        db.RecommendationRuns.Add(run);

        // Mark rebalance as executed (recommendation produced). When we later implement "recommended trades"
        // vs "executed trades" we might want a separate field, but for now this drives scheduling.
        portfolio.LastRebalanceDate = asOfDate;

        await db.SaveChangesAsync(ct);

        log.LogInformation("Created rebalance recommendation run for {Key} as-of {AsOf}", strategy.Key, asOfDate);
        return run;
    }
}
