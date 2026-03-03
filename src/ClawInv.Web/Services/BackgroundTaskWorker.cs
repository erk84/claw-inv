using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Services;

public sealed class BackgroundTaskWorker(
    ILogger<BackgroundTaskWorker> log,
    IServiceScopeFactory scopeFactory,
    BackgroundTaskQueue queue)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Recover any pending tasks from the DB (durable across restarts).
        await EnqueuePendingFromDbAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Prefer executing durable DB tasks first (covers the common "app restarted" case).
            if (await TryRunOnePendingDbTaskAsync(stoppingToken))
                continue;

            // Otherwise, run in-memory queued work.
            var work = await queue.DequeueAsync(stoppingToken);

            try
            {
                await work(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Background task failed");
            }

            // Tiny delay to avoid tight loop if something goes weird.
            await Task.Delay(TimeSpan.FromMilliseconds(50), stoppingToken);
        }
    }

    private async Task EnqueuePendingFromDbAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var pending = await db.BackgroundTasks
                .Where(t => t.Status == BackgroundTaskStatus.Pending)
                .OrderBy(t => t.Id)
                .Select(t => t.Id)
                .ToListAsync(ct);

            if (pending.Count > 0)
                log.LogInformation("Recovered pending background tasks: {Count}", pending.Count);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to recover pending background tasks");
        }
    }

    private async Task<bool> TryRunOnePendingDbTaskAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var row = await db.BackgroundTasks
            .Where(t => t.Status == BackgroundTaskStatus.Pending)
            .OrderBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return false;

        row.Status = BackgroundTaskStatus.Running;
        row.StartedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            switch (row.Type)
            {
                case BackgroundTaskType.BootstrapStrategy:
                {
                    var bootstrap = scope.ServiceProvider.GetRequiredService<BootstrapEngine>();

                    // Parse as-of from message (fallback: yesterday)
                    var asOf = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                    var parts = (row.Message ?? "").Split("as-of", StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && DateOnly.TryParse(parts[1], out var parsed))
                        asOf = parsed;

                    if (row.StrategyConfigId is null)
                        throw new InvalidOperationException("BootstrapStrategy task missing StrategyConfigId");

                    await bootstrap.BootstrapLast5YearsIfEmptyAsync(row.StrategyConfigId.Value, asOf, ct);
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unknown background task type: {row.Type}");
            }

            row.Status = BackgroundTaskStatus.Succeeded;
            row.FinishedAtUtc = DateTimeOffset.UtcNow;
            row.Error = null;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            row.Status = BackgroundTaskStatus.Failed;
            row.FinishedAtUtc = DateTimeOffset.UtcNow;
            row.Error = ex.ToString();
            await db.SaveChangesAsync(ct);
            log.LogError(ex, "DB background task failed: id={Id} type={Type}", row.Id, row.Type);
            return true;
        }
    }

    public async Task EnqueueBootstrapAsync(int strategyConfigId, DateOnly asOf, CancellationToken ct)
    {
        // Note: ct here is request token; only used for enqueue+DB insert.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var t = new BackgroundTask
        {
            Type = BackgroundTaskType.BootstrapStrategy,
            Status = BackgroundTaskStatus.Pending,
            StrategyConfigId = strategyConfigId,
            Message = $"Bootstrap last 5y as-of {asOf}"
        };

        db.BackgroundTasks.Add(t);
        await db.SaveChangesAsync(ct);

        await queue.EnqueueAsync(async workerCt =>
        {
            using var inner = scopeFactory.CreateScope();
            var db2 = inner.ServiceProvider.GetRequiredService<AppDbContext>();
            var bootstrap = inner.ServiceProvider.GetRequiredService<BootstrapEngine>();

            var row = await db2.BackgroundTasks.SingleAsync(x => x.Id == t.Id, workerCt);
            row.Status = BackgroundTaskStatus.Running;
            row.StartedAtUtc = DateTimeOffset.UtcNow;
            await db2.SaveChangesAsync(workerCt);

            try
            {
                await bootstrap.BootstrapLast5YearsIfEmptyAsync(strategyConfigId, asOf, workerCt);

                row.Status = BackgroundTaskStatus.Succeeded;
                row.FinishedAtUtc = DateTimeOffset.UtcNow;
                row.Error = null;
                await db2.SaveChangesAsync(workerCt);
            }
            catch (Exception ex)
            {
                row.Status = BackgroundTaskStatus.Failed;
                row.FinishedAtUtc = DateTimeOffset.UtcNow;
                row.Error = ex.ToString();
                await db2.SaveChangesAsync(workerCt);
                throw;
            }
        });
    }
}
