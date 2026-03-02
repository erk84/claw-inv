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
        while (!stoppingToken.IsCancellationRequested)
        {
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
