using ClawInv.Core.Strategies;

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
    bool UseLowVolFilter,
    int VolLookbackMonths,

    // trend knobs
    int TrendMaMonths,

    // Regime filter
    RegimeKind Regime,
    int RegimeMaMonths,
    double RegimeBreadthThreshold,

    // risk-off behavior
    RiskOffMode RiskOffMode,
    int DefensiveVolLookbackMonths,

    // scoring
    double MaxDrawdownPenaltyLambda
);

