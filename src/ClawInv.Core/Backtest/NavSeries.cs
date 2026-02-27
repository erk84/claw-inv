namespace ClawInv.Core.Backtest;

public sealed class NavSeries
{
    public string Name { get; }
    public string OrderbookId { get; }
    public IReadOnlyList<NavPoint> Points { get; }

    public NavSeries(string name, string orderbookId, IReadOnlyList<NavPoint> points)
    {
        Name = name;
        OrderbookId = orderbookId;
        Points = points;
    }
}
