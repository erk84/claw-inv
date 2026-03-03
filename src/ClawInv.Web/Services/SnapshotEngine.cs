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
        var cash = 1.0; // equity value when not invested

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
            {
                RebalanceEqualWeight(shares, ref cash, active, equityValue: cash, date: start);
            }
        }

        var snapshots = new List<PortfolioDailySnapshot>(allDates.Length);

        var tradeGroups = trades
            .GroupBy(t => t.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var d in allDates)
        {
            // Compute equity at start-of-day (pre-trades) to use as rebalance base.
            var equityPre = ComputeEquityValue(shares, cash, d);

            // Rebalance on trade dates: after applying trade events, reallocate equal-weight.
            if (tradeGroups.TryGetValue(d, out var dayTrades))
            {
                // Realize equity into cash before changing holdings.
                cash = equityPre;

                // Apply sells then buys to determine the new target set.
                foreach (var t in dayTrades.Where(x => x.Side == TradeSide.Sell))
                    shares.Remove(t.FundId);

                foreach (var t in dayTrades.Where(x => x.Side == TradeSide.Buy))
                    if (!shares.ContainsKey(t.FundId))
                        shares[t.FundId] = 0.0;

                var target = shares.Keys.OrderBy(x => x).ToArray();
                if (target.Length > 0)
                    RebalanceEqualWeight(shares, ref cash, target, equityPre, d);
                else
                    shares.Clear();
            }

            // Snapshot should reflect end-of-day holdings at date d (post-trade on rebalance days).
            var equity = ComputeEquityValue(shares, cash, d);

            snapshots.Add(new PortfolioDailySnapshot
            {
                PortfolioId = portfolio.Id,
                Date = d,
                EquityIndex = equity
            });
        }

        return snapshots;
    }

    private double ComputeEquityValue(Dictionary<string, double> shares, double cash, DateOnly date)
    {
        var sum = cash;

        foreach (var (fundId, sh) in shares)
        {
            if (sh <= 0) continue;
            var navAt = nav.TryGetNavAtOrBefore(fundId, date);
            if (navAt is null || navAt <= 0m)
                continue;

            sum += sh * (double)navAt.Value;
        }

        return sum;
    }

    private void RebalanceEqualWeight(Dictionary<string, double> shares, ref double cash, string[] targetIds, double equityValue, DateOnly date)
    {
        if (targetIds.Length == 0)
            return;

        var perFund = equityValue / targetIds.Length;
        cash = 0.0;

        foreach (var id in targetIds)
        {
            var navAt = nav.TryGetNavAtOrBefore(id, date);
            if (navAt is null || navAt <= 0m)
            {
                // If we can't price the fund at rebalance date, keep its portion as cash.
                shares[id] = 0.0;
                cash += perFund;
                continue;
            }

            shares[id] = perFund / (double)navAt.Value;
        }
    }
}
