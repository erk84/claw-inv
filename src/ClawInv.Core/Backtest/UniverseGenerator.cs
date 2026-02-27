using System.Text.Json;
using ClawInv.Core.Avanza;

namespace ClawInv.Core.Backtest;

public sealed class UniverseGenerator
{
    private readonly AvanzaClient _avanza;

    public UniverseGenerator(AvanzaClient avanza)
    {
        _avanza = avanza;
    }

    public async Task<Universe> GenerateAsync(int targetFunds, int maxRequests, CancellationToken ct = default)
    {
        var rnd = new Random(1337);
        var seen = new Dictionary<string, FundRef>();

        string RandomQuery()
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz";
            var len = rnd.Next(2, 4); // 2-3
            return new string(Enumerable.Range(0, len).Select(_ => letters[rnd.Next(letters.Length)]).ToArray());
        }

        for (var i = 0; i < maxRequests && seen.Count < targetFunds; i++)
        {
            var q = i == 0 ? "" : RandomQuery();
            var hits = await _avanza.SearchFundsAsync(q, ct);
            foreach (var h in hits)
            {
                if (!seen.ContainsKey(h.OrderbookId))
                    seen[h.OrderbookId] = new FundRef(h.Name, h.OrderbookId);

                if (seen.Count >= targetFunds)
                    break;
            }
        }

        return new Universe(seen.Values.OrderBy(x => x.Name).ToList());
    }

    public static void Save(Universe u, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(u, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
