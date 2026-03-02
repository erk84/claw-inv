using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class LowVolLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.LowVolatilitySelection;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Approximate vol with monthly return stdev over VolatilityLookbackMonths.
        // (Uses sparse monthly points via nav-at-or-before.)
        var lb = Math.Max(2, strat.VolatilityLookbackMonths);
        var vols = new List<(string id, double vol)>();

        foreach (var s in series)
        {
            var returns = new List<double>();
            for (var i = lb; i >= 1; i--)
            {
                var d = asOf.AddMonths(-i + 1);
                var r = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, d, 1);
                if (r is not null && !double.IsNaN(r.Value))
                    returns.Add(r.Value);
            }

            if (returns.Count < 2)
                continue;

            var mean = returns.Average();
            var varSum = returns.Sum(x => (x - mean) * (x - mean));
            var variance = varSum / (returns.Count - 1);
            var vol = Math.Sqrt(variance) * Math.Sqrt(12.0);
            vols.Add((s.OrderbookId, vol));
        }

        var chosen = vols
            .OrderBy(x => x.vol)
            .Take(Math.Max(1, strat.TopK))
            .Select(x => x.id)
            .ToArray();

        if (chosen.Length == 0)
            return new Dictionary<string, decimal>();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }
}
