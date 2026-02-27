using System.Text;
using System.Text.Json;
using ClawInv.Core.Strategies;

namespace ClawInv.Core.Backtest;

public static class ReportWriter
{
    public static void WriteMarkdown(
        string outDir,
        Universe universe,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<StrategyResult> top,
        IReadOnlyDictionary<StrategyType, StrategyResult> bestPerType)
    {
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine("# claw-inv backtest report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"Universe size: {universe.Funds.Count}");
        sb.AppendLine($"Period: {from:yyyy-MM-dd} .. {to:yyyy-MM-dd}");
        sb.AppendLine();

        sb.AppendLine("## Best per strategy type");
        sb.AppendLine();
        foreach (var kv in bestPerType.OrderBy(x => x.Key.ToString()))
        {
            var r = kv.Value.Result;
            sb.AppendLine($"- {kv.Key}: {kv.Value.Strategy.Name}");
            sb.AppendLine($"  - Sharpe: {(r.Sharpe?.ToString("0.##") ?? "n/a")}");
            sb.AppendLine($"  - CAGR: {r.Cagr:P2}");
            sb.AppendLine($"  - Max drawdown: {r.MaxDrawdown:P2}");
            sb.AppendLine($"  - Total return: {r.TotalReturn:P2}");
        }

        sb.AppendLine();
        sb.AppendLine("## Top strategies (overall)");
        sb.AppendLine();

        var i = 0;
        foreach (var s in top)
        {
            i++;
            var r = s.Result;
            sb.AppendLine($"{i}. {s.Strategy.Name}");
            sb.AppendLine($"   - Type: {s.Strategy.Type}");
            sb.AppendLine($"   - Sharpe: {(r.Sharpe?.ToString("0.##") ?? "n/a")}");
            sb.AppendLine($"   - CAGR: {r.Cagr:P2}");
            sb.AppendLine($"   - MDD: {r.MaxDrawdown:P2}");
        }

        File.WriteAllText(Path.Combine(outDir, "report.md"), sb.ToString());
    }

    public static void WriteJson(
        string outDir,
        Universe universe,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<StrategyResult> top,
        IReadOnlyDictionary<StrategyType, StrategyResult> bestPerType)
    {
        Directory.CreateDirectory(outDir);

        var obj = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            universeSize = universe.Funds.Count,
            from,
            to,
            bestPerType,
            top
        };

        File.WriteAllText(
            Path.Combine(outDir, "report.json"),
            JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }
}
