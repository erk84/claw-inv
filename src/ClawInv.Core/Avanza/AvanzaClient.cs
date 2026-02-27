using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClawInv.Core.Infrastructure;

namespace ClawInv.Core.Avanza;

public sealed class AvanzaClient
{
    private readonly HttpClient _http;
    private readonly SimpleDiskCache? _cache;
    private readonly RateLimiter _rateLimiter;

    public AvanzaClient(HttpClient http, SimpleDiskCache? cache = null, RateLimiter? rateLimiter = null)
    {
        _http = http;
        _cache = cache;

        _http.BaseAddress ??= new Uri("https://www.avanza.se/");
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        _rateLimiter = rateLimiter ?? new RateLimiter(TimeSpan.FromMilliseconds(500));
    }

    public async Task<IReadOnlyList<AvanzaFundHit>> SearchFundsAsync(string name, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct);

        var req = new HttpRequestMessage(HttpMethod.Post, "_api/fund-guide/search")
        {
            Content = JsonContent.Create(new { name })
        };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();

        var payload = await res.Content.ReadFromJsonAsync<AvanzaSearchResponse>(cancellationToken: ct);
        return payload?.FundSearchViews ?? [];
    }

    /// <summary>
    /// Download fund chart series for an explicit date interval.
    /// Example:
    ///   /_api/fund-guide/chart/1270939/2022-11-01/2025-11-04
    /// </summary>
    public async Task<AvanzaChartResponse> GetFundChartAsync(
        string orderbookId,
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderbookId))
            throw new ArgumentException("Missing orderbookId", nameof(orderbookId));
        if (to < from)
            throw new ArgumentException("to must be >= from");

        var url = $"_api/fund-guide/chart/{orderbookId}/{from:yyyy-MM-dd}/{to:yyyy-MM-dd}";
        var cacheKey = $"avanza:chart:{orderbookId}:{from:yyyy-MM-dd}:{to:yyyy-MM-dd}";

        if (_cache is not null && _cache.TryRead(cacheKey, out var cachedJson))
        {
            var cached = JsonSerializer.Deserialize<AvanzaChartResponse>(cachedJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (cached is not null)
                return cached;
        }

        // Be gentle: rate limit + exponential backoff on 429/503
        var attempt = 0;
        while (true)
        {
            attempt++;
            await _rateLimiter.WaitAsync(ct);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Requested-With", "XMLHttpRequest");
            req.Headers.Accept.ParseAdd("application/json");

            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (attempt >= 6)
                    res.EnsureSuccessStatusCode();

                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                await Task.Delay(delay, ct);
                continue;
            }

            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(ct);
            var parsed = JsonSerializer.Deserialize<AvanzaChartResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed is null)
                throw new InvalidOperationException("Failed to parse Avanza chart response");

            _cache?.Write(cacheKey, json);
            return parsed;
        }
    }
}
