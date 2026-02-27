using System.Text.Json.Serialization;

namespace ClawInv.Core.Avanza;

public sealed record AvanzaFundHit(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("isin")] string Isin,
    [property: JsonPropertyName("orderbookId")] string OrderbookId,
    [property: JsonPropertyName("rating")] int? Rating,
    [property: JsonPropertyName("risk")] int? Risk,
    [property: JsonPropertyName("developmentThreeYears")] decimal? DevelopmentThreeYears
);
