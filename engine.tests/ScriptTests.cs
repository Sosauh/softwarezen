using Gpc.Engine.Vm;
using static Gpc.Engine.Runtime.Identifiers;
using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

/// <summary>The scripts shipped in /scripts and /probes must compile and behave.</summary>
public class ScriptTests
{
    public static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SoftwareZen.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    static (int x, int y) Stick(Machine m, int x, int y)
    {
        m.Input[LX] = x;
        m.Input[LY] = y;
        m.RunCycle();
        return (m.Output[LX], m.Output[LY]);
    }

    [Fact]
    public void AntiDriftHidesSmallOffsetsAndKeepsFullRange()
    {
        var m = new Machine(Build(RepoFile("scripts/anti_drift.gpc")));
        Assert.Equal((0, 0), Stick(m, 5, -4));     // drift inside the deadzone of 8
        Assert.Equal((0, 0), Stick(m, 8, 0));
        Assert.Equal((100, 0), Stick(m, 100, 0));  // full push still reaches 100
        Assert.Equal((50, 0), Stick(m, 54, 0));    // (54 - 8) * 100 / 92
        var (x, y) = Stick(m, -70, 70);            // diagonal keeps its direction
        Assert.Equal(-x, y);
        Assert.InRange(y, 60, 100);
    }

    [Fact]
    public void AntiDriftLeavesButtonsAlone()
    {
        var m = new Machine(Build(RepoFile("scripts/anti_drift.gpc")));
        m.Input[CROSS] = 100;
        m.Input[R2] = 73;
        m.RunCycle();
        Assert.Equal(100, m.Output[CROSS]);
        Assert.Equal(73, m.Output[R2]);
    }
}

public class TurboScriptTests
{
    [Fact]
    public void HoldingCrossFiresTenPressesASecond()
    {
        var m = new Machine(Build(ScriptTests.RepoFile("scripts/turbo_cross.gpc")));
        m.Input[CROSS] = 100;
        var seen = new List<int>();
        for (int i = 0; i < 100; i++) { m.RunCycle(); seen.Add(m.Output[CROSS]); } // one second
        Assert.Equal(new[] { 100, 100, 100, 100, 100, 0, 0, 0, 0, 0 }, seen.Take(10));
        int presses = seen.Zip(seen.Skip(1), (a, b) => a == 0 && b == 100 ? 1 : 0).Sum() + 1;
        Assert.Equal(10, presses);
    }

    [Fact]
    public void ReleasingCrossStopsImmediately()
    {
        var m = new Machine(Build(ScriptTests.RepoFile("scripts/turbo_cross.gpc")));
        m.Input[CROSS] = 100;
        for (int i = 0; i < 3; i++) m.RunCycle();
        m.Input[CROSS] = 0;
        m.RunCycle();
        Assert.Equal(0, m.Output[CROSS]);
        m.Input[CROSS] = 100; // pressing again starts a fresh press, not mid-cycle
        m.RunCycle();
        Assert.Equal(100, m.Output[CROSS]);
    }

    [Fact]
    public void OtherButtonsPassThrough()
    {
        var m = new Machine(Build(ScriptTests.RepoFile("scripts/turbo_cross.gpc")));
        m.Input[CIRCLE] = 100;
        m.Input[LX] = -60;
        m.RunCycle();
        Assert.Equal(100, m.Output[CIRCLE]);
        Assert.Equal(-60, m.Output[LX]);
    }
}
