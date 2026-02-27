using System.Text.Json;

namespace ClawInv.Core.Backtest;

public static class UniverseLoader
{
    public static Universe Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Universe file not found: {path}");

        var json = File.ReadAllText(path);
        var u = JsonSerializer.Deserialize<Universe>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (u is null || u.Funds.Count == 0)
            throw new InvalidOperationException("Universe must contain at least one fund");

        return u;
    }
}
