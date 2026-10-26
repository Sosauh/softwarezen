using Gpc.HostWin.Ds5;
using static Gpc.Engine.Runtime.Identifiers;

namespace Gpc.HostWin.Tests;

public class EngineBridgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(255)]
    public void AxisRoundTripStaysWithinOneStep(byte raw)
    {
        byte back = EngineBridge.AxisByte(DriftAnalyzer.ToGpc(raw));
        Assert.InRange(back, raw - 2, raw + 2);
    }

    [Fact]
    public void PassthroughOfInputGivesSameDs4AsDirectMapping()
    {
        var s = Ds5Device.Neutral;
        s.Cross = s.L1 = true;
        s.Hat = Ds5Hat.NE;
        s.R2 = 255;
        s.LeftX = 0;
        var slots = new int[SlotCount];
        EngineBridge.WriteInput(s, slots);
        var viaEngine = EngineBridge.ReadOutput(slots);
        var direct = Ds4.Ds5ToDs4.Map(s);
        Assert.Equal(direct.Cross, viaEngine.Cross);
        Assert.Equal(direct.L1, viaEngine.L1);
        Assert.True(viaEngine.Up && viaEngine.Right && !viaEngine.Down);
        Assert.Equal(255, viaEngine.R2);
        Assert.Equal(0, viaEngine.LeftX);
        Assert.Equal(128, viaEngine.RightX);
        Assert.Equal(100, slots[CROSS]);
    }
}
