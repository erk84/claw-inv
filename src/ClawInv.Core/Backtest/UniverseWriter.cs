using System.Text.Json;

namespace ClawInv.Core.Backtest;

public static class UniverseWriter
{
    public static void Save(Universe u, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(u, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
