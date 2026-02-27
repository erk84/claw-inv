using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace ClawInv.Core;

public static class CsvNavReader
{
    private sealed class Row
    {
        public string Date { get; set; } = "";
        public decimal NAV { get; set; }
    }

    public static IReadOnlyList<NavPoint> Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"CSV not found: {path}");

        using var reader = new StreamReader(path);
        var cfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectColumnCountChanges = true,
        };

        using var csv = new CsvReader(reader, cfg);
        var rows = csv.GetRecords<Row>().ToList();

        var points = new List<NavPoint>(rows.Count);
        foreach (var r in rows)
        {
            if (!DateOnly.TryParse(r.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw new FormatException($"Invalid Date '{r.Date}' (expected YYYY-MM-DD)");
            points.Add(new NavPoint(d, r.NAV));
        }

        return points
            .OrderBy(p => p.Date)
            .GroupBy(p => p.Date)
            .Select(g => g.Last())
            .ToList();
    }
}
