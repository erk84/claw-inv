namespace ClawInv.Core;

public sealed record Metrics(
    DateOnly Start,
    DateOnly End,
    int Days,
    decimal Cagr,
    decimal Volatility,
    decimal? Sharpe,
    decimal MaxDrawdown
);
