namespace ClawInv.Core.Avanza;

public static class AvanzaChartConverter
{
    /// <summary>
    /// Converts Avanza chart points (unix ms + y=percent development since first datapoint)
    /// into a normalized NAV series where first point is NAV=1.0.
    ///
    /// This yields comparable returns even if absolute NAV isn't provided.
    /// </summary>
    public static IReadOnlyList<NavPoint> ToNormalizedNav(AvanzaChartResponse chart, TimeZoneInfo tz)
    {
        var points = chart.DataSerie
            .Where(p => p.Y.HasValue)
            .Select(p =>
            {
                var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(p.X), tz);
                var date = DateOnly.FromDateTime(local.DateTime);
                var nav = 1m + (decimal)p.Y.Value / 100m;
                return new NavPoint(date, nav);
            })
            .OrderBy(p => p.Date)
            .GroupBy(p => p.Date)
            .Select(g => g.Last())
            .ToList();

        if (points.Count == 0)
            return points;

        // normalize so first value is 1.0
        var first = points[0].Nav;
        if (first == 0m)
            return points;

        return points.Select(p => p with { Nav = p.Nav / first }).ToList();
    }

    public static TimeZoneInfo GetStockholmTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Local; }
        }
    }
}
