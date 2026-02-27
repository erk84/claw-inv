namespace ClawInv.Core.Research;

public sealed class FeatureMatrices
{
    public required DateOnly[] Dates { get; init; }
    public required string[] FundIds { get; init; }

    // [t,f]
    public required double[,] Nav { get; init; }
    public required double[,] Ret1M { get; init; }

    // Regime helpers
    public required double[] IndexNav { get; init; }          // equal-weight index NAV (normalized)
    public required double[] Breadth12 { get; init; }         // fraction of funds above 12m MA at t

    public required Dictionary<string, int> FundIndex { get; init; }
}
