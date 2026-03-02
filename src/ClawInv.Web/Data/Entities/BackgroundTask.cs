namespace ClawInv.Web.Data.Entities;

public enum BackgroundTaskType
{
    BootstrapStrategy = 1,
}

public enum BackgroundTaskStatus
{
    Pending = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
}

public sealed class BackgroundTask
{
    public int Id { get; set; }

    public BackgroundTaskType Type { get; set; }
    public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.Pending;

    public int? StrategyConfigId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }

    public string Message { get; set; } = "";
    public string? Error { get; set; }
}
