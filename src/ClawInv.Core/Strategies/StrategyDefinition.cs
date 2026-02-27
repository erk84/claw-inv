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

    // momentum knobs
    int LookbackMonths,
    int TopK,
    AllocationMode Allocation,
    bool UseAbsoluteMomentumFilter,

    // trend knobs
    int MovingAverageMonths,

    // risk knobs
    int VolatilityLookbackMonths,
    bool UseLowVolFilter
);
