using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

/// <summary>
/// Builds/refreshes daily portfolio equity snapshots (indexed, percent-chart friendly).
/// (We keep 10 years to align with research/backtest horizon.)
/// Uses the same model-portfolio assumptions as recommendations:
/// - equal-weight holdings
/// - rebalance only when TradeEvents occur (created by RecommendationEngine)
/// - NAV via NavLookupService (nav-at-or-before)
/// </summary>
public sealed class SnapshotEngine(
    AppDbContext db,
    NavLookupService nav,
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

        var trades = await db.TradeEvents
            .Where(t => t.PortfolioId == portfolio.Id && t.Date >= from && t.Date <= asOf)
            .OrderBy(t => t.Date)
            .ToListAsync(ct);

        var snapshots = BuildSnapshots(portfolio, trades, from, asOf);
        db.PortfolioDailySnapshots.AddRange(snapshots);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Snapshots rebuilt: strategy={Key} days={Count} from={From} to={To}", strat.Key, snapshots.Count, from, asOf);
    }

    private List<PortfolioDailySnapshot> BuildSnapshots(Portfolio portfolio, List<TradeEvent> trades, DateOnly from, DateOnly to)
    {
        var allDates = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToArray();

        // fundId -> shares
        var shares = new Dictionary<string, double>(StringComparer.Ordinal);

        // Try bootstrap from existing holdings if we have no trades in the window.
        if (trades.Count == 0 && portfolio.Holdings.Count > 0)
        {
            var start = from;
            var active = portfolio.Holdings
                .Where(h => h.BuyDate <= start && (h.SellDate is null || h.SellDate > start))
                .Select(h => h.FundId)
                .Distinct()
                .ToArray();

            if (active.Length > 0)
                RebalanceEqualWeight(shares, active, equityValue: 1.0, date: start);
        }

        var snapshots = new List<PortfolioDailySnapshot>(allDates.Length);

        var tradeGroups = trades
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var d in allDates)
        {
            var equity = ComputeEquityValue(shares, d);

            // Rebalance on trade dates: after applying trade events, reallocate equal-weight.
            if (tradeGroups.TryGetValue(d, out var dayTrades))
            {
                // Apply sells then buys to determine the new target set.
                foreach (var t in dayTrades.Where(x => x.Side == TradeSide.Sell))
                    shares.Remove(t.FundId);

                foreach (var t in dayTrades.Where(x => x.Side == TradeSide.Buy))
                    if (!shares.ContainsKey(t.FundId))
                        shares[t.FundId] = 0.0;

                var target = shares.Keys.OrderBy(x => x).ToArray();
                if (target.Length > 0)
                    RebalanceEqualWeight(shares, target, equity, d);
            }

            // equity index is relative to initial value=1.0 (because we allocate from 1.0).
            snapshots.Add(new PortfolioDailySnapshot
            {
                PortfolioId = portfolio.Id,
                Date = d,
                EquityIndex = equity
            });
        }

        return snapshots;
    }

    private double ComputeEquityValue(Dictionary<string, double> shares, DateOnly date)
    {
        if (shares.Count == 0)
            return 1.0;

        double sum = 0.0;
        foreach (var (fundId, sh) in shares)
        {
            var navAt = nav.TryGetNavAtOrBefore(fundId, date);
            if (navAt is null || navAt <= 0m)
                continue;

            sum += sh * (double)navAt.Value;
        }

        // If everything is missing, keep last value-ish by returning 1.
        return sum > 0.0 ? sum : 1.0;
    }

    private void RebalanceEqualWeight(Dictionary<string, double> shares, string[] targetIds, double equityValue, DateOnly date)
    {
        if (targetIds.Length == 0)
            return;

        var perFund = equityValue / targetIds.Length;

        foreach (var id in targetIds)
        {
            var navAt = nav.TryGetNavAtOrBefore(id, date);
            if (navAt is null || navAt <= 0m)
            {
                shares[id] = 0.0;
                continue;
            }

            shares[id] = perFund / (double)navAt.Value;
        }
    }
}
