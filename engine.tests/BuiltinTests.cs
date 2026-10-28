using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

public class BuiltinTests
{
    [Fact]
    public void DataReadersUseByteOffsetsLittleEndian()
    {
        const string g = "data(20, 42, 35, 255, 1, 100, 0, 86);";
        Assert.Equal(-1, Eval("dint8(3)", g));
        Assert.Equal(255, Eval("duint8(3)", g));
        const string g2 = "data(0xFF, 0x7F, 0x34, 0x12, 0x78, 0x56, 0x34, 0x12);";
        Assert.Equal(-1, Eval("dint8(0)", g2));
        Assert.Equal(0x1234, Eval("dint16(2)", g2));
        Assert.Equal(0x12345678, Eval("dint32(4)", g2));
        const string g3 = "data(0x12, 0x1234, 0x12345678);";
        Assert.Equal(0x12, Eval("duint8(0)", g3));
        Assert.Equal(0x1234, Eval("duint16(1)", g3));
        Assert.Equal(0x12345678, Eval("dint32(3)", g3));
        Assert.Equal(-1, Eval("dchar(0)", "data(255);"));
        Assert.Equal(65535, Eval("duint16(0)", "data(0xFF, 0xFF);"));
    }

    [Fact]
    public void BitBuiltins()
    {
        Assert.Equal(9, Eval("get_bits(0x90, 4, 0x0F)"));
        Assert.Equal(1, Eval("test_bit(8, 3)"));
        var m = Run("int p; int f; int g = 15; main { set_bits(p, 9, 4, 0x0F); set_bit(f, 3); clear_bit(g, 2); set_val(TRACE_1, p); set_val(TRACE_2, f); set_val(TRACE_3, g); }");
        Assert.Equal(0x90, m.Trace(1));
        Assert.Equal(8, m.Trace(2));
        Assert.Equal(11, m.Trace(3));
    }

    [Theory]
    [InlineData("pow(5, 3)", 125)]
    [InlineData("isqrt(10)", 3)]
    [InlineData("abs(-7)", 7)]
    [InlineData("inv(7)", -7)]
    [InlineData("clamp(150, 0, 100)", 100)]
    [InlineData("min(3, -2)", -2)]
    [InlineData("max(3, -2)", 3)]
    [InlineData("sin(30)", 5000)]
    [InlineData("sin(45)", 7071)]
    [InlineData("sin(90)", 10000)]
    [InlineData("sin(270)", -10000)]
    [InlineData("sin(361)", 175)]
    [InlineData("cos(0)", 10000)]
    [InlineData("cos(30)", 8660)]
    [InlineData("cos(60)", 5000)]
    [InlineData("cos(180)", -10000)]
    [InlineData("sin(-90)", -10000)]
    [InlineData("atan2(1, 1)", 45)]
    [InlineData("asin(5000)", 30)]
    public void MathBuiltins(string expr, int expected) => Assert.Equal(expected, Eval(expr));

    [Fact]
    public void RandomIsInclusiveAndSeeded()
    {
        var a = Run("int i; int lo = 100; int hi; int v; main { for (i = 0; i < 500; i++) { v = random(1, 3); lo = min(lo, v); hi = max(hi, v); } set_val(TRACE_1, lo); set_val(TRACE_2, hi); set_val(TRACE_3, v); }");
        var b = Run("int i; int lo = 100; int hi; int v; main { for (i = 0; i < 500; i++) { v = random(1, 3); lo = min(lo, v); hi = max(hi, v); } set_val(TRACE_1, lo); set_val(TRACE_2, hi); set_val(TRACE_3, v); }");
        Assert.Equal(1, a.Trace(1));
        Assert.Equal(3, a.Trace(2));
        Assert.Equal(a.Trace(3), b.Trace(3));
    }

    [Fact]
    public void PvarFallsBackToDefaultOutsideRange()
    {
        var m = Run("int a; int b; main { set_pvar(SPVAR_1, 50); a = get_pvar(SPVAR_1, 0, 100, 7); b = get_pvar(SPVAR_1, 0, 10, 7); set_val(TRACE_1, a); set_val(TRACE_2, b); }");
        Assert.Equal(50, m.Trace(1));
        Assert.Equal(7, m.Trace(2));
    }

    [Fact]
    public void RumbleOverrideAndBlock()
    {
        var m = new Vm.Machine(Build("main { if (get_ival(PS4_CROSS)) set_rumble(RUMBLE_A, 80); if (get_ival(PS4_CIRCLE)) block_rumble(); }"));
        m.RumbleFromGame[0] = 30;
        m.RumbleFromGame[1] = 40;
        m.RunCycle();
        Assert.Equal(30, m.RumbleOut(0));
        m.Input[Runtime.Identifiers.CROSS] = 100;
        m.Input[Runtime.Identifiers.CIRCLE] = 100;
        m.RunCycle();
        Assert.Equal(80, m.RumbleOut(0));
        Assert.Equal(0, m.RumbleOut(1));
    }
}

public class MoreBuiltinTests
{
    [Fact]
    public void SetRemapperMovesAButtonAtRunTime()
    {
        var m = new Vm.Machine(Harness.Build("init { set_remapper(PS4_CROSS, PS4_CIRCLE); } main { }"));
        m.Input[Runtime.Identifiers.CROSS] = 100;
        m.RunCycle();
        Assert.Equal(100, m.Output[Runtime.Identifiers.CIRCLE]);
        Assert.Equal(100, m.Output[Runtime.Identifiers.CROSS]); // the source keeps its own output (CronusMAX manual)
    }

    [Fact]
    public void AdaptiveTriggerRecordsRoundTrip()
    {
        var m = Harness.Run("""
            const ps5adt HEAVY = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            int a; int b; int c;
            main {
                adt_setx(PS5_R2, addr(HEAVY));
                a = adt_cmp(PS5_R2, addr(HEAVY));
                set_adt(PS5_R2, PS5_ADT_FREQ, 99);
                b = adt_cmp(PS5_R2, addr(HEAVY));
                c = get_adt(PS5_R2, PS5_ADT_FREQ);
                set_val(TRACE_1, a); set_val(TRACE_2, b); set_val(TRACE_3, c);
            }
            """);
        Assert.Equal(1, m.Trace(1));
        Assert.Equal(0, m.Trace(2));
        Assert.Equal(99, m.Trace(3));
    }

    [Theory]
    [InlineData("KEY_A", 4)]
    [InlineData("KEY_SPACE", 44)]
    [InlineData("MOD_LALT", 4)]
    [InlineData("MOD_RGUI", 128)]
    [InlineData("PS5_ADT_STRENGTH_HIGH", 6)]
    [InlineData("PS5_HAPTICS_L", 26)]
    public void GuideNamedConstantsExist(string name, int expected) => Assert.Equal(expected, Harness.Eval(name));

    [Fact]
    public void LegacyAndDeviceBuiltinsCompile()
    {
        var r = Compile.GpcCompiler.Compile("int x; main { x = ps4_authtimeout(); output_reconnection(); x = get_info(ZENLINK_STATUS); x = wiir_offscreen(); adt_off(PS5_L2); }");
        Assert.True(r.Success, string.Join("\n", r.Errors));
    }
}
