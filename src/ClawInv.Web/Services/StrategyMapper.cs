using ClawInv.Core.Research;
using ClawInv.Core.Strategies;
using ClawInv.Web.Data.Entities;

namespace ClawInv.Web.Services;

public static class StrategyMapper
{
    public static StrategyDefinition ToStrategyDefinition(StrategyConfig cfg)
    {
        // NOTE: extend as more knobs are exposed in StrategyConfig.
        var type = cfg.Kind switch
        {
            ResearchStrategyKind.Momentum => StrategyType.MomentumRotation,
            ResearchStrategyKind.Trend => StrategyType.TrendFollowing,
            ResearchStrategyKind.LowVol => StrategyType.LowVolatilitySelection,
            ResearchStrategyKind.MeanReversion => StrategyType.MeanReversionRotation,
            ResearchStrategyKind.MinVariance2 => StrategyType.MinVariance2,

            // These currently exist in research mode only; map to closest until we add dedicated daily implementations.
            ResearchStrategyKind.SharpeProxy => StrategyType.MomentumRotation,
            ResearchStrategyKind.CorrFilteredTop2 => StrategyType.MomentumRotation,
            ResearchStrategyKind.BandReversion => StrategyType.MeanReversionRotation,

            _ => StrategyType.MomentumRotation
        };

        return new StrategyDefinition(
            Id: cfg.Key,
            Name: cfg.DisplayName,
            Type: type,
            RebalanceEveryMonths: cfg.RebalanceMonths,
            LookbackMonths: cfg.LookbackMonths,
            TopK: Math.Max(1, Math.Min(2, cfg.TopK)),
            Allocation: AllocationMode.EqualWeightTopK,
            UseAbsoluteMomentumFilter: cfg.UseAbsoluteMomentum,
            MovingAverageMonths: cfg.TrendMaMonths,
            VolatilityLookbackMonths: cfg.VolLookbackMonths,
            UseLowVolFilter: cfg.UseLowVolFilter,
            Regime: RegimeKind.None,
            RegimeMaMonths: 1,
            RegimeThreshold: 0.0,
            RiskOffMode: RiskOffMode.Cash,
            DefensiveVolLookbackMonths: 6
        );
    }
}
