using Gpc.Engine.Runtime;
using Gpc.Engine.Vm;
using static Gpc.Engine.Runtime.Identifiers;
using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

/// <summary>Input layers, edges, timing and combos.</summary>
public class CycleTests
{
    [Fact]
    public void LastWriteInTheCycleIsSent()
    {
        var m = Run("main { set_val(PS4_RX, 25); set_val(PS4_RX, -40); }");
        Assert.Equal(-40, m.Output[RX]);
    }

    [Fact]
    public void InputLayers()
    {
        var m = new Machine(Build("""
            int v; int iv; int lv;
            main {
                set_val(PS4_RX, get_ival(PS4_RX) / 2);
                v = get_val(PS4_RX); iv = get_ival(PS4_RX); lv = get_lval(PS4_RX);
                set_val(TRACE_1, v); set_val(TRACE_2, iv); set_val(TRACE_3, lv);
            }
            """));
        m.Input[RX] = 80;
        m.RunCycle();
        Assert.Equal(40, m.Trace(1));
        Assert.Equal(80, m.Trace(2));
        Assert.Equal(0, m.Trace(3));
        m.Input[RX] = 60;
        m.RunCycle();
        Assert.Equal(30, m.Trace(1));
        Assert.Equal(40, m.Trace(3)); // previous cycle's final value
    }

    [Fact]
    public void EventPressAndReleaseFireForOneCycle()
    {
        var m = new Machine(Build("main { set_val(TRACE_1, event_press(PS4_CROSS)); set_val(TRACE_2, event_release(PS4_CROSS)); set_val(TRACE_3, get_ptime(PS4_CROSS)); }"));
        m.RunCycle();
        Assert.Equal(0, m.Trace(1));
        m.Input[CROSS] = 100;
        m.RunCycle();
        Assert.Equal(1, m.Trace(1));
        Assert.Equal(0, m.Trace(3));
        m.RunCycle();
        m.RunCycle();
        Assert.Equal(0, m.Trace(1));
        Assert.Equal(20, m.Trace(3));
        m.Input[CROSS] = 0;
        m.RunCycle();
        Assert.Equal(1, m.Trace(2));
    }

    [Fact]
    public void DoublePressDetectedWithBrtime()
    {
        var m = new Machine(Build("int hits; main { if (event_press(PS4_CROSS) && get_brtime(PS4_CROSS) < 300) hits++; set_val(TRACE_1, hits); }"));
        void Cycle(int cross) { m.Input[CROSS] = cross; m.RunCycle(); }
        Cycle(100); Cycle(0); Cycle(0); Cycle(100); // second press 20 ms after release
        Assert.Equal(1, m.Trace(1)); // first press after load does not count
    }

    [Fact]
    public void VmTctrlChangesIntervalAndRtime()
    {
        var m = new Machine(Build("main { vm_tctrl(-5); set_val(TRACE_1, get_rtime()); }"));
        Assert.Equal(5, m.RunCycle());
        m.RunCycle();
        Assert.Equal(5, m.Trace(1));
        m = new Machine(Build("main { vm_tctrl(40); }"));
        Assert.Equal(40, m.RunCycle()); // clamped to +30
    }

    [Fact]
    public void RapidFireComboTiming()
    {
        var m = new Machine(Build("""
            main { if (get_ival(PS4_R2)) combo_run(rapid); }
            combo rapid { set_val(PS4_R2, 100); wait(40); set_val(PS4_R2, 0); wait(30); }
            """));
        m.Input[R2] = 100;
        var seen = new List<int>();
        for (int i = 0; i < 14; i++) { m.RunCycle(); seen.Add(m.Output[R2]); }
        // 40 ms on (4 cycles), 30 ms off (3 cycles), repeat
        Assert.Equal(new[] { 100, 100, 100, 100, 0, 0, 0, 100, 100, 100, 100, 0, 0, 0 }, seen);
    }

    [Fact]
    public void ComboOverridesMainSameCycle()
    {
        var m = Run("main { set_val(PS4_CROSS, 100); combo_run(c); } combo c { set_val(PS4_CROSS, 0); wait(10); }");
        Assert.Equal(0, m.Output[CROSS]);
    }

    [Fact]
    public void ComboRunDoesNotRestartButRestartDoes()
    {
        const string body = "combo c { set_val(TRACE_1, 1); wait(30); set_val(TRACE_1, 2); wait(30); }";
        var run = new Machine(Build("int n; main { combo_run(c); set_val(TRACE_2, combo_current_step(c)); } " + body));
        var restart = new Machine(Build("main { combo_restart(c); } " + body));
        for (int i = 0; i < 4; i++) { run.RunCycle(); restart.RunCycle(); }
        Assert.Equal(2, run.Trace(1));
        Assert.Equal(1, restart.Trace(1));
    }

    [Fact]
    public void CallPausesParentUntilChildEnds()
    {
        var m = new Machine(Build("""
            init { combo_run(parent); }
            main { }
            combo parent { set_val(TRACE_1, 1); wait(10); call(child); set_val(TRACE_1, 3); wait(10); }
            combo child { set_val(TRACE_2, 2); wait(20); }
            """));
        var t1 = new List<int>();
        var t2 = new List<int>();
        for (int i = 0; i < 5; i++) { m.RunCycle(); t1.Add(m.Trace(1)); t2.Add(m.Trace(2)); }
        Assert.Equal(new[] { 1, 0, 0, 3, 0 }, t1);
        Assert.Equal(new[] { 0, 2, 2, 0, 0 }, t2);
    }

    [Fact]
    public void ComboStopSuspendResume()
    {
        var m = new Machine(Build("""
            int t;
            main {
                t++;
                if (t == 1) combo_run(c);
                if (t == 2) combo_suspend(c);
                if (t == 4) combo_resume(c);
                set_val(TRACE_2, combo_running(c));
                set_val(TRACE_3, combo_suspended(c));
            }
            combo c { set_val(TRACE_1, 7); wait(1000); }
            """));
        m.RunCycle();
        Assert.Equal(7, m.Trace(1));
        m.RunCycle();
        Assert.Equal(0, m.Trace(1)); // suspended: nothing written
        Assert.Equal(1, m.Trace(3));
        m.RunCycle(); m.RunCycle();
        Assert.Equal(7, m.Trace(1));
        Assert.Equal(1, m.Trace(2));
    }

    [Fact]
    public void RecordAndHoldModeReappliesWritesWithoutRerunningCode()
    {
        const string src = "int n; init { combo_run(c); } main { set_val(TRACE_2, n); } combo c { n++; set_val(TRACE_1, 5); wait(30); }";
        var hold = new Machine(Build(src, new CompatFlags { ComboStepMode = ComboStepMode.RecordAndHold }));
        var reexec = new Machine(Build(src));
        for (int i = 0; i < 3; i++) { hold.RunCycle(); reexec.RunCycle(); }
        // main reads n before the combo runs: hold ran the step once, re-execute ran it every cycle
        Assert.Equal(5, hold.Trace(1));
        Assert.Equal(1, hold.Trace(2));
        Assert.Equal(5, reexec.Trace(1));
        Assert.Equal(2, reexec.Trace(2));
    }

    [Fact]
    public void RemapAndUnmapApplyAfterScript()
    {
        var m = new Machine(Build("remap PS4_CROSS -> PS4_CIRCLE; remap PS4_CIRCLE -> PS4_CROSS; unmap PS4_SQUARE; main { set_val(TRACE_1, get_val(PS4_CROSS)); }"));
        m.Input[CROSS] = 100;
        m.Input[SQUARE] = 100;
        m.RunCycle();
        Assert.Equal(0, m.Output[CROSS]);
        Assert.Equal(100, m.Output[CIRCLE]);
        Assert.Equal(0, m.Output[SQUARE]);
        Assert.Equal(100, m.Trace(1)); // script still sees original identifiers
    }

    [Fact]
    public void BlockSuppressesForwardingForDuration()
    {
        var m = new Machine(Build("int t; main { t++; if (t == 1) block(PS4_CROSS, 30); }"));
        m.Input[CROSS] = 100;
        var seen = new List<int>();
        for (int i = 0; i < 5; i++) { m.RunCycle(); seen.Add(m.Output[CROSS]); }
        Assert.Equal(new[] { 0, 0, 0, 100, 100 }, seen);
    }

    [Fact]
    public void SwapAndBlockAllInputs()
    {
        var m = new Machine(Build("main { swap(PS4_CROSS, PS4_CIRCLE); }"));
        m.Input[CROSS] = 100;
        m.RunCycle();
        Assert.Equal(0, m.Output[CROSS]);
        Assert.Equal(100, m.Output[CIRCLE]);
        m = new Machine(Build("main { block_all_inputs(); set_val(PS4_R1, 100); }"));
        m.Input[CROSS] = 100;
        m.Input[LX] = 50;
        m.RunCycle();
        Assert.Equal(0, m.Output[CROSS]);
        Assert.Equal(0, m.Output[LX]);
        Assert.Equal(100, m.Output[R1]);
    }

    [Fact]
    public void PolarRoundTrip()
    {
        var m = new Machine(Build("main { set_polar(POLAR_RS, 90, 32767); set_val(TRACE_1, get_polar(POLAR_RS, POLAR_ANGLE)); }"));
        m.RunCycle();
        Assert.Equal(0, m.Output[RX]);
        Assert.Equal(100, m.Output[RY]); // 90 degrees points down
        Assert.Equal(90, m.Trace(1));
    }

    [Fact]
    public void RunawayLoopEndsTheCycleInsteadOfHanging()
    {
        var m = Run("int i; main { while (i >= 0) { i = i + 1 - 1; } }");
        Assert.Equal(FaultKind.InstructionBudget, m.LastFault);
    }

    [Fact]
    public void OutOfRangeDynamicIndexFaultsWithoutCrashing()
    {
        var m = Run("int a[2]; int i = 5000; main { a[i] = 1; set_val(TRACE_1, a[i]); }");
        Assert.Equal(FaultKind.IndexOutOfRange, m.LastFault);
        Assert.Equal(0, m.Trace(1));
    }

    [Fact]
    public void IdenticalInputGivesIdenticalOutput()
    {
        const string src = "int x; main { x = x * 31 + get_ival(PS4_LX); set_val(TRACE_1, x); if (event_press(PS4_CROSS)) combo_restart(c); } combo c { set_val(PS4_R2, random(0, 100)); wait(30); }";
        var a = new Machine(Build(src));
        var b = new Machine(Build(src));
        var rnd = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            int lx = rnd.Next(-100, 101), cross = rnd.Next(2) * 100;
            a.Input[LX] = b.Input[LX] = lx;
            a.Input[CROSS] = b.Input[CROSS] = cross;
            a.RunCycle();
            b.RunCycle();
            Assert.True(a.Output.SequenceEqual(b.Output), $"diverged at cycle {i}");
        }
    }

    [Fact]
    public void TickPathDoesNotAllocate()
    {
        var m = new Machine(Build("""
            int i; int arr[10];
            main {
                for (i = 0; i < 10; i++) arr[i] = get_ival(PS4_LX) * i;
                set_val(PS4_RX, f(arr[3]));
                if (event_press(PS4_CROSS)) combo_run(c);
                deadzone(PS4_LX, PS4_LY, DZ_CIRCLE, 10);
            }
            function f(v) { return clamp(v, -100, 100); }
            combo c { set_val(PS4_R2, 100); wait(20); set_val(PS4_R2, 0); wait(20); }
            """));
        for (int i = 0; i < 100; i++) m.RunCycle(); // warm up JIT
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            m.Input[LX] = i % 200 - 100;
            m.Input[CROSS] = (i / 7 % 2) * 100;
            m.RunCycle();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
