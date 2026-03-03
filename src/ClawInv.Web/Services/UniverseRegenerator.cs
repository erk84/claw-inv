using ClawInv.Core.Avanza;
using ClawInv.Core.Backtest;
using ClawInv.Core.Infrastructure;
using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class UniverseRegenerator(
    ILogger<UniverseRegenerator> log,
    IConfiguration cfg,
    IServiceScopeFactory scopeFactory)
{
    public async Task RegenerateAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.UniverseSettings.SingleOrDefaultAsync(x => x.Key == "default", ct);
        if (settings is null)
        {
            settings = new UniverseSettings { Key = "default" };
            db.UniverseSettings.Add(settings);
        }

        var cacheDir = cfg["ClawInv:CacheDir"] ?? "data/avanza-cache";
        var universePath = UniversePathResolver.Resolve(cfg, AppContext.BaseDirectory, log);

        Directory.CreateDirectory(cacheDir);
        Directory.CreateDirectory(Path.GetDirectoryName(universePath) ?? ".");

        log.LogInformation(
            "Regenerating universe: rating>={RatingLimit}, totalFee<={FeeLimit}, risk>={RiskLimit} (no max count)",
            settings.RatingLimit, settings.TotalFeeLimit, settings.RiskLimit);

        using var http = new HttpClient();
        var cache = new SimpleDiskCache(cacheDir);
        var avanza = new AvanzaClient(http, cache);

        var gen = new UniverseGenerator(avanza);
        var universe = await gen.GenerateAllFromFundListAsync(
            settings.RatingLimit,
            settings.TotalFeeLimit,
            settings.RiskLimit,
            ct);

        UniverseWriter.Save(universe, universePath);

        settings.LastRegeneratedAtUtc = DateTimeOffset.UtcNow;
        settings.UniverseFundCount = universe.Funds.Count;

        await db.SaveChangesAsync(ct);

        log.LogInformation("Universe regenerated: {Count} funds written to {Path}", universe.Funds.Count, universePath);
    }
}
