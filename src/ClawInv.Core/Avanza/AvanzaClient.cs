using System.Net.Http.Json;

namespace ClawInv.Core.Avanza;

public sealed class AvanzaClient
{
    private readonly HttpClient _http;

    public AvanzaClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://www.avanza.se/");
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("claw-inv/0.1");
    }

    public async Task<IReadOnlyList<AvanzaFundHit>> SearchFundsAsync(string name, CancellationToken ct = default)
    {
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
}
