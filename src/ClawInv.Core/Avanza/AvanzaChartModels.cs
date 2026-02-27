using System.Text.Json.Serialization;

namespace ClawInv.Core.Avanza;

public sealed record AvanzaChartPoint(
    [property: JsonPropertyName("x")] long X,
    [property: JsonPropertyName("y")] double? Y
);

public sealed record AvanzaChartResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fromDate")] string FromDate,
    [property: JsonPropertyName("toDate")] string ToDate,
    [property: JsonPropertyName("dataSerie")] List<AvanzaChartPoint> DataSerie
);
