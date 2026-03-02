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
        var logic = StrategyLogicRegistry.Get(strat.Type);
        return logic.SelectHoldings(strat, series, fundIndex, asOf);
    }
}
