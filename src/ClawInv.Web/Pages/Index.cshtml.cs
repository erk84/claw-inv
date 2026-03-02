using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class IndexModel(AppDbContext db, NavLookupService nav) : PageModel
{
    public List<StrategyConfig> EnabledStrategies { get; private set; } = new();

    public Dictionary<int, RecommendationRun> LatestRunByStrategyId { get; private set; } = new();

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

    public sealed record SnapshotVm(DateOnly Date, double PerfPct);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var cutoff = asOf.AddYears(-5);

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

        LatestRunByStrategyId = runs
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
                    decimal? perf = null;

                    // Some historical rows may have BuyNav=0 (older data). Fallback to nav lookup at buy date.
                    var buyNav = h.BuyNav > 0m ? h.BuyNav : (nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate) ?? 0m);

                    if (buyNav > 0m)
                    {
                        if (h.SellDate is not null)
                        {
                            // Use sell NAV if available, otherwise fallback to nav lookup.
                            var sellNav = h.SellNav;
                            if (sellNav is null)
                            {
                                var ln = nav.TryGetNavAtOrBefore(h.FundId, h.SellDate.Value);
                                if (ln.HasValue) sellNav = ln.Value;
                            }

                            if (sellNav is not null && sellNav.Value > 0m)
                                perf = (sellNav.Value / buyNav - 1m) * 100m;
                        }
                        else
                        {
                            var latestNav = nav.TryGetLatestNav(h.FundId);
                            if (latestNav.HasValue && latestNav.Value > 0m)
                                perf = (latestNav.Value / buyNav - 1m) * 100m;
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

            SnapshotsByStrategyId[s.Id] = snaps
                .Where(x => x.PortfolioId == p.Id)
                .Select(x => new SnapshotVm(x.Date, (x.EquityIndex - 1.0) * 100.0))
                .ToList();
        }
    }
}
