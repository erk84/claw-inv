using Spectre.Console.Cli;

namespace ClawInv.Cli;

public sealed class TraceCommand : Command<TraceCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--best-json <PATH>")]
        public string BestJsonPath { get; init; } = "";

        [CommandOption("--universe <PATH>")]
        public string UniversePath { get; init; } = "";

        [CommandOption("--years <N>")]
        public int Years { get; init; } = 10;

        [CommandOption("--out <PATH>")]
        public string OutPath { get; init; } = "out/trace.csv";
    }

    public override int Execute(CommandContext context, Settings settings)
    {
        return TraceBest.Run(settings.BestJsonPath, settings.UniversePath, settings.Years, settings.OutPath);
    }
}
