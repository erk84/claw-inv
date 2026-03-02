using System.Text.Json;

namespace ClawInv.Core.Backtest;

/// <summary>
/// Stores NAV series on disk as JSON/CSV to avoid re-downloading.
/// </summary>
public sealed class NavDataStore
{
    private readonly string _root;

    public NavDataStore(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    private string SeriesPath(string orderbookId) => Path.Combine(_root, $"{orderbookId}.nav.json");

    public bool TryRead(string orderbookId, out IReadOnlyList<NavPoint> nav)
    {
        var path = SeriesPath(orderbookId);
        if (!File.Exists(path))
        {
            nav = Array.Empty<NavPoint>();
            return false;
        }

        var json = File.ReadAllText(path);
        var points = JsonSerializer.Deserialize<List<NavPoint>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        nav = points is { Count: > 0 } ? points : Array.Empty<NavPoint>();
        return nav.Count > 0;
    }

    public void Write(string orderbookId, IReadOnlyList<NavPoint> nav)
    {
        var path = SeriesPath(orderbookId);

        // NOTE: Avanza chart data is returned as "% development since the FIRST datapoint in the requested range".
        // Therefore, callers must fetch using a stable anchor 'from' date for series to be comparable across runs.
        // We intentionally overwrite the cache file to avoid mixing differently-anchored normalizations.

        var json = JsonSerializer.Serialize(nav);
        File.WriteAllText(path, json);
    }
}
