namespace ClawInv.Web.Data.Entities;

public sealed class UniverseSettings
{
    public int Id { get; set; }

    // single-row settings pattern (Key = "default")
    public string Key { get; set; } = "default";

    public int RatingLimit { get; set; } = 3;

    // total fee percentage, e.g. 2.0
    public double TotalFeeLimit { get; set; } = 2.0;

    // risk level upper bound; keep same semantics as CLI gen-universe (0 means no cap)
    public int RiskLimit { get; set; } = 0;

    public DateTimeOffset? LastRegeneratedAtUtc { get; set; }

    public int UniverseFundCount { get; set; }
}
