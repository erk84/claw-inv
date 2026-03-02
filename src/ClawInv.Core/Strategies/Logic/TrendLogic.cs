using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class TrendLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.TrendFollowing;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Simple trend: pick top momentum among funds whose NAV is above MA.
        // This mirrors earlier usage where moving average acted as a trend gate.
        var scored = new List<(string id, double mom)>();

        foreach (var s in series)
        {
            var endNav = StrategyNavHelpers.NavAtOrBefore(fundIndex, s.OrderbookId, asOf);
            if (endNav is null) continue;

            var maStart = asOf.AddMonths(-Math.Max(1, strat.MovingAverageMonths));
            var maNav = StrategyNavHelpers.NavAtOrBefore(fundIndex, s.OrderbookId, maStart);
            if (maNav is null || maNav <= 0m) continue;

            var above = endNav.Value >= maNav.Value;
            if (!above)
                continue;

            var r = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, asOf, strat.LookbackMonths);
            if (r is null || double.IsNaN(r.Value))
                continue;

            if (strat.UseAbsoluteMomentumFilter && r.Value <= 0)
                continue;

            scored.Add((s.OrderbookId, r.Value));
        }

        var chosen = scored
            .OrderByDescending(x => x.mom)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }
}
