using ClawInv.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ClawInv.Web.Services;

/// <summary>
/// Minimal in-process scheduler:
/// - Daily at ~02:00 UTC: refresh NAV/fund data + check rebalance recommendations per enabled strategy.
/// - Weekly on Sunday ~03:00 UTC: regenerate universe.
///
/// This keeps deployment simple for Raspberry Pi + Docker.
/// Later we can swap to Quartz.NET if needed.
/// </summary>
public sealed class ScheduledJobsService(
    ILogger<ScheduledJobsService> log,
    IServiceScopeFactory scopeFactory,
    UniverseRegenerator universeRegenerator)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once on startup (non-destructive): ensure weekly universe exists.
        try
        {
            await universeRegenerator.RegenerateAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Universe regeneration failed on startup");
        }

        // Simple periodic loop.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var utcNow = DateTimeOffset.UtcNow;

            // Weekly: Sunday 03:00 UTC
            if (utcNow.DayOfWeek == DayOfWeek.Sunday && utcNow.Hour == 3 && utcNow.Minute == 0)
            {
                try
                {
                    await universeRegenerator.RegenerateAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Weekly universe regeneration failed");
                }
            }

            // Daily: 02:00 UTC
            if (utcNow.Hour == 2 && utcNow.Minute == 0)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ClawInv.Web.Data.AppDbContext>();
                    var engine = scope.ServiceProvider.GetRequiredService<RecommendationEngine>();

                    var enabled = await db.StrategyConfigs.Where(x => x.Enabled).Select(x => x.Id).ToListAsync(stoppingToken);
                    log.LogInformation("Daily jobs: computing recommendations for {Count} enabled strategies", enabled.Count);

                    var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

                    foreach (var id in enabled)
                        await engine.ComputeIfDueAsync(id, asOf, stoppingToken);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Daily jobs failed");
                }
            }
        }
    }
}
