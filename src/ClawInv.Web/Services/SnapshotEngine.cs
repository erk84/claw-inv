using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

/// <summary>
/// Builds/refreshes daily portfolio equity snapshots (indexed, percent-chart friendly).
/// (We keep 10 years to align with research/backtest horizon.)
/// Snapshots must match CLI backtests exactly.
/// We therefore reuse the core backtest engine (MonthEndRebalanceDailyBacktester)
/// to produce the daily equity curve.
/// </summary>
public sealed class SnapshotEngine(
    AppDbContext db,
    NavService navService,
    ILogger<SnapshotEngine> log)
{
    public Task RebuildLast5YearsAsync(int strategyConfigId, DateOnly asOf, CancellationToken ct)
        => RebuildLast10YearsAsync(strategyConfigId, asOf, ct);

    public async Task RebuildLast10YearsAsync(int strategyConfigId, DateOnly asOf, CancellationToken ct)
    {
        var strat = await db.StrategyConfigs.FirstOrDefaultAsync(x => x.Id == strategyConfigId, ct);
        if (strat is null)
            return;

        var portfolio = await db.Portfolios
            .Include(p => p.Holdings)
            .FirstOrDefaultAsync(p => p.StrategyConfigId == strategyConfigId, ct);

        if (portfolio is null)
        {
            portfolio = new Portfolio
            {
                StrategyConfigId = strategyConfigId,
                StartDate = asOf.AddYears(-10)
            };
            db.Portfolios.Add(portfolio);
            await db.SaveChangesAsync(ct);
        }

        var from = asOf.AddYears(-10);
        if (portfolio.StartDate > from)
            from = portfolio.StartDate;

        // Delete and rebuild the window. Simple + robust; small DB size.
        await db.PortfolioDailySnapshots
            .Where(s => s.PortfolioId == portfolio.Id && s.Date >= from && s.Date <= asOf)
            .ExecuteDeleteAsync(ct);

        // Load NAV for the same window using the same store as the rest of the web app.
        var navSeries = await navService.LoadUniverseNavAsync(from, asOf, ct);

        var def = StrategyMapper.ToStrategyDefinition(strat);
        var (r, curve) = ClawInv.Core.Backtest.MonthEndRebalanceDailyBacktester.RunWithEquityCurve(def, navSeries, from, asOf);

        var snapshots = curve
            .Where(x => x.Date >= from && x.Date <= asOf)
            .Select(x => new PortfolioDailySnapshot { PortfolioId = portfolio.Id, Date = x.Date, EquityIndex = (double)x.EquityIndex })
            .ToList();

        db.PortfolioDailySnapshots.AddRange(snapshots);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Snapshots rebuilt (core backtest): strategy={Key} days={Count} from={From} to={To} equity={Eq:0.###}x",
            strat.Key, snapshots.Count, from, asOf, 1.0m + r.TotalReturn);
    }
}
