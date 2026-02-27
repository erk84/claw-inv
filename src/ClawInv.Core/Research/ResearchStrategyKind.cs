namespace ClawInv.Core.Research;

public enum ResearchStrategyKind
{
    Momentum,
    LowVol,
    Trend,
    MeanReversion,
    MinVariance2,

    // New families
    SharpeProxy,
    CorrFilteredTop2,
    BandReversion
}


