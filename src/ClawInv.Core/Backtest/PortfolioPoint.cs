namespace ClawInv.Core.Backtest;

public sealed record PortfolioPoint(DateOnly Date, decimal Equity, string Holding);
