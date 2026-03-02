using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

internal sealed class SharpeProxyLogic : IStrategyLogic
{
    public StrategyType Type => StrategyType.SharpeProxy;

    public IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        var months = Math.Max(6, strat.LookbackMonths);
        var scored = new List<(string id, double s)>();

        foreach (var s in series)
        {
            if (!fundIndex.TryGetValue(s.OrderbookId, out var pts))
                continue;

            var vals = new List<double>();
            for (var i = months; i >= 1; i--)
            {
                var r = StrategyNavHelpers.MonthlyReturn(fundIndex, s.OrderbookId, asOf.AddMonths(-i + 1), 1);
                if (r is not null && !double.IsNaN(r.Value))
                    vals.Add(r.Value);
            }

            if (vals.Count < Math.Max(4, months / 2))
                continue;

            var mean = vals.Average();
            var varSum = vals.Sum(x => (x - mean) * (x - mean));
            var stdev = Math.Sqrt(varSum / (vals.Count - 1));
            if (stdev <= 0) continue;

            scored.Add((s.OrderbookId, mean / stdev));
        }

        if (scored.Count == 0)
            return new Dictionary<string, decimal>();

        scored.Sort((a, b) => b.s.CompareTo(a.s));
        var k = Math.Max(1, Math.Min(strat.TopK, scored.Count));
        var chosen = scored.Take(k).Select(x => x.id).ToArray();

        var w = 1.0m / chosen.Length;
        return chosen.ToDictionary(x => x, _ => w);
    }
}
