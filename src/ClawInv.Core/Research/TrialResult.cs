namespace ClawInv.Core.Research;

public sealed record TrialResult(
    TrialParams Params,
    double Sharpe,
    double Cagr,
    double MaxDrawdown,
    double Score
);
