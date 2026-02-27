using System.Text.Json.Serialization;

namespace ClawInv.Core.Avanza;

public sealed record AvanzaSearchResponse(
    [property: JsonPropertyName("fundSearchViews")] List<AvanzaFundHit> FundSearchViews
);
