using Spectre.Console.Cli;

namespace ClawInv.Cli;

public sealed class BacktestStrategyCommand : Command<BacktestStrategyCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--universe <PATH>")]
        public string UniversePath { get; init; } = "";

        [CommandOption("--years <N>")]
        public int Years { get; init; } = 10;

        [CommandOption("--type <TYPE>")]
        public string Type { get; init; } = "MomentumRotation";

        [CommandOption("--lookback <N>")]
        public int LookbackMonths { get; init; } = 12;

        [CommandOption("--rebalance <N>")]
        public int RebalanceEveryMonths { get; init; } = 1;

        [CommandOption("--slots <N>")]
        public int Slots { get; init; } = 2;

        [CommandOption("--abs-mom")]
        public bool UseAbsoluteMomentum { get; init; }

        [CommandOption("--ma <N>")]
        public int MovingAverageMonths { get; init; } = 12;

        [CommandOption("--vol-lb <N>")]
        public int VolatilityLookbackMonths { get; init; } = 12;

        [CommandOption("--regime <KIND>")]
        public string Regime { get; init; } = "None";

        [CommandOption("--regime-ma <N>")]
        public int RegimeMaMonths { get; init; } = 10;

        [CommandOption("--regime-th <X>")]
        public double RegimeThreshold { get; init; } = 0.0;

        [CommandOption("--risk-off <MODE>")]
        public string RiskOffMode { get; init; } = "Cash";

        [CommandOption("--def-vol-lb <N>")]
        public int DefensiveVolLookbackMonths { get; init; } = 12;

        [CommandOption("--start-capital <N>")]
        public decimal StartCapital { get; init; } = 100_000m;
    }

    public override int Execute(CommandContext context, Settings s)
    {
        return BacktestStrategy.Run(s);
    }
}
