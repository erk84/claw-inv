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

        // lightweight headers that seem safe
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<IReadOnlyList<AvanzaFundHit>> SearchFundsAsync(string name, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct);

        var req = new HttpRequestMessage(HttpMethod.Post, "_api/fund-guide/search")
        {
            Content = JsonContent.Create(new { name })
        };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");

        var res = await SendWithRetryAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<AvanzaSearchResponse>(cancellationToken: ct);
        return payload?.FundSearchViews ?? [];
    }

    public async Task<AvanzaFundListResponse> GetFundListPageAsync(int startIndex, double? maxTotalFee = null, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct);

        var url = "_api/fund-guide/list?shouldCheckFundExcludedFromPromotion=true";
        var body = AvanzaFundListRequest.Default(startIndex, maxTotalFee);

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Origin", "https://www.avanza.se");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.avanza.se/fonder/handla-fonder.html/list?sortField=developmentThreeYears&sortDirection=DESCENDING&selectedTab=overview");

        var res = await SendWithRetryAsync(req, ct);
        var payload = await res.Content.ReadFromJsonAsync<AvanzaFundListResponse>(cancellationToken: ct);

        return payload ?? new AvanzaFundListResponse([], 0);
    }

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

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Requested-With", "XMLHttpRequest");
        req.Headers.TryAddWithoutValidation("Referer", "https://www.avanza.se/fonder/handla-fonder.html");

        var res = await SendWithRetryAsync(req, ct);

        var json = await res.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<AvanzaChartResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (parsed is null)
            throw new InvalidOperationException("Failed to parse Avanza chart response");

        _cache?.Write(cacheKey, json);
        return parsed;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;

            var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (res.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                if (attempt >= 6)
                    res.EnsureSuccessStatusCode();

                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
                await Task.Delay(delay, ct);
                continue;
            }

            res.EnsureSuccessStatusCode();
            return res;
        }
    }
}
