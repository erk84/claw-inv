using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class MeanReversionLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.MeanReversionRotation;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Mean reversion: pick worst recent performers (1M), with optional trend gate via MA.
        var scored = new List<(string id, double r1m)>();

        foreach (var s in series)
        {
            if (strat.MovingAverageMonths > 0)
            {
                var endNav = StrategyNavHelpers.NavAtOrBefore(fundIndex, s.OrderbookId, asOf);
                var maNav = StrategyNavHelpers.NavAtOrBefore(fundIndex, s.OrderbookId, asOf.AddMonths(-Math.Max(1, strat.MovingAverageMonths)));
                if (endNav is null || maNav is null || maNav <= 0m) continue;
                if (endNav.Value < maNav.Value) continue;
            }

            var r = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, asOf, 1);
            if (r is null || double.IsNaN(r.Value))
                continue;

            scored.Add((s.OrderbookId, r.Value));
        }

        var chosen = scored
            .OrderBy(x => x.r1m)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }
}
