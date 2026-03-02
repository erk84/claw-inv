namespace ClawInv.Core.Strategies.Logic;

public static class StrategyLogicRegistry
{
    private static readonly IReadOnlyDictionary<StrategyType, IStrategyLogic> _map =
        new IStrategyLogic[]
        {
            new MomentumLogic(),
            new TrendLogic(),
            new LowVolLogic(),
            new MeanReversionLogic(),
            new MinVariance2Logic(),
            new CorrFilteredTopKLogic(),
        }.ToDictionary(x => x.Type, x => x);

    public static IStrategyLogic Get(StrategyType type)
    {
        if (_map.TryGetValue(type, out var logic))
            return logic;

        throw new NotSupportedException($"No strategy logic registered for {type}");
    }
}
