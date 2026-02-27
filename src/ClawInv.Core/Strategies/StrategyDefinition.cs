namespace ClawInv.Core.Strategies;

public enum AllocationMode
{
    Top1,
    EqualWeightTopK
}

public sealed record StrategyDefinition(
    string Id,
    string Name,
    string Type,
    int LookbackMonths,
    int TopK,
    int RebalanceEveryMonths,
    AllocationMode Allocation
);
