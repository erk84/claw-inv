using ClawInv.Core;
using Xunit;

namespace ClawInv.Tests;

public sealed class MetricsTests
{
    [Fact]
    public void ComputeMetrics_Basic()
    {
        var nav = new List<NavPoint>
        {
            new(new DateOnly(2020, 1, 1), 100m),
            new(new DateOnly(2021, 1, 1), 110m),
            new(new DateOnly(2022, 1, 1), 121m),
        };

        var m = MetricsCalculator.Compute(nav);
        Assert.InRange(m.Cagr, 0.09m, 0.11m);
    }
}
