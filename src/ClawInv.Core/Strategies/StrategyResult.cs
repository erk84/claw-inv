using ClawInv.Core.Backtest;

namespace ClawInv.Core.Strategies;

public sealed record StrategyResult(
    StrategyDefinition Strategy,
    BacktestResult Result
);
