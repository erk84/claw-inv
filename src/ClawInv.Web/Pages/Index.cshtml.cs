using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class IndexModel(AppDbContext db, NavLookupService nav) : PageModel
{
    public List<StrategyConfig> EnabledStrategies { get; private set; } = new();

    public Dictionary<int, RecommendationVm> LatestRunByStrategyId { get; private set; } = new();

    public Dictionary<int, decimal> SimulatedBalanceByStrategyId { get; private set; } = new();

    public Dictionary<int, List<HoldingVm>> ActiveHoldingsByStrategyId { get; private set; } = new();

    public Dictionary<int, List<TradeRoundTripVm>> RecentTradesByStrategyId { get; private set; } = new();

    public Dictionary<int, List<SnapshotVm>> SnapshotsByStrategyId { get; private set; } = new();

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

        // SQLite provider does not support ordering by DateTimeOffset (NotSupportedException).
        // Load then order client-side.
        var runs = (await db.RecommendationRuns
                .Include(r => r.Trades)
                .Where(r => ids.Contains(r.StrategyConfigId))
                .ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();

        // Latest run per strategy (we map to a VM later, once we have snapshots/balances).
        var latestRunEntityByStrategyId = runs
            .GroupBy(r => r.StrategyConfigId)
            .ToDictionary(g => g.Key, g => g.First());

        // Resolve portfolios for enabled strategies
        var portfolios = await db.Portfolios
            .Where(p => ids.Contains(p.StrategyConfigId))
            .ToListAsync(ct);

        var portfolioByStrategyId = portfolios.ToDictionary(p => p.StrategyConfigId, p => p);

        // Active holdings
        var pids = portfolios.Select(p => p.Id).ToList();
        var holdings = await db.PortfolioHoldings
            .Where(h => pids.Contains(h.PortfolioId) && h.SellDate == null)
            .OrderBy(h => h.FundName)
            .ToListAsync(ct);

        foreach (var s in EnabledStrategies)
        {
            if (!portfolioByStrategyId.TryGetValue(s.Id, out var p))
            {
                ActiveHoldingsByStrategyId[s.Id] = new();
                RecentTradesByStrategyId[s.Id] = new();
                continue;
            }

            var active = holdings.Where(h => h.PortfolioId == p.Id).ToList();
            ActiveHoldingsByStrategyId[s.Id] = active
                .Select(h =>
                {
                    // For "current" performance, use latest NAV in store (not as-of yesterday).
                    var latestNav = nav.TryGetLatestNav(h.FundId);

                    // Some historical rows may have BuyNav=0 (older data). Fallback to nav lookup at buy date.
                    var buyNav = h.BuyNav > 0m ? h.BuyNav : (nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate) ?? 0m);

                    decimal? perf = null;
                    if (latestNav.HasValue && buyNav > 0m)
                        perf = (latestNav.Value / buyNav - 1m) * 100m;

                    return new HoldingVm(h.FundId, h.FundName, h.BuyDate, buyNav, latestNav, perf);
                })
                .OrderBy(x => x.FundName)
                .ToList();
        }

        // Trade history: show one row per holding (buy->sell) with percent performance.
        var recentHoldings = await db.PortfolioHoldings
            .Where(h => pids.Contains(h.PortfolioId) && h.BuyDate >= cutoff)
            .OrderByDescending(h => h.BuyDate)
            .ToListAsync(ct);

        foreach (var s in EnabledStrategies)
        {
            if (!portfolioByStrategyId.TryGetValue(s.Id, out var p))
                continue;

            var rows = recentHoldings
                .Where(h => h.PortfolioId == p.Id)
                .Take(200)
                .Select(h =>
                {
                    // Always compute performance from NAV store to avoid stale/zero stored NAV fields.
                    // Buy NAV: at-or-before BuyDate
                    var buyNav = nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate);

                    decimal? perf = null;
                    if (buyNav is not null && buyNav.Value > 0m)
                    {
                        if (h.SellDate is not null)
                        {
                            // Sell NAV: at-or-before SellDate
                            var sellNav = nav.TryGetNavAtOrBefore(h.FundId, h.SellDate.Value);
                            if (sellNav is not null && sellNav.Value > 0m)
                                perf = (sellNav.Value / buyNav.Value - 1m) * 100m;
                        }
                        else
                        {
                            // Open position: use latest NAV
                            var latestNav = nav.TryGetLatestNav(h.FundId);
                            if (latestNav is not null && latestNav.Value > 0m)
                                perf = (latestNav.Value / buyNav.Value - 1m) * 100m;
                        }
                    }

                    return new TradeRoundTripVm(h.FundId, h.FundName, h.BuyDate, h.SellDate, perf);
                })
                .ToList();

            RecentTradesByStrategyId[s.Id] = rows;
        }

        // Snapshots last 5y (for chart)
        var snaps = await db.PortfolioDailySnapshots
            .Where(s => pids.Contains(s.PortfolioId) && s.Date >= cutoff)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        foreach (var s in EnabledStrategies)
        {
            if (!portfolioByStrategyId.TryGetValue(s.Id, out var p))
                continue;

            var list = snaps
                .Where(x => x.PortfolioId == p.Id)
                .Select(x => new SnapshotVm(x.Date, (x.EquityIndex - 1.0) * 100.0, x.EquityIndex))
                .ToList();

            SnapshotsByStrategyId[s.Id] = list;

            var last = list.LastOrDefault();
            var equityIndex = last?.EquityIndex ?? 1.0;
            SimulatedBalanceByStrategyId[s.Id] = 100_000m * (decimal)equityIndex;
        }

        // Map latest run into VM + compute simulated cash in/out per trade.
        foreach (var s in EnabledStrategies)
        {
            if (!latestRunEntityByStrategyId.TryGetValue(s.Id, out var run))
                continue;

            // Use equity as-of the run date, if we have it. Otherwise assume start=100k.
            var eqAt = SnapshotsByStrategyId.GetValueOrDefault(s.Id)
                ?.Where(x => x.Date <= run.AsOfDate)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault()?.EquityIndex ?? 1.0;

            var balance = 100_000m * (decimal)eqAt;
            var perSlot = s.Slots > 0 ? balance / s.Slots : balance;

            var tradeVms = run.Trades.Select(t =>
            {
                decimal? inAmt = null;
                decimal? outAmt = null;

                if (t.Action == RecommendationAction.Buy)
                    inAmt = perSlot;
                else if (t.Action == RecommendationAction.Sell)
                    outAmt = perSlot;

                return new RecommendationTradeVm(t.Action, t.FundName, t.Reason, inAmt, outAmt);
            }).ToList();

            LatestRunByStrategyId[s.Id] = new RecommendationVm(run.AsOfDate, tradeVms);
        }
    }
}
