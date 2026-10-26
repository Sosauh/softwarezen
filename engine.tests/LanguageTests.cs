using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

/// <summary>Vectors stated by the Zen GPC guide (language and arithmetic pages).</summary>
public class LanguageTests
{
    [Theory]
    [InlineData("10 + 5", 15)]
    [InlineData("10 - 5", 5)]
    [InlineData("10 * 5", 50)]
    [InlineData("10 / 3", 3)]
    [InlineData("10 % 3", 1)]
    [InlineData("3 / 4", 0)]
    [InlineData("5 -1", 4)]
    [InlineData("(40 * 125) / 100", 50)]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("1 << 4 | 1", 17)]
    [InlineData("!0 + !5", 1)]
    [InlineData("~0", -1)]
    [InlineData("1 ^^ 1", 0)]
    [InlineData("1 ^^ 0", 1)]
    [InlineData("3 > 2 && 2 > 1", 1)]
    [InlineData("0 || 0", 0)]
    [InlineData("1 ? 7 : 9", 7)]
    [InlineData("0x7FFFFFFF + 1", int.MinValue)]
    [InlineData("0xFFFFFFFF", -1)]
    [InlineData("0b1010", 10)]
    [InlineData("TRUE + TRUE", 2)]
    [InlineData("2.5", 3)]
    public void ConstantExpressions(string expr, int expected) => Assert.Equal(expected, Eval(expr));

    [Fact]
    public void BitwiseAndBindsLooserThanEquality()
    {
        // flags & MASK == MASK parses as flags & (MASK == MASK)
        Assert.Equal(1, Eval("f & 6 == 6", "int f = 7;"));
    }

    [Fact]
    public void RuntimeArithmeticMatchesFolding()
    {
        Assert.Equal(3, Eval("a / b", "int a = 10; int b = 3;"));
        Assert.Equal(1, Eval("a % b", "int a = 10; int b = 3;"));
        Assert.Equal(int.MinValue, Eval("a + 1", "int a = 2147483647;"));
    }

    [Fact]
    public void DivideByZeroGuardedByTernaryDoesNotDivide()
    {
        Assert.Equal(0, Eval("d != 0 ? n / d : 0", "int n = 9; int d = 0;"));
    }

    [Fact]
    public void PostIncrementYieldsOldPreIncrementYieldsNew()
    {
        var m = Run("int a = 5; int b; int c; main { b = a++; c = ++a; set_val(TRACE_1, b); set_val(TRACE_2, c); set_val(TRACE_3, a); }");
        Assert.Equal(5, m.Trace(1));
        Assert.Equal(7, m.Trace(2));
        Assert.Equal(7, m.Trace(3));
    }

    [Fact]
    public void PostIncrementFlagRestoresOriginalCompiler()
    {
        var flags = new Runtime.CompatFlags { PostIncrementYieldsOld = false };
        Assert.Equal(6, Eval("a++", "int a = 5;", flags));
    }

    [Fact]
    public void ChainedAssignment()
    {
        var m = Run("int a; int b; main { a = b = 5; set_val(TRACE_1, a); set_val(TRACE_2, b); }");
        Assert.Equal(5, m.Trace(1));
        Assert.Equal(5, m.Trace(2));
    }

    [Fact]
    public void CompoundAssignmentsIncludingLogical()
    {
        var m = Run("""
            int a = 10; int b = 3; int c = 1; int d = 0;
            main { a += 5; a <<= 1; b *= b; c &&= 0; d ||= 7;
                   set_val(TRACE_1, a); set_val(TRACE_2, b); set_val(TRACE_3, c); set_val(TRACE_4, d); }
            """);
        Assert.Equal(30, m.Trace(1));
        Assert.Equal(9, m.Trace(2));
        Assert.Equal(0, m.Trace(3));
        Assert.Equal(1, m.Trace(4));
    }

    [Fact]
    public void EnumValuesRestartAfterExplicitValue()
    {
        const string g = "enum { A, B, C = 10, D, E = 20, F };";
        Assert.Equal(0, Eval("A", g));
        Assert.Equal(1, Eval("B", g));
        Assert.Equal(11, Eval("D", g));
        Assert.Equal(21, Eval("F", g));
        Assert.Equal(3, Eval("COUNT", "enum { OFF, STD, ALT, COUNT };"));
    }

    [Fact]
    public void DefinesResolveInAnyOrder()
    {
        Assert.Equal(50, Eval("HALF", "define HALF = FULL / 2; define FULL = 100;"));
        Assert.Equal(2, Eval("X", "define X = sizeof(int16);"));
    }

    [Fact]
    public void VariablesAliasSequentialSlots()
    {
        var m = Run("int first; int second; main { first[1] = 25; set_val(TRACE_1, second); }");
        Assert.Equal(25, m.Trace(1));
        m = Run("int alias[0]; int value; main { alias[0] = 50; set_val(TRACE_1, value); }");
        Assert.Equal(50, m.Trace(1));
    }

    [Fact]
    public void ArraysAndLoops()
    {
        var m = Run("""
            int arr[5]; int i; int sum;
            main {
                for (i = 0; i < 5; i++) arr[i] = i * i;
                sum = 0; i = 0;
                while (1) { if (i >= 5) break; if (i == 2) { i++; continue; } sum += arr[i]; i++; }
                set_val(TRACE_1, sum);
            }
            """);
        Assert.Equal(0 + 1 + 9 + 16, m.Trace(1));
    }

    [Fact]
    public void DoWhileRunsAtLeastOnce()
    {
        Assert.Equal(1, Eval("n", "int n; init { do { n++; } while (0); }"));
    }

    [Fact]
    public void SwitchWithBreakFallthroughAndDefault()
    {
        const string src = """
            int v; int r;
            main {
                r = 0;
                switch (v) {
                    case 1: { r += 1; break; }
                    case 2: { r += 2; }
                    case 3: { r += 3; break; }
                    default: { r = 99; }
                }
                set_val(TRACE_1, r);
            }
            """;
        foreach (var (v, want) in new[] { (1, 1), (2, 5), (3, 3), (7, 99) })
            Assert.Equal(want, Run(src.Replace("int v;", $"int v = {v};")).Trace(1));
    }

    [Fact]
    public void FunctionsReturnValuesAndDefaultZero()
    {
        var m = Run("""
            main { set_val(TRACE_1, add(2, 3)); set_val(TRACE_2, nothing()); set_val(TRACE_3, fact(5)); }
            function add(a, b) { return a + b; }
            function nothing() { if (0) return 1; }
            function fact(n) { if (n <= 1) return 1; return n * fact(n - 1); }
            """);
        Assert.Equal(5, m.Trace(1));
        Assert.Equal(0, m.Trace(2));
        Assert.Equal(120, m.Trace(3));
    }

    [Fact]
    public void ParameterShadowsGlobal()
    {
        Assert.Equal(7, Eval("f(7)", "int x = 1; function f(x) { return x; }"));
    }

    [Fact]
    public void ConstArrays1DAnd2DAndSizeof()
    {
        const string g = """
            const uint8 BUTTONS[] = { PS4_CROSS, PS4_CIRCLE, PS4_SQUARE };
            const int16 GRID[][] = { { 1, 2, 3 }, { -4, 5, 600 } };
            """;
        Assert.Equal(3, Eval("sizeof(BUTTONS)", g));
        Assert.Equal(18, Eval("BUTTONS[1]", g));
        Assert.Equal(600, Eval("GRID[1][2]", g));
        Assert.Equal(-4, Eval("GRID[1][0]", g));
        Assert.Equal(3, Eval("sizeof(GRID[0])", g));
    }

    [Fact]
    public void ShortCircuitSkipsRightOperand()
    {
        var m = Run("int n; main { if (0 && bump()) {} if (1 || bump()) {} set_val(TRACE_1, n); } function bump() { n++; return 1; }");
        Assert.Equal(0, m.Trace(1));
    }

    [Fact]
    public void ManyInitAndMainBlocksConcatenate()
    {
        var m = Run("int a; init { a = 1; } init { a = a * 10; } main { set_val(TRACE_1, a); } main { set_val(TRACE_2, a + 1); }");
        Assert.Equal(10, m.Trace(1));
        Assert.Equal(11, m.Trace(2));
    }

    [Fact]
    public void MissingSemicolonsAreTolerated()
    {
        var r = Compile.GpcCompiler.Compile("int a\nmain { a = 2\n set_val(TRACE_1, a) }");
        Assert.True(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Code == "GZ2108");
    }
}
