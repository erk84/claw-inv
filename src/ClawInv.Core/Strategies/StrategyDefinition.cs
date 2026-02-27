using ClawInv.Core.Research;

namespace ClawInv.Core.Strategies;

public enum AllocationMode
{
    Top1,
    EqualWeightTopK
}

public sealed record StrategyDefinition(
    string Id,
    string Name,
    StrategyType Type,

    // generic knobs
    int RebalanceEveryMonths,

    // selection knobs
    int LookbackMonths,
    int TopK,
    AllocationMode Allocation,

    // momentum knobs
    bool UseAbsoluteMomentumFilter,

    // mean reversion / trend knobs
    int MovingAverageMonths,

    // risk knobs
    int VolatilityLookbackMonths,
    bool UseLowVolFilter,

    // regime + risk-off behavior
    RegimeKind Regime,
    int RegimeMaMonths,
    double RegimeThreshold,
    RiskOffMode RiskOffMode,
    int DefensiveVolLookbackMonths
);

