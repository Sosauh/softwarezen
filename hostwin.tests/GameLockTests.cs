using Gpc.HostWin.Safety;

namespace Gpc.HostWin.Tests;

public class GameLockTests
{
    [Fact]
    public void WithoutAGameItNeverStops()
    {
        var g = new GameLockState(null);
        Assert.Null(g.Check(new[] { "explorer", "BEService", "EasyAntiCheat" }));
    }

    [Fact]
    public void WaitsForTheGameThenStopsWhenItCloses()
    {
        var g = new GameLockState("Bully.exe");
        Assert.Null(g.Check(new[] { "explorer" }));              // not started yet: wait
        Assert.Null(g.Check(new[] { "explorer", "Bully" }));     // running
        Assert.Equal("'bully' closed", g.Check(new[] { "explorer" }));
    }

    [Fact]
    public void AntiCheatProcessesDoNotMatter()
    {
        var g = new GameLockState("eldenring");
        Assert.Null(g.Check(new[] { "eldenring", "EasyAntiCheat_EOS" }));
    }
}
