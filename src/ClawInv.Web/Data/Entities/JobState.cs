namespace ClawInv.Web.Data.Entities;

public sealed class JobState
{
    public int Id { get; set; }

    public string Key { get; set; } = "";

    public DateTimeOffset? LastRunAtUtc { get; set; }

    public string? LastError { get; set; }
}
