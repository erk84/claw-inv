using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class EqualWeightBuyHoldBacktester
{
    public static (BacktestResult result, IReadOnlyList<PortfolioPoint> curve) Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to,
        decimal initialCapital = 100_000m)
    {
        IReadOnlyList<string> Choose(DateOnly _, IReadOnlyList<NavSeries> ss)
        {
            // Equal weight across ALL funds once, then effectively rebalance at chosen interval.
            return ss.Select(x => x.OrderbookId).ToList();
        }

        return RebalanceEngine.Run(strat, series, from, to, Choose, initialCapital);
    }
}
