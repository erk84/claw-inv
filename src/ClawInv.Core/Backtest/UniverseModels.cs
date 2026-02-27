using System.Text.Json.Serialization;

namespace ClawInv.Core.Backtest;

public sealed record FundRef(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("orderbookId")] string OrderbookId
);

public sealed record Universe(
    [property: JsonPropertyName("funds")] List<FundRef> Funds
);
