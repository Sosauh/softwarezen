using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

public class DiagnosticTests
{
    [Theory]
    [InlineData("main { x = 1; }", "GZ4002")]
    [InlineData("main { get_ival(PS4_CROSS); }", "GZ5135")]
    [InlineData("main { wait(10); }", "GZ4010")]
    [InlineData("main { while (1) { } }", "GZ4027")]
    [InlineData("main { int x; }", "GZ4003")]
    [InlineData("int a[3] = 1; main { }", "GZ2100")]
    [InlineData("const int8 v[] = {100, 200}; main { }", "GZ5114")]
    [InlineData("const uint8 v[3] = {1, 2, 3}; main { }", "GZ2130")]
    [InlineData("enum { A, B, }; main { }", "GZ2101")]
    [InlineData("main { 1 == 2; }", "GZ5145")]
    [InlineData("main { return; }", "GZ4005")]
    [InlineData("function set_val(a) { } main { }", "GZ4001")]
    [InlineData("int TRUE; main { }", "GZ4001")]
    [InlineData("int a; int a; main { }", "GZ4000")]
    [InlineData("main { combo_run(nope); }", "GZ4009")]
    [InlineData("main { foo(); }", "GZ5128")]
    [InlineData("main { set_val(1); }", "GZ5130")]
    [InlineData("combo c { call(c); } main { combo_run(c); }", "GZ4015")]
    [InlineData("int a[2]; main { a[5] = 1; }", "GZ4023")]
    [InlineData("main { if (1) { } else { } } x = 1;", "GZ2103")]
    [InlineData("main { switch (1) { case 1: { } case 1: { } } }", "GZ5139")]
    [InlineData("function f() { return 1; } function g() { if (1) return; return 2; } main { }", "GZ5125")]
    [InlineData("function f() { } main { set_val(TRACE_1, f()); }", "GZ5134")]
    public void ReportsError(string src, string code) => Assert.Contains(code, ErrorCodes(src));

    [Fact]
    public void ReportsSeveralErrorsPerFile()
    {
        var codes = ErrorCodes("main { a = 1; b = 2; get_val(PS4_CROSS); } combo c { wait(10) } function f( { }");
        Assert.True(codes.Count >= 3, string.Join(",", codes));
    }

    [Fact]
    public void DiagnosticsCarryLineAndColumn()
    {
        var r = Compile.GpcCompiler.Compile("main {\n  set_val(PS4_CROSS, 100);\n  y = 3;\n}");
        var d = Assert.Single(r.Errors);
        Assert.Equal(3, d.Span.Line);
        Assert.Equal(3, d.Span.Column);
    }

    [Fact]
    public void WarnsOnDecimalLiteralRounding()
    {
        var r = Compile.GpcCompiler.Compile("main { set_val(TRACE_1, 1.5); }");
        Assert.True(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Code == "GZ1008");
    }
}

public class ReplaceableBuiltinTests
{
    [Fact]
    public void ScriptsCanDefineTheirOwnSinAndCos()
    {
        var m = Harness.Run("function sin(a) { return 7; } function cos(a) { return a + 1; } main { set_val(TRACE_1, sin(30)); set_val(TRACE_2, cos(4)); }");
        Assert.Equal(7, m.Trace(1));
        Assert.Equal(5, m.Trace(2));
    }
}
