namespace ClawInv.Core.Research;

public sealed record TrialParams(
    int LookbackMonths,
    int RebalanceMonths,
    int TopK,
    bool UseAbsoluteMomentum,
    int VolLookbackMonths,
    bool UseLowVolFilter,
    int TrendMaMonths,
    bool UseTrendFilter,
    double ScoreMddPenalty
);
