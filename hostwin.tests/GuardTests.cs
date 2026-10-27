using Gpc.HostWin.Safety;

namespace Gpc.HostWin.Tests;

public class GuardTests
{
    [Theory]
    [InlineData("EasyAntiCheat_EOS.exe")]
    [InlineData("BEService")]
    [InlineData("vgc")]
    [InlineData("cod")]
    public void StopsWhenAnAntiCheatProcessRuns(string process)
    {
        var g = new GuardState();
        Assert.Contains("anti-cheat", g.Check(new[] { "explorer", process, "gpcrun" }));
    }

    [Fact]
    public void KeepsRunningWithOrdinaryProcesses()
    {
        var g = new GuardState();
        Assert.Null(g.Check(new[] { "explorer", "Bully", "steam", "chrome" }));
    }

    [Fact]
    public void OnlyModeWaitsForTheGameThenStopsWhenItCloses()
    {
        var g = new GuardState("Bully.exe");
        Assert.Null(g.Check(new[] { "explorer" }));              // not started yet: wait
        Assert.Null(g.Check(new[] { "explorer", "Bully" }));     // running
        Assert.Equal("'bully' closed", g.Check(new[] { "explorer" }));
    }

    [Fact]
    public void AntiCheatWinsEvenInOnlyMode()
    {
        var g = new GuardState("Bully");
        Assert.Contains("anti-cheat", g.Check(new[] { "Bully", "BEService_x64" }));
    }

    [Fact]
    public void ExtraBlockedNamesAreHonoured()
    {
        var g = new GuardState(extraBlocked: new[] { "SomeGame.exe" });
        Assert.NotNull(g.Check(new[] { "somegame" }));
    }
}
