using System.Text.Json;
using ClawInv.Core.Avanza;
using ClawInv.Core.Backtest;
using ClawInv.Core.Infrastructure;
using Microsoft.Extensions.Caching.Memory;

namespace ClawInv.Web.Services;

public sealed class NavService(ILogger<NavService> log, IConfiguration cfg, IWebHostEnvironment env)
{
    private readonly MemoryCache _mem = new(new MemoryCacheOptions());

    public async Task<IReadOnlyList<NavSeries>> LoadUniverseNavAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var universePath = UniversePathResolver.Resolve(cfg, env.ContentRootPath, log);
        if (!File.Exists(universePath))
            throw new InvalidOperationException($"Universe file not found: {universePath}. Regenerate universe first.");

        var cacheDir = cfg["ClawInv:CacheDir"] ?? "data/avanza-cache";
        var navStoreDir = cfg["ClawInv:NavStoreDir"] ?? "data/nav";
        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(navStoreDir);

        var key = $"nav:{universePath}:{from}:{to}";
        if (_mem.TryGetValue(key, out IReadOnlyList<NavSeries>? cached) && cached is not null)
            return cached;

        var universe = LoadUniverse(universePath);

        using var http = new HttpClient();
        var cache = new SimpleDiskCache(cacheDir);
        var navStore = new NavDataStore(navStoreDir);
        var avanza = new AvanzaClient(http, cache);
        var tz = AvanzaChartConverter.GetStockholmTz();

        var opt = new StrategyOptimizer(avanza, navStore, tz);
        log.LogInformation("Loading NAV for universe ({Count} funds) {From}..{To}", universe.Funds.Count, from, to);
        var series = await opt.LoadUniverseNavAsync(universe, from, to);

        _mem.Set(key, series, TimeSpan.FromMinutes(15));
        return series;
    }

    private static Universe LoadUniverse(string path)
    {
        var json = File.ReadAllText(path);
        var u = JsonSerializer.Deserialize<Universe>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (u is null || u.Funds.Count == 0)
            throw new InvalidOperationException($"Universe file empty/invalid: {path}");
        return u;
    }
}
