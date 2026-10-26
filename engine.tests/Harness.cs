using Gpc.Engine.Compile;
using Gpc.Engine.Runtime;
using Gpc.Engine.Vm;

namespace Gpc.Engine.Tests;

static class Harness
{
    public static CompiledScript Build(string src, CompatFlags? flags = null)
    {
        var r = GpcCompiler.Compile(src, flags);
        Assert.True(r.Success, "compile failed:\n" + string.Join("\n", r.Errors));
        return r.Script!;
    }

    public static Machine Run(string src, int cycles = 1, CompatFlags? flags = null)
    {
        var m = new Machine(Build(src, flags));
        for (int i = 0; i < cycles; i++) m.RunCycle();
        return m;
    }

    /// <summary>Evaluates one expression in main and returns it through TRACE_1.</summary>
    public static int Eval(string expr, string globals = "", CompatFlags? flags = null) =>
        Run($"{globals}\nmain {{ set_val(TRACE_1, {expr}); }}", 1, flags).Trace(1);

    public static List<string> ErrorCodes(string src) =>
        GpcCompiler.Compile(src).Errors.Select(d => d.Code).ToList();
}
