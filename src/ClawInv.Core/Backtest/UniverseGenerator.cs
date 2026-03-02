using ClawInv.Core.Avanza;

namespace ClawInv.Core.Backtest;

public sealed class UniverseGenerator
{
    private readonly AvanzaClient _avanza;

    public UniverseGenerator(AvanzaClient avanza)
    {
        _avanza = avanza;
    }

    public async Task<Universe> GenerateFromFundListAsync(
        int targetFunds,
        int ratingLimit,
        double totalFeeLimit,
        int riskLimit,
        CancellationToken ct = default)
    {
        var seen = new Dictionary<string, FundRef>();

        var startIndex = 0;
        var total = int.MaxValue;

        // We page until we have enough or no more funds.
        while (startIndex < total && seen.Count < targetFunds)
        {
            var page = await _avanza.GetFundListPageAsync(startIndex, maxTotalFee: totalFeeLimit, ct);
            total = page.TotalNoFunds;

            if (page.FundListViews.Count == 0)
                break;

            foreach (var f in page.FundListViews)
            {
                if (string.IsNullOrWhiteSpace(f.OrderbookId))
                    continue;

                var ok = f.Rating.HasValue && f.Rating.Value >= ratingLimit
                      && f.TotalFee.HasValue && f.TotalFee.Value <= totalFeeLimit
                      && f.Risk.HasValue && f.Risk.Value >= riskLimit;

                if (!ok)
                    continue;

                if (!seen.ContainsKey(f.OrderbookId))
                    seen[f.OrderbookId] = new FundRef(f.Name, f.OrderbookId);

                if (seen.Count >= targetFunds)
                    break;
            }

            startIndex += page.FundListViews.Count;
        }

        return new Universe(seen.Values.OrderBy(x => x.Name).ToList());
    }

    /// <summary>
    /// Generate an unbounded universe: include all funds that match the criteria.
    /// </summary>
    public async Task<Universe> GenerateAllFromFundListAsync(
        int ratingLimit,
        double totalFeeLimit,
        int riskLimit,
        CancellationToken ct = default)
    {
        var seen = new Dictionary<string, FundRef>();

        var startIndex = 0;
        var total = int.MaxValue;

        while (startIndex < total)
        {
            var page = await _avanza.GetFundListPageAsync(startIndex, maxTotalFee: totalFeeLimit, ct);
            total = page.TotalNoFunds;

            if (page.FundListViews.Count == 0)
                break;

            foreach (var f in page.FundListViews)
            {
                if (string.IsNullOrWhiteSpace(f.OrderbookId))
                    continue;

                var ok = f.Rating.HasValue && f.Rating.Value >= ratingLimit
                      && f.TotalFee.HasValue && f.TotalFee.Value <= totalFeeLimit
                      && f.Risk.HasValue && f.Risk.Value >= riskLimit;

                if (!ok)
                    continue;

                if (!seen.ContainsKey(f.OrderbookId))
                    seen[f.OrderbookId] = new FundRef(f.Name, f.OrderbookId);
            }

            startIndex += page.FundListViews.Count;
        }

        return new Universe(seen.Values.OrderBy(x => x.Name).ToList());
    }
}
