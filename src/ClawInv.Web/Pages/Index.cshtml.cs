using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class IndexModel(AppDbContext db, NavLookupService nav) : PageModel
{
    public List<StrategyConfig> EnabledStrategies { get; private set; } = new();

    public sealed record StrategyCardVm(
        int StrategyId,
        string DisplayName,
        string Kind,
        int Slots,
        decimal SimulatedBalance,
        DateOnly? LatestAsOf,
        int ActiveHoldingsCount,
        decimal? ActiveHoldingsPerfAvgPct);

    public List<StrategyCardVm> Cards { get; private set; } = new();

    // Reused by Strategy details page
    public sealed record HoldingVm(
        string FundId,
        string FundName,
        DateOnly BuyDate,
        decimal BuyNav,
        decimal? LatestNav,
        decimal? PerfPct
    );

    public sealed record TradeRoundTripVm(
        string FundId,
        string FundName,
        DateOnly BuyDate,
        DateOnly? SellDate,
        decimal? PerfPct);

    public sealed record SnapshotVm(DateOnly Date, double PerfPct, double EquityIndex);

    public sealed record RecommendationTradeVm(
        RecommendationAction Action,
        string FundName,
        string Reason,
        decimal? InAmount,
        decimal? OutAmount);

    public sealed record RecommendationVm(
        DateOnly AsOfDate,
        List<RecommendationTradeVm> Trades);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var cutoff = asOf.AddYears(-10);

        EnabledStrategies = await db.StrategyConfigs
            .Where(x => x.Enabled)
            .OrderBy(x => x.DisplayName)
            .ToListAsync(ct);

        var ids = EnabledStrategies.Select(x => x.Id).ToList();

        // Latest runs (load then order client-side due to SQLite DateTimeOffset limitations)
        var runs = (await db.RecommendationRuns
                .Where(r => ids.Contains(r.StrategyConfigId))
                .ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();

        var latestAsOfByStrategyId = runs
            .GroupBy(r => r.StrategyConfigId)
            .ToDictionary(g => g.Key, g => (DateOnly?)g.First().AsOfDate);

        // Resolve portfolios for enabled strategies
        var portfolios = await db.Portfolios
            .Where(p => ids.Contains(p.StrategyConfigId))
            .ToListAsync(ct);

        var portfolioByStrategyId = portfolios.ToDictionary(p => p.StrategyConfigId, p => p);
        var pids = portfolios.Select(p => p.Id).ToList();

        // Active holdings for small summary
        var holdings = await db.PortfolioHoldings
            .Where(h => pids.Contains(h.PortfolioId) && h.SellDate == null)
            .ToListAsync(ct);

        // Snapshots for simulated balance (use latest point)
        var snaps = await db.PortfolioDailySnapshots
            .Where(s => pids.Contains(s.PortfolioId) && s.Date >= cutoff)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        foreach (var s in EnabledStrategies)
        {
            if (!portfolioByStrategyId.TryGetValue(s.Id, out var p))
                continue;

            var stratSnaps = snaps.Where(x => x.PortfolioId == p.Id).ToList();
            var last = stratSnaps.LastOrDefault();
            var equityIndex = last?.EquityIndex ?? 1.0;
            var balance = 100_000m * (decimal)equityIndex;

            var active = holdings.Where(h => h.PortfolioId == p.Id).ToList();
            decimal? avgPerf = null;
            var perfs = active
                .Select(h =>
                {
                    var latestNav = nav.TryGetLatestNav(h.FundId);
                    var buyNav = h.BuyNav > 0m ? h.BuyNav : (nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate) ?? 0m);
                    if (latestNav.HasValue && buyNav > 0m)
                        return (decimal?)((latestNav.Value / buyNav - 1m) * 100m);
                    return null;
                })
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .ToList();

            if (perfs.Count > 0)
                avgPerf = perfs.Average();

            Cards.Add(new StrategyCardVm(
                s.Id,
                s.DisplayName,
                s.Kind.ToString(),
                s.Slots,
                balance,
                latestAsOfByStrategyId.GetValueOrDefault(s.Id),
                active.Count,
                avgPerf));
        }

        Cards = Cards.OrderByDescending(x => x.SimulatedBalance).ToList();
    }
}
