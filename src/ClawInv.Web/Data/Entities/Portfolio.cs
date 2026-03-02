namespace ClawInv.Web.Data.Entities;

public sealed class Portfolio
{
    public int Id { get; set; }

    public int StrategyConfigId { get; set; }
    public StrategyConfig? Strategy { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? LastRebalanceDate { get; set; }

    // how far back to show in UI; default 5y window is done in UI query.

    public List<PortfolioHolding> Holdings { get; set; } = new();
    public List<TradeEvent> Trades { get; set; } = new();
    public List<PortfolioDailySnapshot> Snapshots { get; set; } = new();
}

public sealed class PortfolioHolding
{
    public int Id { get; set; }

    public int PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }

    // fund identity (Avanza id or our internal id). For now store ISIN/name.
    public string FundId { get; set; } = "";
    public string FundName { get; set; } = "";

    public DateOnly BuyDate { get; set; }
    public decimal BuyNav { get; set; }

    public DateOnly? SellDate { get; set; }
    public decimal? SellNav { get; set; }
}

public enum TradeSide
{
    Buy = 1,
    Sell = 2,
}

public sealed class TradeEvent
{
    public int Id { get; set; }

    public int PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }

    public DateOnly Date { get; set; }

    public string FundId { get; set; } = "";
    public string FundName { get; set; } = "";

    public TradeSide Side { get; set; }

    public decimal Nav { get; set; }
}

public sealed class PortfolioDailySnapshot
{
    public int Id { get; set; }

    public int PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }

    public DateOnly Date { get; set; }

    // 1.0 at start; UI plots (EquityIndex - 1)*100
    public double EquityIndex { get; set; }
}
