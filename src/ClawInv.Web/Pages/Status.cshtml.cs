using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class StatusModel(AppDbContext db) : PageModel
{
    public UniverseSettings? Universe { get; private set; }

    public List<Row> Strategies { get; private set; } = new();

    public List<BackgroundTask> RecentTasks { get; private set; } = new();

    public List<JobState> Jobs { get; private set; } = new();

    public sealed record Row(
        string Key,
        string Name,
        bool Enabled,
        DateOnly? LatestAsOf,
        DateOnly? LatestSnapshotDate);

    public DateTimeOffset? DailyJobLastRun { get; private set; }
    public string? DailyJobLastError { get; private set; }

    public DateTimeOffset? WeeklyUniverseLastRun { get; private set; }
    public string? WeeklyUniverseLastError { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Universe = await db.UniverseSettings.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

        var strategies = await db.StrategyConfigs.OrderBy(x => x.DisplayName).ToListAsync(ct);
        var ids = strategies.Select(x => x.Id).ToList();

        var latestRuns = await db.RecommendationRuns
            .Where(r => ids.Contains(r.StrategyConfigId))
            .GroupBy(r => r.StrategyConfigId)
            .Select(g => new { StrategyConfigId = g.Key, LatestAsOf = g.Max(x => x.AsOfDate) })
            .ToListAsync(ct);

        var portfolios = await db.Portfolios.Where(p => ids.Contains(p.StrategyConfigId)).ToListAsync(ct);
        var pidByStrategy = portfolios.ToDictionary(p => p.StrategyConfigId, p => p.Id);

        var snapshotMax = await db.PortfolioDailySnapshots
            .Where(s => pidByStrategy.Values.Contains(s.PortfolioId))
            .GroupBy(s => s.PortfolioId)
            .Select(g => new { PortfolioId = g.Key, Latest = g.Max(x => x.Date) })
            .ToListAsync(ct);

        var runByStrategy = latestRuns.ToDictionary(x => x.StrategyConfigId, x => x.LatestAsOf);
        var snapByPid = snapshotMax.ToDictionary(x => x.PortfolioId, x => x.Latest);

        Strategies = strategies.Select(s =>
        {
            DateOnly? snap = null;
            if (pidByStrategy.TryGetValue(s.Id, out var pid) && snapByPid.TryGetValue(pid, out var d))
                snap = d;

            return new Row(
                s.Key,
                s.DisplayName,
                s.Enabled,
                runByStrategy.GetValueOrDefault(s.Id),
                snap);
        }).ToList();

        // SQLite provider does not support ordering by DateTimeOffset (NotSupportedException).
        // Load then order client-side (small volume).
        RecentTasks = (await db.BackgroundTasks
                .AsNoTracking()
                .ToListAsync(ct))
            .OrderByDescending(t => t.CreatedAtUtc)
            .Take(50)
            .ToList();

        Jobs = await db.JobStates
            .OrderBy(x => x.Key)
            .ToListAsync(ct);

        var daily = Jobs.FirstOrDefault(x => x.Key == "daily-recommendations");
        DailyJobLastRun = daily?.LastRunAtUtc;
        DailyJobLastError = daily?.LastError;

        var weekly = Jobs.FirstOrDefault(x => x.Key == "weekly-universe");
        WeeklyUniverseLastRun = weekly?.LastRunAtUtc;
        WeeklyUniverseLastError = weekly?.LastError;
    }
}
