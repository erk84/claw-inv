namespace ClawInv.Core.Infrastructure;

/// <summary>
/// Very small async rate limiter: enforces a minimum delay between requests.
/// Intended to avoid hammering Avanza endpoints (429).
/// </summary>
public sealed class RateLimiter
{
    private readonly TimeSpan _minDelay;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public RateLimiter(TimeSpan minDelay)
    {
        _minDelay = minDelay;
    }

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var next = _last + _minDelay;
            if (next > now)
                await Task.Delay(next - now, ct);

            _last = DateTimeOffset.UtcNow;
        }
        finally
        {
            _mutex.Release();
        }
    }
}
