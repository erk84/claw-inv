namespace ClawInv.Web.Data.Entities;

public sealed class RecommendationRun
{
    public int Id { get; set; }

    public int StrategyConfigId { get; set; }
    public StrategyConfig? Strategy { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateOnly AsOfDate { get; set; }

    public string Notes { get; set; } = "";

    public List<TradeRecommendation> Trades { get; set; } = new();
}

public enum RecommendationAction
{
    Buy = 1,
    Sell = 2,
    Hold = 3,
}

public sealed class TradeRecommendation
{
    public int Id { get; set; }

    public int RecommendationRunId { get; set; }
    public RecommendationRun? Run { get; set; }

    public RecommendationAction Action { get; set; }

    public string FundId { get; set; } = "";
    public string FundName { get; set; } = "";

    public string Reason { get; set; } = "";
}
