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
