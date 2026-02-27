namespace ClawInv.Core.Strategies;

public static class BestStrategies
{
    /// <summary>
    /// Discovered via 100k-trial random search (research mode) on 100-fund universe, 10y.
    ///
    /// Summary (research backtest, month-end eval):
    /// - Sharpe ~1.48
    /// - CAGR ~19.33%
    /// - Max drawdown ~-15.76%
    ///
    /// Components:
    /// - Relative momentum lookback 2 months
    /// - Rebalance every 2 months
    /// - Equal weight TopK=2
    /// - Absolute momentum filter (risk-off to CASH when best momentum <= 0)
    /// - Low-vol filter using 3 month realized vol
    /// </summary>
    public static StrategyDefinition BestStrategyV1 => new(
        Id: "best_v1_mom2m_reb2m_top2_abs_lowvol3m",
        Name: "BestStrategyV1: Mom(2m) reb2m top2 abs + lowvol(3m)",
        Type: StrategyType.BestStrategyV1MonthEnd,
        RebalanceEveryMonths: 2,
        LookbackMonths: 2,
        TopK: 2,
        Allocation: AllocationMode.EqualWeightTopK,
        UseAbsoluteMomentumFilter: true,
        MovingAverageMonths: 0,
        VolatilityLookbackMonths: 3,
        UseLowVolFilter: true
    );
}
