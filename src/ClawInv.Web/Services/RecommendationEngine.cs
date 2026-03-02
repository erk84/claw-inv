using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class RecommendationEngine(ILogger<RecommendationEngine> log, AppDbContext db, NavService nav)
{
    private static double? TryGetNav(IReadOnlyList<ClawInv.Core.Backtest.NavSeries> series, string fundId, DateOnly date)
    {
        var s = series.FirstOrDefault(x => x.OrderbookId == fundId);
        if (s is null) return null;
        var p = s.Points
            .Where(p => p.Date <= date)
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();
        return p is null ? null : (double)p.Nav;
    }

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

        // Load NAV with enough lookback to make selection.
        var lookbackMonths = Math.Max(24, strategy.LookbackMonths + 24);
        var from = asOfDate.AddMonths(-lookbackMonths);
        var series = await nav.LoadUniverseNavAsync(from, asOfDate, ct);

        var def = StrategyMapper.ToStrategyDefinition(strategy);
        var target = ClawInv.Core.Strategies.Logic.HoldingsSelector.Select(def, series, asOfDate);
        var targetIds = target.Keys.ToArray();

        // Current "model" holdings
        var current = await db.PortfolioHoldings
            .Where(h => h.PortfolioId == portfolio.Id && h.SellDate == null)
            .ToListAsync(ct);

        var currentIds = current.Select(x => x.FundId).ToHashSet();
        var targetSet = targetIds.ToHashSet();

        var trades = new List<TradeRecommendation>();

        // Sells
        foreach (var h in current.Where(h => !targetSet.Contains(h.FundId)))
        {
            trades.Add(new TradeRecommendation
            {
                Action = RecommendationAction.Sell,
                FundId = h.FundId,
                FundName = h.FundName,
                Reason = "Not in target holdings on rebalance"
            });

            h.SellDate = asOfDate;
            var sell = TryGetNav(series, h.FundId, asOfDate);
            h.SellNav = sell is null ? null : (decimal)sell.Value;

            db.TradeEvents.Add(new TradeEvent
            {
                PortfolioId = portfolio.Id,
                Date = asOfDate,
                FundId = h.FundId,
                FundName = h.FundName,
                Side = TradeSide.Sell,
                Nav = h.SellNav ?? 0m
            });
        }

        // Buys
        foreach (var id in targetIds.Where(id => !currentIds.Contains(id)))
        {
            var s = series.FirstOrDefault(x => x.OrderbookId == id);
            trades.Add(new TradeRecommendation
            {
                Action = RecommendationAction.Buy,
                FundId = id,
                FundName = s?.Name ?? id,
                Reason = "Selected by strategy on rebalance"
            });

            var buyNav = TryGetNav(series, id, asOfDate);

            db.PortfolioHoldings.Add(new PortfolioHolding
            {
                PortfolioId = portfolio.Id,
                FundId = id,
                FundName = s?.Name ?? id,
                BuyDate = asOfDate,
                BuyNav = (decimal)(buyNav ?? 0.0)
            });

            db.TradeEvents.Add(new TradeEvent
            {
                PortfolioId = portfolio.Id,
                Date = asOfDate,
                FundId = id,
                FundName = s?.Name ?? id,
                Side = TradeSide.Buy,
                Nav = (decimal)(buyNav ?? 0.0)
            });
        }

        var run = new RecommendationRun
        {
            StrategyConfigId = strategy.Id,
            AsOfDate = asOfDate,
            Notes = $"Rebalance due. Target={string.Join(",", targetIds)}",
            Trades = trades
        };

        db.RecommendationRuns.Add(run);

        portfolio.LastRebalanceDate = asOfDate;

        await db.SaveChangesAsync(ct);

        log.LogInformation("Created recommendation run for {Key} as-of {AsOf}: {Trades} trades", strategy.Key, asOfDate, trades.Count);
        return run;
    }
}
