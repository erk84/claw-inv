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

            // Weekly: Sunday ~03:00 UTC (run once per week even if we missed exact minute)
            if (utcNow.DayOfWeek == DayOfWeek.Sunday && utcNow.Hour >= 3)
            {
                await TryRunOnceAsync(
                    jobKey: "weekly-universe",
                    shouldRun: last => last is null || last.Value.UtcDateTime.Date < utcNow.UtcDateTime.Date,
                    run: async () => await universeRegenerator.RegenerateAsync(stoppingToken),
                    stoppingToken);
            }

            // Daily: ~02:00 UTC (run once per day even if we missed exact minute)
            if (utcNow.Hour >= 2)
            {
                await TryRunOnceAsync(
                    jobKey: "daily-recommendations",
                    shouldRun: last => last is null || last.Value.UtcDateTime.Date < utcNow.UtcDateTime.Date,
                    run: async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ClawInv.Web.Data.AppDbContext>();
                        var engine = scope.ServiceProvider.GetRequiredService<RecommendationEngine>();
                        var snapshots = scope.ServiceProvider.GetRequiredService<SnapshotEngine>();

                        var enabled = await db.StrategyConfigs.Where(x => x.Enabled).Select(x => x.Id).ToListAsync(stoppingToken);
                        log.LogInformation("Daily jobs: updating snapshots + computing recommendations for {Count} enabled strategies", enabled.Count);

                        var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

                        foreach (var id in enabled)
                        {
                            await snapshots.RebuildLast5YearsAsync(id, asOf, stoppingToken);
                            await engine.ComputeIfDueAsync(id, asOf, stoppingToken);
                        }
                    },
                    stoppingToken);
            }
        }
    }

    private async Task TryRunOnceAsync(string jobKey, Func<DateTimeOffset?, bool> shouldRun, Func<Task> run, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ClawInv.Web.Data.AppDbContext>();

            var row = await db.JobStates.SingleOrDefaultAsync(x => x.Key == jobKey, ct);
            if (row is null)
            {
                row = new ClawInv.Web.Data.Entities.JobState { Key = jobKey };
                db.JobStates.Add(row);
                await db.SaveChangesAsync(ct);
            }

            if (!shouldRun(row.LastRunAtUtc))
                return;

            row.LastError = null;
            await db.SaveChangesAsync(ct);

            await run();

            row.LastRunAtUtc = DateTimeOffset.UtcNow;
            row.LastError = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Job {JobKey} failed", jobKey);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ClawInv.Web.Data.AppDbContext>();
                var row = await db.JobStates.SingleOrDefaultAsync(x => x.Key == jobKey, ct);
                if (row is not null)
                {
                    row.LastError = ex.ToString();
                    await db.SaveChangesAsync(ct);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
