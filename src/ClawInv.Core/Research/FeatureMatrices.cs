namespace ClawInv.Core.Research;

public sealed class FeatureMatrices
{
    public required DateOnly[] Dates { get; init; }
    public required string[] FundIds { get; init; }

    // [t,f]
    public required double[,] Nav { get; init; }
    public required double[,] Ret1M { get; init; }

    public required Dictionary<string, int> FundIndex { get; init; }
}
