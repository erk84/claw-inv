using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class UniverseRegenerator(ILogger<UniverseRegenerator> log, AppDbContext db)
{
    // NOTE: this is a placeholder for wiring to the same universe-generation logic as the CLI.
    // For now we only persist settings timestamps and counts.
    public async Task RegenerateAsync(CancellationToken ct)
    {
        var settings = await db.UniverseSettings.SingleOrDefaultAsync(x => x.Key == "default", ct);
        if (settings is null)
        {
            settings = new UniverseSettings { Key = "default" };
            db.UniverseSettings.Add(settings);
        }

        // TODO: call into ClawInv.Core/Cli universe generation logic and write to disk + update count.
        log.LogInformation("Regenerating universe with filters: rating<= {RatingLimit}, fee<= {FeeLimit}, risk<= {RiskLimit}",
            settings.RatingLimit, settings.TotalFeeLimit, settings.RiskLimit);

        settings.LastRegeneratedAtUtc = DateTimeOffset.UtcNow;
        // placeholder until we generate the actual list.
        settings.UniverseFundCount = settings.UniverseFundCount;

        await db.SaveChangesAsync(ct);
    }
}
