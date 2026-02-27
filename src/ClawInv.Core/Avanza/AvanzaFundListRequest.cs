using System.Text.Json.Serialization;

namespace ClawInv.Core.Avanza;

/// <summary>
/// Minimal request payload for /_api/fund-guide/list.
/// We keep fields explicit to avoid accidental changes.
/// </summary>
public sealed record AvanzaFundListRequest(
    [property: JsonPropertyName("startIndex")] int StartIndex,
    [property: JsonPropertyName("managedType")] string ManagedType,
    [property: JsonPropertyName("svanenMark")] bool SvanenMark,
    [property: JsonPropertyName("commonRegionFilter")] List<string> CommonRegionFilter,
    [property: JsonPropertyName("otherRegionFilter")] List<string> OtherRegionFilter,
    [property: JsonPropertyName("alignmentFilter")] List<string> AlignmentFilter,
    [property: JsonPropertyName("industryFilter")] List<string> IndustryFilter,
    [property: JsonPropertyName("fundTypeFilter")] List<string> FundTypeFilter,
    [property: JsonPropertyName("interestTypeFilter")] List<string> InterestTypeFilter,
    [property: JsonPropertyName("sortField")] string SortField,
    [property: JsonPropertyName("sortDirection")] string SortDirection,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("recommendedHoldingPeriodFilter")] List<string> RecommendedHoldingPeriodFilter,
    [property: JsonPropertyName("companyFilter")] List<string> CompanyFilter,
    [property: JsonPropertyName("productInvolvementsFilter")] List<string> ProductInvolvementsFilter,
    [property: JsonPropertyName("ratingFilter")] List<string> RatingFilter,
    [property: JsonPropertyName("riskFilter")] List<string> RiskFilter,
    [property: JsonPropertyName("sustainabilityRatingFilter")] List<string> SustainabilityRatingFilter,
    [property: JsonPropertyName("environmentalRatingFilter")] List<string> EnvironmentalRatingFilter,
    [property: JsonPropertyName("socialRatingFilter")] List<string> SocialRatingFilter,
    [property: JsonPropertyName("governanceRatingFilter")] List<string> GovernanceRatingFilter,
    [property: JsonPropertyName("sustainableDevelopmentGoalsAlignmentFilter")] List<string> SustainableDevelopmentGoalsAlignmentFilter,
    [property: JsonPropertyName("euArticleTypeFilter")] List<string> EuArticleTypeFilter,
    [property: JsonPropertyName("maxTotalFee")] double? MaxTotalFee,
    [property: JsonPropertyName("cashDividends")] bool CashDividends
)
{
    public static AvanzaFundListRequest Default(int startIndex, double? maxTotalFee = null) =>
        new(
            StartIndex: startIndex,
            ManagedType: "ANY",
            SvanenMark: false,
            CommonRegionFilter: [],
            OtherRegionFilter: [],
            AlignmentFilter: [],
            IndustryFilter: [],
            FundTypeFilter: [],
            InterestTypeFilter: [],
            SortField: "developmentThreeYears",
            SortDirection: "DESCENDING",
            Name: "",
            RecommendedHoldingPeriodFilter: [],
            CompanyFilter: [],
            ProductInvolvementsFilter: [],
            RatingFilter: [],
            RiskFilter: [],
            SustainabilityRatingFilter: [],
            EnvironmentalRatingFilter: [],
            SocialRatingFilter: [],
            GovernanceRatingFilter: [],
            SustainableDevelopmentGoalsAlignmentFilter: [],
            EuArticleTypeFilter: [],
            MaxTotalFee: maxTotalFee,
            CashDividends: false
        );
}
