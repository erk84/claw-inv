using ClawInv.Core;
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

        // Files are written sorted by date; use binary search and return null if no point qualifies.
        var pts = nav as IList<NavPoint> ?? nav.ToList();
        var lo = 0;
        var hi = pts.Count - 1;
        var best = -1;

        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (pts[mid].Date <= date)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best >= 0 ? pts[best].Nav : null;
    }

    public decimal? TryGetLatestNav(string fundId)
    {
        if (!_store.TryRead(fundId, out var nav) || nav.Count == 0)
            return null;

        // Nav files are sorted ascending.
        return nav[^1].Nav;
    }
}
