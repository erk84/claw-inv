using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawInv.Core.Backtest;
using ClawInv.Core.Research;

namespace ClawInv.Cli;

public static class TraceBest
{
    private sealed record LegacyTrialParams(
        int LookbackMonths,
        int RebalanceMonths,
        int TopK,
        bool UseAbsoluteMomentum,
        int VolLookbackMonths,
        bool UseLowVolFilter,
        int TrendMaMonths,
        bool UseTrendFilter,
        double ScoreMddPenalty
    );

    private sealed record LegacyTrialResult(
        LegacyTrialParams Params,
        double Sharpe,
        double Cagr,
        double MaxDrawdown,
        double Score
    );

    public static int Run(string bestJsonPath, string universePath, int years, string outPath)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOpts.Converters.Add(new JsonStringEnumConverter());

        var bestJson = File.ReadAllText(bestJsonPath);

        // Detect legacy schema (research v1)
        using var doc = JsonDocument.Parse(bestJson);
        if (doc.RootElement.TryGetProperty("Params", out var pEl) && pEl.TryGetProperty("UseLowVolFilter", out _))
        {
            var legacy = JsonSerializer.Deserialize<LegacyTrialResult>(bestJson, jsonOpts);
            if (legacy is null) throw new ArgumentException($"Could not read legacy best json: {bestJsonPath}");
            return RunLegacy(legacy, universePath, years, outPath);
        }

        var best = JsonSerializer.Deserialize<TrialResult>(bestJson, jsonOpts);
        if (best is null) throw new ArgumentException($"Could not read best json: {bestJsonPath}");

        var universe = JsonSerializer.Deserialize<Universe>(File.ReadAllText(universePath), jsonOpts);
        if (universe is null) throw new ArgumentException($"Could not read universe: {universePath}");

        var to = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var from = to.AddYears(-years);

        var nav = LoadNav(universe, from, to);
        var matrices = FeatureBuilder.BuildMonthEndMatrices(nav);
        var search = new StrategySearch(matrices);

        var trace = search.Trace(best.Params);
        var idToName = nav.ToDictionary(s => s.OrderbookId, s => s.Name);
        WriteTraceCsv(outPath, trace.Events, idToName);

        Console.WriteLine($"Wrote trace to {outPath}");
        Console.WriteLine($"Final equity: {trace.FinalEquity:0.###}x  CAGR: {trace.Cagr:P2}  MDD: {trace.MaxDrawdown:P2}");
        return 0;
    }

    private static int RunLegacy(LegacyTrialResult best, string universePath, int years, string outPath)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var universe = JsonSerializer.Deserialize<Universe>(File.ReadAllText(universePath), jsonOpts);
        if (universe is null) throw new ArgumentException($"Could not read universe: {universePath}");

        var to = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var from = to.AddYears(-years);

        var nav = LoadNav(universe, from, to);
        var m = FeatureBuilder.BuildMonthEndMatrices(nav);

        var events = new List<RebalanceEvent>();

        var T = m.Dates.Length;
        var F = m.FundIds.Length;

        var lb = Math.Max(1, best.Params.LookbackMonths);
        var reb = Math.Max(1, best.Params.RebalanceMonths);
        var topK = Math.Max(1, Math.Min(2, best.Params.TopK));
        var volLb = Math.Max(2, best.Params.VolLookbackMonths);

        var warmup = Math.Max(lb, volLb) + 2;

        var equity = 1.0;
        var peak = 1.0;
        var mdd = 0.0;

        var holdings = new List<int>();

        for (var t = 1; t < T; t++)
        {
            if (t >= warmup && (t % reb == 0))
            {
                holdings.Clear();
                var infoT = t - 1;

                // Momentum ranking
                var momList = new List<(int f, double mom)>();
                for (var f = 0; f < F; f++)
                {
                    var navNow = m.Nav[infoT, f];
                    var navThen = m.Nav[infoT - lb, f];
                    if (double.IsNaN(navNow) || double.IsNaN(navThen) || navThen == 0) continue;
                    var mom = navNow / navThen - 1.0;
                    momList.Add((f, mom));
                }

                if (momList.Count > 0)
                {
                    momList.Sort((a, b) => b.mom.CompareTo(a.mom));
                    var bestMom = momList[0].mom;

                    if (best.Params.UseAbsoluteMomentum && bestMom <= 0)
                    {
                        // CASH
                        events.Add(new RebalanceEvent(m.Dates[t], "REBALANCE", Array.Empty<string>(), bestMom, null, equity));
                    }
                    else
                    {
                        if (best.Params.UseLowVolFilter)
                        {
                            // Take a momentum pool, then pick lowest vol within that pool
                            var poolSize = Math.Min(momList.Count, Math.Max(10, topK * 5));
                            var pool = momList.Take(poolSize).Select(x => x.f).ToArray();

                            var vols = new List<(int f, double v)>();
                            for (var i = 0; i < pool.Length; i++)
                            {
                                var f = pool[i];
                                var v = VolMonthly(m.Ret1M, infoT, f, volLb);
                                if (!double.IsNaN(v)) vols.Add((f, v));
                            }

                            if (vols.Count > 0)
                            {
                                vols.Sort((a, b) => a.v.CompareTo(b.v));
                                holdings.AddRange(vols.Take(topK).Select(x => x.f));
                            }
                            else
                            {
                                holdings.AddRange(pool.Take(topK));
                            }
                        }
                        else
                        {
                            holdings.AddRange(momList.Take(topK).Select(x => x.f));
                        }

                        events.Add(new RebalanceEvent(
                            Date: m.Dates[t],
                            Kind: "REBALANCE",
                            Holdings: holdings.Select(i => m.FundIds[i]).ToArray(),
                            BestMomentum: bestMom,
                            AppliedReturn: null,
                            Equity: equity));
                    }
                }
            }

            // Apply month return
            var r = 0.0;
            if (holdings.Count > 0)
            {
                var sum = 0.0;
                var n = 0;
                foreach (var f in holdings)
                {
                    var rr = m.Ret1M[t, f];
                    if (double.IsNaN(rr)) continue;
                    sum += rr;
                    n++;
                }
                if (n > 0) r = sum / n;
            }

            equity *= (1.0 + r);
            peak = Math.Max(peak, equity);
            var dd = equity / peak - 1.0;
            mdd = Math.Min(mdd, dd);

            if (events.Count > 0 && events[^1].Date == m.Dates[t] && events[^1].AppliedReturn is null)
            {
                var last = events[^1];
                events[^1] = last with { AppliedReturn = r, Equity = equity };
            }
        }

        // Legacy trace currently uses orderbookId as fundId; map via loaded NAV series
        var idToName = nav.ToDictionary(s => s.OrderbookId, s => s.Name);
        WriteTraceCsv(outPath, events, idToName);
        Console.WriteLine($"Wrote trace to {outPath}");
        Console.WriteLine($"Final equity: {equity:0.###}x  (max dd {mdd:P2})");
        return 0;
    }

    private static void WriteTraceCsv(string outPath, IReadOnlyList<RebalanceEvent> events, IReadOnlyDictionary<string, string> idToName)
    {
        const double initialCapital = 100_000.0;

        var sb = new StringBuilder();
        sb.AppendLine("Date,Kind,Holdings,BestMomentum,AppliedReturn,StartEquityX,EndEquityX,PctChange,StartValue,EndValue,MoneyChange");

        var prevEquityX = 1.0;

        foreach (var e in events)
        {
            var holdings = string.Join("|", e.Holdings.Select(id => idToName.TryGetValue(id, out var n) ? $"{n} ({id})" : id));

            var endEquityX = e.Equity;
            var startEquityX = prevEquityX;

            // Prefer AppliedReturn if present; otherwise compute from equity.
            var pct = e.AppliedReturn ?? (startEquityX > 0 ? (endEquityX / startEquityX - 1.0) : double.NaN);

            var startValue = initialCapital * startEquityX;
            var endValue = initialCapital * endEquityX;
            var moneyChange = endValue - startValue;

            sb.AppendLine(
                $"{e.Date},{e.Kind},\"{holdings}\"," +
                $"{(e.BestMomentum?.ToString("0.########") ?? "")}," +
                $"{(e.AppliedReturn?.ToString("0.########") ?? "")}," +
                $"{startEquityX:0.########},{endEquityX:0.########}," +
                $"{pct:0.########}," +
                $"{startValue:0.##},{endValue:0.##},{moneyChange:0.##}"
            );

            prevEquityX = endEquityX;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? ".");
        File.WriteAllText(outPath, sb.ToString());
    }

    private static IReadOnlyList<NavSeries> LoadNav(Universe universe, DateOnly from, DateOnly to)
    {
        var cacheDir = Path.Combine(Environment.CurrentDirectory, ".cache", "avanza");
        Directory.CreateDirectory(cacheDir);
        var cache = new ClawInv.Core.Infrastructure.SimpleDiskCache(cacheDir);
        var navStore = new NavDataStore(Path.Combine(Environment.CurrentDirectory, ".cache", "nav"));
        using var http = new HttpClient();
        var avanza = new ClawInv.Core.Avanza.AvanzaClient(http, cache);
        var tz = ClawInv.Core.Avanza.AvanzaChartConverter.GetStockholmTz();
        var opt = new ClawInv.Core.Backtest.StrategyOptimizer(avanza, navStore, tz);
        return opt.LoadUniverseNavAsync(universe, from, to).GetAwaiter().GetResult();
    }

    private static double VolMonthly(double[,] ret1M, int t, int f, int months)
    {
        if (months <= 1) return double.NaN;
        var start = t - months;
        if (start < 1) return double.NaN;
        var vals = new List<double>();
        for (var i = start; i <= t; i++)
        {
            var v = ret1M[i, f];
            if (double.IsNaN(v)) continue;
            vals.Add(v);
        }
        if (vals.Count < 2) return double.NaN;
        var mean = vals.Average();
        var varSum = vals.Sum(x => (x - mean) * (x - mean));
        var variance = varSum / (vals.Count - 1);
        return Math.Sqrt(variance) * Math.Sqrt(12.0);
    }
}
