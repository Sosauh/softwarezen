using Gpc.HostWin.Ds5;

namespace Gpc.HostWin.Tests;

public class DriftAnalyzerTests
{
    [Theory]
    [InlineData(128, 0)]
    [InlineData(0, -100)]
    [InlineData(255, 100)]
    public void RawAxisMapsToGpcRange(byte raw, int expected) => Assert.Equal(expected, DriftAnalyzer.ToGpc(raw));

    [Fact]
    public void CentredStickSuggestsOnlyTheMargin()
    {
        var a = new DriftAnalyzer();
        for (int i = 0; i < 100; i++) a.Add(Ds5Device.Neutral);
        Assert.Equal(0, a.Left.MaxRadius);
        Assert.Equal(2, a.Left.SuggestedDeadzone());
    }

    [Fact]
    public void WanderingStickIsMeasured()
    {
        var a = new DriftAnalyzer();
        var s = Ds5Device.Neutral;
        a.Add(s);
        s.RightX = 128 + 13; // about +10 in GPC units
        a.Add(s);
        Assert.Equal(10, a.Right.MaxRadius);
        Assert.Equal(12, a.Right.SuggestedDeadzone());
        Assert.Equal(0, a.Left.MaxRadius);
    }
}
