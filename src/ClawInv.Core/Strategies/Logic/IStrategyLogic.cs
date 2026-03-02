using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies.Logic;

public interface IStrategyLogic
{
    StrategyType Type { get; }

    /// <summary>
    /// Select target holdings (fund orderbookIds) for a strategy as-of a given date.
    /// </summary>
    IReadOnlyDictionary<string, decimal> SelectHoldings(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        IReadOnlyDictionary<string, NavPoint[]> fundIndex,
        DateOnly asOf);
}
