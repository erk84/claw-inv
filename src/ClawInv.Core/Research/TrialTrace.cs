namespace ClawInv.Core.Research;

public sealed record RebalanceEvent(
    DateOnly Date,
    string Kind,
    IReadOnlyList<string> Holdings,
    double? BestMomentum,
    double? AppliedReturn,
    double Equity
);

public sealed record TrialTrace(
    TrialParams Params,
    IReadOnlyList<RebalanceEvent> Events,
    double FinalEquity,
    double Cagr,
    double MaxDrawdown
);
