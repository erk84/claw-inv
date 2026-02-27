using ClawInv.Core.Research;
using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

/// <summary>
/// Month-end backtester mirroring the research evaluator (to avoid daily calendar artifacts).
/// Uses month-end NAV, chooses holdings using info at t-1, applies return t-1->t.
/// </summary>
public static class MonthEndBestV1Backtester
{
    public static BacktestResult Run(
        StrategyDefinition strat,
        IReadOnlyList<NavSeries> series,
        DateOnly from,
        DateOnly to)
    {
        var matrices = FeatureBuilder.BuildMonthEndMatrices(series);
        var search = new StrategySearch(matrices);

        var p = new TrialParams(
            LookbackMonths: strat.LookbackMonths,
            RebalanceMonths: strat.RebalanceEveryMonths,
            TopK: strat.TopK,
            UseAbsoluteMomentum: strat.UseAbsoluteMomentumFilter,
            VolLookbackMonths: strat.VolatilityLookbackMonths,
            UseLowVolFilter: strat.UseLowVolFilter,
            TrendMaMonths: strat.MovingAverageMonths,
            UseTrendFilter: strat.MovingAverageMonths > 0,
            ScoreMddPenalty: 0.0
        );

        var r = search.Evaluate(p);

        // Map to BacktestResult
        // Using the available full matrix span; we approximate start/end from matrix.
        var start = matrices.Dates.First();
        var end = matrices.Dates.Last();
        var days = end.DayNumber - start.DayNumber;

        return new BacktestResult(
            StrategyId: strat.Id,
            StrategyName: strat.Name + " (month-end)",
            Start: start,
            End: end,
            Days: days,
            Cagr: (decimal)r.Cagr,
            Volatility: 0m,
            Sharpe: (decimal)r.Sharpe,
            MaxDrawdown: (decimal)r.MaxDrawdown,
            TotalReturn: 0m,
            Notes: "Research-aligned month-end backtest"
        );
    }
}
