using System.Text.Json.Serialization;

namespace ClawInv.Core.Avanza;

public sealed record AvanzaFundListItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("orderbookId")] string OrderbookId,
    [property: JsonPropertyName("rating")] int? Rating,
    [property: JsonPropertyName("risk")] int? Risk,
    [property: JsonPropertyName("totalFee")] double? TotalFee,
    [property: JsonPropertyName("buyable")] bool? Buyable,
    [property: JsonPropertyName("developmentThreeYears")] double? DevelopmentThreeYears,
    [property: JsonPropertyName("developmentFiveYears")] double? DevelopmentFiveYears,
    [property: JsonPropertyName("developmentTenYears")] double? DevelopmentTenYears
);

public sealed record AvanzaFundListResponse(
    [property: JsonPropertyName("fundListViews")] List<AvanzaFundListItem> FundListViews,
    [property: JsonPropertyName("totalNoFunds")] int TotalNoFunds
);
