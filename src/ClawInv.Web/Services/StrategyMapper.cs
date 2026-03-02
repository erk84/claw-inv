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
            ResearchStrategyKind.SharpeProxy => StrategyType.SharpeProxy,
            ResearchStrategyKind.CorrFilteredTop2 => StrategyType.CorrFilteredTopK,
            ResearchStrategyKind.BandReversion => StrategyType.BandReversion,

            _ => StrategyType.MomentumRotation
        };

        // Important: Slots controls live holdings count.
        // Keep cfg.TopK as a strategy-family parameter if/when we expose it separately.
        var slots = Math.Max(1, cfg.Slots);

        return new StrategyDefinition(
            Id: cfg.Key,
            Name: cfg.DisplayName,
            Type: type,
            RebalanceEveryMonths: cfg.RebalanceMonths,
            LookbackMonths: cfg.LookbackMonths,
            TopK: slots,
            Allocation: AllocationMode.EqualWeightTopK,
            UseAbsoluteMomentumFilter: cfg.UseAbsoluteMomentum,
            MovingAverageMonths: cfg.TrendMaMonths,
            VolatilityLookbackMonths: cfg.VolLookbackMonths,
            UseLowVolFilter: cfg.UseLowVolFilter,
            Regime: cfg.Regime,
            RegimeMaMonths: Math.Max(1, cfg.RegimeMaMonths),
            RegimeThreshold: cfg.RegimeThreshold,
            RiskOffMode: cfg.RiskOffMode,
            DefensiveVolLookbackMonths: Math.Max(1, cfg.DefensiveVolLookbackMonths)
        );
    }
}
