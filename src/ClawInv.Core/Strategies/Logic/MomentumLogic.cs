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

        // Use "12-1" style momentum (skip most recent month) to reduce mean-reversion noise.
        // We interpret LookbackMonths as the lookback window length excluding the most recent month.
        var end = asOf.AddMonths(-1);

        foreach (var s in series)
        {
            var r = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, end, strat.LookbackMonths);
            if (r is null || double.IsNaN(r.Value))
                continue;

            // absolute momentum filter: reject negative momentum
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
