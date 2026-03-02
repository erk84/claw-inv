using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class MomentumLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.MomentumRotation;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        var scored = new List<(string id, double mom)>();

        // Impl #7: Dual momentum horizons (12-1 and 6-1 blended).
        var end = asOf.AddMonths(-1);
        var lbLong = Math.Max(3, strat.LookbackMonths);
        var lbShort = Math.Max(3, Math.Min(6, lbLong));

        foreach (var s in series)
        {
            var rLong = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, end, lbLong);
            var rShort = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, end, lbShort);
            if (rLong is null || rShort is null) continue;
            if (double.IsNaN(rLong.Value) || double.IsNaN(rShort.Value)) continue;

            var mom = 0.5 * rLong.Value + 0.5 * rShort.Value;

            if (strat.UseAbsoluteMomentumFilter && mom <= 0)
                continue;

            scored.Add((s.OrderbookId, mom));
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
