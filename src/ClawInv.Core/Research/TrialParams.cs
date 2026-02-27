namespace ClawInv.Core.Research;

public sealed record TrialParams(
    ResearchStrategyKind Kind,

    // timing
    int LookbackMonths,
    int RebalanceMonths,
    int TopK,

    // momentum knobs
    bool UseAbsoluteMomentum,

    // low-vol knobs
    int VolLookbackMonths,

    // trend knobs
    int TrendMaMonths,

    // Regime filter
    RegimeKind Regime,
    int RegimeMaMonths,
    double RegimeBreadthThreshold,

    // scoring
    double MaxDrawdownPenaltyLambda
);
