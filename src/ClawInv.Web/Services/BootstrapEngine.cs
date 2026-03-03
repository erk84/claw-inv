using ClawInv.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

/// <summary>
/// Backfills model portfolio history (TradeEvents + holdings) for the last 5 years.
/// Uses the same RecommendationEngine (and thus same core strategy logic) as live runs,
/// but drives it over historical month-end dates.
/// </summary>
public sealed class BootstrapEngine(
    ILogger<BootstrapEngine> log,
    AppDbContext db,
    RecommendationEngine rec,
    SnapshotEngine snapshots,
    NavService nav)
{
    public async Task BootstrapLast5YearsIfEmptyAsync(int strategyConfigId, DateOnly asOf, CancellationToken ct)
    {
        // If we already have a deep history, do not overwrite. But if we only have shallow history
        // (e.g. a couple of trades from the daily job), we must still bootstrap to make web match CLI backtests.
        var existingTrades = await db.TradeEvents
            .Where(t => t.Portfolio!.StrategyConfigId == strategyConfigId)
            .Select(t => t.Date)
            .ToListAsync(ct);

        if (existingTrades.Count > 0)
        {
            var minDate = existingTrades.Min();
            // Heuristic: if we have at least ~2 years of trades, or if the earliest trade reaches far back,
            // assume history is already bootstrapped.
            var deepEnough = minDate <= asOf.AddYears(-9) || minDate <= asOf.AddYears(-2);
            if (deepEnough)
            {
                log.LogInformation("Bootstrap skipped (already has history): strategyId={Id} minTrade={Min} trades={Count}", strategyConfigId, minDate, existingTrades.Count);
                return;
            }

            log.LogWarning("Bootstrap will overwrite shallow history: strategyId={Id} minTrade={Min} trades={Count}", strategyConfigId, minDate, existingTrades.Count);

            // Clear shallow history first.
            var portfolioIds = await db.Portfolios.Where(p => p.StrategyConfigId == strategyConfigId).Select(p => p.Id).ToListAsync(ct);
            if (portfolioIds.Count > 0)
            {
                await db.PortfolioDailySnapshots.Where(s => portfolioIds.Contains(s.PortfolioId)).ExecuteDeleteAsync(ct);
                await db.TradeEvents.Where(t => portfolioIds.Contains(t.PortfolioId)).ExecuteDeleteAsync(ct);
                await db.PortfolioHoldings.Where(h => portfolioIds.Contains(h.PortfolioId)).ExecuteDeleteAsync(ct);
                await db.Portfolios.Where(p => portfolioIds.Contains(p.Id)).ExecuteDeleteAsync(ct);
            }

            var runIds = await db.RecommendationRuns.Where(r => r.StrategyConfigId == strategyConfigId).Select(r => r.Id).ToListAsync(ct);
            if (runIds.Count > 0)
            {
                await db.TradeRecommendations.Where(t => runIds.Contains(t.RecommendationRunId)).ExecuteDeleteAsync(ct);
                await db.RecommendationRuns.Where(r => runIds.Contains(r.Id)).ExecuteDeleteAsync(ct);
            }

            await db.SaveChangesAsync(ct);
        }

        var from = asOf.AddYears(-10);

        // Ensure portfolio exists and anchors far enough back so due-check works.
        var portfolio = await db.Portfolios.SingleOrDefaultAsync(p => p.StrategyConfigId == strategyConfigId, ct);
        if (portfolio is null)
        {
            db.Portfolios.Add(new Data.Entities.Portfolio
            {
                StrategyConfigId = strategyConfigId,
                StartDate = from,
                LastRebalanceDate = null
            });
            await db.SaveChangesAsync(ct);
        }
        else
        {
            portfolio.StartDate = from;
            portfolio.LastRebalanceDate = null;
            await db.SaveChangesAsync(ct);
        }

        var monthEnds = GetMonthEnds(from, asOf);

        log.LogInformation("Bootstrap start: strategyId={Id} monthEnds={Count} from={From} to={To}", strategyConfigId, monthEnds.Count, from, asOf);

        // Preload NAV once for the whole bootstrap window.
        // Important: Avanza charts are "% since first datapoint" so we use a stable anchor.
        var navAnchorFrom = new DateOnly(2021, 1, 1);
        var preloadFrom = from < navAnchorFrom ? from : navAnchorFrom;
        var preloaded = await nav.LoadUniverseNavAsync(preloadFrom, asOf, ct);

        foreach (var d in monthEnds)
        {
            ct.ThrowIfCancellationRequested();
            // This will create RecommendationRun + TradeEvents/holdings only when due.
            await rec.ComputeIfDueWithPreloadedNavAsync(strategyConfigId, d, preloaded, ct);
        }

        await snapshots.RebuildLast10YearsAsync(strategyConfigId, asOf, ct);

        log.LogInformation("Bootstrap done: strategyId={Id}", strategyConfigId);
    }

    private static List<DateOnly> GetMonthEnds(DateOnly from, DateOnly to)
    {
        // Create a daily grid and pick last weekday per month.
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToArray();

        var res = new List<DateOnly>();
        DateOnly? last = null;
        foreach (var d in days)
        {
            if (last is null)
            {
                last = d;
                continue;
            }

            if (d.Month != last.Value.Month || d.Year != last.Value.Year)
            {
                res.Add(last.Value);
            }

            last = d;
        }

        if (last is not null)
            res.Add(last.Value);

        // Ensure within bounds
        return res.Where(d => d >= from && d <= to).Distinct().ToList();
    }
}
