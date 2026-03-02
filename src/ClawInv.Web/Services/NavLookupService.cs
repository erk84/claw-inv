using ClawInv.Core.Backtest;

namespace ClawInv.Web.Services;

/// <summary>
/// Fast NAV lookup from the on-disk NavDataStore without loading the whole universe.
/// </summary>
public sealed class NavLookupService(IConfiguration cfg)
{
    private readonly NavDataStore _store = new(cfg["ClawInv:NavStoreDir"] ?? "data/nav");

    public decimal? TryGetNavAtOrBefore(string fundId, DateOnly date)
    {
        if (!_store.TryRead(fundId, out var nav) || nav.Count == 0)
            return null;

        var p = nav
            .Where(p => p.Date <= date)
            .OrderByDescending(p => p.Date)
            .FirstOrDefault();

        return p is null ? null : p.Nav;
    }

    public decimal? TryGetLatestNav(string fundId)
    {
        if (!_store.TryRead(fundId, out var nav) || nav.Count == 0)
            return null;

        return nav.OrderByDescending(p => p.Date).First().Nav;
    }
}
