using Gpc.Engine.Vm;
using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

/// <summary>
/// Pins what the engine answers for each probe today. When a probe is measured on a Zen and
/// differs, change the matching CompatFlags default, then update the row here and in probes/README.md.
/// </summary>
public class ProbeTests
{
    [Theory]
    [InlineData("p1a_divide_modulo.gpc", 0, 0, -3, -1, -3, 1)]
    [InlineData("p1b_overflow_literals.gpc", int.MinValue, int.MinValue, int.MinValue, 3, 2, 1)]
    [InlineData("p1c_shifts.gpc", -4, -1, int.MinValue, 1, 2, -32768)]
    [InlineData("p2a_language_rules.gpc", 5, 7, 0, 0, 12, 0)]
    [InlineData("p2b_aliasing.gpc", 77, 88, 50, 25, 0, 0)]
    [InlineData("p3_value_reads.gpc", 40, 0, 40, 55, 30, 0)]
    public void EngineAnswer(string file, int t1, int t2, int t3, int t4, int t5, int t6)
    {
        var m = new Machine(Build(ScriptTests.RepoFile("probes/" + file)));
        for (int i = 0; i < 3; i++) m.RunCycle();
        Assert.Equal(new[] { t1, t2, t3, t4, t5, t6 }, Enumerable.Range(1, 6).Select(m.Trace).ToArray());
    }
}
