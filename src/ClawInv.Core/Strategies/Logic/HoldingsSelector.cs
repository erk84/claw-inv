using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

public static class HoldingsSelector
{
    public static IReadOnlyDictionary<string, decimal> Select(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly asOf)
    {
        var fundIndex = series.ToDictionary(s => s.OrderbookId, s => s.Points.OrderBy(p => p.Date).ToArray());
        return Select(strat, series, fundIndex, asOf);
    }

    public static IReadOnlyDictionary<string, decimal> Select(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf)
    {
        // Regime / risk-off gate (shared across all strategies)
        if (!RegimeRiskOff.IsRiskOn(strat, series, fundIndex, asOf))
        {
            if (strat.RiskOffMode == RiskOffMode.DefensiveFund)
            {
                var def = RegimeRiskOff.SelectDefensiveFund(series, fundIndex, asOf, strat.DefensiveVolLookbackMonths);
                return string.IsNullOrEmpty(def) ? new Dictionary<string, decimal>() : EqualWeight([def]);
            }

            return new Dictionary<string, decimal>(); // CASH
        }

        var logic = StrategyLogicRegistry.Get(strat.Type);
        return logic.SelectHoldings(strat, series, fundIndex, asOf);
    }

    private static IReadOnlyDictionary<string, decimal> EqualWeight(string[] ids)
    {
        if (ids.Length == 0) return new Dictionary<string, decimal>();
        var distinct = ids.Distinct().ToArray();
        var w = 1.0m / distinct.Length;
        return distinct.ToDictionary(x => x, _ => w);
    }
}
