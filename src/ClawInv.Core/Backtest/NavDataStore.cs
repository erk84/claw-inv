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

        // Merge with existing data so repeated partial-window downloads do not truncate history.
        // This is important for long-running web usage where we frequently load rolling windows.
        List<NavPoint>? existing = null;
        if (File.Exists(path))
        {
            try
            {
                existing = JsonSerializer.Deserialize<List<NavPoint>>(File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                existing = null;
            }
        }

        if (existing is { Count: > 0 })
        {
            var map = new Dictionary<DateOnly, decimal>();
            foreach (var p in existing)
                map[p.Date] = p.Nav;
            foreach (var p in nav)
                map[p.Date] = p.Nav;

            nav = map
                .OrderBy(kv => kv.Key)
                .Select(kv => new NavPoint(kv.Key, kv.Value))
                .ToList();
        }

        var json = JsonSerializer.Serialize(nav);
        File.WriteAllText(path, json);
    }
}
