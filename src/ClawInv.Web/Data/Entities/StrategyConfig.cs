using ClawInv.Core.Research;

namespace ClawInv.Web.Data.Entities;

public sealed class StrategyConfig
{
    public int Id { get; set; }

    // unique stable key like "MeanReversion/default"
    public string Key { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public bool Enabled { get; set; }

    public ResearchStrategyKind Kind { get; set; }

    // Per-strategy portfolio slots. Default 2.
    public int Slots { get; set; } = 2;

    // Strategy parameters (subset; extend as needed)
    public int LookbackMonths { get; set; } = 12;
    public int RebalanceMonths { get; set; } = 3;
    public int TopK { get; set; } = 2;
    public bool UseAbsoluteMomentum { get; set; }
    public bool UseLowVolFilter { get; set; }
    public int VolLookbackMonths { get; set; } = 12;
    public int TrendMaMonths { get; set; } = 12;

    public string DefaultSource { get; set; } = "";

    // Soft-change behavior: apply parameter/slot changes at next rebalance.
    public DateTimeOffset? PendingChangesAtUtc { get; set; }
}
