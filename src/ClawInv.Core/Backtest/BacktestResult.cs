namespace ClawInv.Core.Backtest;

public sealed record BacktestResult(
    string StrategyId,
    string StrategyName,
    DateOnly Start,
    DateOnly End,
    int Days,
    decimal Cagr,
    decimal Volatility,
    decimal? Sharpe,
    decimal MaxDrawdown,
    decimal TotalReturn,
    string Notes
);
