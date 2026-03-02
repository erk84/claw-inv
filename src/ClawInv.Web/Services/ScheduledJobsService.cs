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

            // Daily: 02:00 UTC (placeholder)
            if (utcNow.Hour == 2 && utcNow.Minute == 0)
            {
                try
                {
                    // TODO: implement NAV refresh + per-strategy rebalance recommendations.
                    log.LogInformation("Daily jobs tick: TODO refresh NAV + compute recommendations");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Daily jobs failed");
                }
            }
        }
    }
}
