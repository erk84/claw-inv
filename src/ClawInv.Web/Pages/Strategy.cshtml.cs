using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class StrategyModel(AppDbContext db, NavLookupService nav) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public StrategyConfig? Strategy { get; private set; }

    public decimal SimulatedBalance { get; private set; } = 100_000m;

    public List<IndexModel.HoldingVm> ActiveHoldings { get; private set; } = new();

    public List<IndexModel.TradeRoundTripVm> RecentTrades { get; private set; } = new();

    public List<IndexModel.SnapshotVm> Snapshots { get; private set; } = new();

    public IndexModel.RecommendationVm? LatestRun { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Strategy = await db.StrategyConfigs.FirstOrDefaultAsync(x => x.Id == Id, ct);
        if (Strategy is null)
            return;

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var cutoff = asOf.AddYears(-10);

        var portfolio = await db.Portfolios.FirstOrDefaultAsync(p => p.StrategyConfigId == Strategy.Id, ct);
        if (portfolio is null)
            return;

        // Active holdings
        var holdings = await db.PortfolioHoldings
            .Where(h => h.PortfolioId == portfolio.Id && h.SellDate == null)
            .OrderBy(h => h.FundName)
            .ToListAsync(ct);

        ActiveHoldings = holdings
            .Select(h =>
            {
                var latestNav = nav.TryGetLatestNav(h.FundId);
                var buyNav = h.BuyNav > 0m ? h.BuyNav : (nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate) ?? 0m);

                decimal? perf = null;
                if (latestNav.HasValue && buyNav > 0m)
                    perf = (latestNav.Value / buyNav - 1m) * 100m;

                return new IndexModel.HoldingVm(h.FundId, h.FundName, h.BuyDate, buyNav, latestNav, perf);
            })
            .OrderBy(x => x.FundName)
            .ToList();

        // Trade history (10y)
        var recentHoldings = await db.PortfolioHoldings
            .Where(h => h.PortfolioId == portfolio.Id && h.BuyDate >= cutoff)
            .OrderByDescending(h => h.BuyDate)
            .Take(500)
            .ToListAsync(ct);

        RecentTrades = recentHoldings
            .Select(h =>
            {
                var buyNav = nav.TryGetNavAtOrBefore(h.FundId, h.BuyDate);

                decimal? perf = null;
                if (buyNav is not null && buyNav.Value > 0m)
                {
                    if (h.SellDate is not null)
                    {
                        var sellNav = nav.TryGetNavAtOrBefore(h.FundId, h.SellDate.Value);
                        if (sellNav is not null && sellNav.Value > 0m)
                            perf = (sellNav.Value / buyNav.Value - 1m) * 100m;
                    }
                    else
                    {
                        var latestNav = nav.TryGetLatestNav(h.FundId);
                        if (latestNav is not null && latestNav.Value > 0m)
                            perf = (latestNav.Value / buyNav.Value - 1m) * 100m;
                    }
                }

                return new IndexModel.TradeRoundTripVm(h.FundId, h.FundName, h.BuyDate, h.SellDate, perf);
            })
            .ToList();

        // Snapshots (10y)
        var snaps = await db.PortfolioDailySnapshots
            .Where(s => s.PortfolioId == portfolio.Id && s.Date >= cutoff)
            .OrderBy(s => s.Date)
            .ToListAsync(ct);

        Snapshots = snaps.Select(x => new IndexModel.SnapshotVm(x.Date, (x.EquityIndex - 1.0) * 100.0, x.EquityIndex)).ToList();

        var last = Snapshots.LastOrDefault();
        SimulatedBalance = 100_000m * (decimal)(last?.EquityIndex ?? 1.0);

        // Latest recommendation run + simulate cash in/out
        var run = (await db.RecommendationRuns
                .Include(r => r.Trades)
                .Where(r => r.StrategyConfigId == Strategy.Id)
                .ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault();

        if (run is not null)
        {
            var eqAt = Snapshots
                .Where(x => x.Date <= run.AsOfDate)
                .OrderByDescending(x => x.Date)
                .FirstOrDefault()?.EquityIndex ?? 1.0;

            var balanceAt = 100_000m * (decimal)eqAt;
            var perSlot = Strategy.Slots > 0 ? balanceAt / Strategy.Slots : balanceAt;

            var tradeVms = run.Trades.Select(t =>
            {
                decimal? inAmt = null;
                decimal? outAmt = null;

                if (t.Action == RecommendationAction.Buy)
                    inAmt = perSlot;
                else if (t.Action == RecommendationAction.Sell)
                    outAmt = perSlot;

                return new IndexModel.RecommendationTradeVm(t.Action, t.FundName, t.Reason, inAmt, outAmt);
            }).ToList();

            LatestRun = new IndexModel.RecommendationVm(run.AsOfDate, tradeVms);
        }
    }
}
