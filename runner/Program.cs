using System.Diagnostics;
using Gpc.Engine;
using Gpc.Engine.Compile;
using Gpc.Engine.Vm;
using Gpc.HostWin;
using Gpc.HostWin.Ds4;
using Gpc.HostWin.Ds5;
using Gpc.HostWin.Safety;
using Gpc.HostWin.Timing;

bool xbox = false;
string? onlyGame = null;
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--xbox") xbox = true;
    else if (args[i] == "--only" && i + 1 < args.Length) onlyGame = args[++i];
    else positional.Add(args[i]);
}
args = positional.ToArray();
var gameLock = new GameLockState(onlyGame);

return args.Length == 0 ? Usage() : args[0] switch
{
    "monitor" => Monitor(),
    "drift" => Drift(args.Length > 1 && int.TryParse(args[1], out int s) ? s : 10),
    "passthrough" => Passthrough(xbox, gameLock),
    "check" when args.Length > 1 => Check(args[1]),
    "sim" when args.Length > 1 => Sim(args[1], args.Length > 2 && int.TryParse(args[2], out int n) ? n : 100),
    "run" when args.Length > 1 => RunScript(args[1], xbox, gameLock),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        gpcrun monitor          live DS5 values in GPC units; Ctrl+C to quit
        gpcrun drift [seconds]  leave the controller untouched; measures stick drift and suggests a deadzone (default 10 s)
        gpcrun passthrough      DS5 -> virtual DS4 with no script (needs ViGEmBus); prints timing stats on exit
        gpcrun check <file.gpc> compile only and print diagnostics (works anywhere)
        gpcrun sim <file.gpc> [cycles]   run with a neutral controller and print TRACE_1..6 (works anywhere)
        gpcrun run <file.gpc>   DS5 -> script -> virtual DS4 (needs ViGEmBus; HidHide recommended)
        options for passthrough and run:
          --xbox             output a virtual Xbox 360 pad instead of a DS4 (for XInput-only games)
          --only <game.exe>  run only while that game runs; quit when it closes (recommended)
        """);
    return 2;
}

static Ds5Device OpenDs5()
{
    var dev = new Ds5Device();
    dev.Log += m => Console.Error.WriteLine(m);
    var sw = Stopwatch.StartNew();
    while (!dev.Connected && sw.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(50);
    if (!dev.Connected) Console.Error.WriteLine("No DualSense on USB yet; waiting. Is HidHide hiding it from this exe too?");
    return dev;
}

static string Bar(int v, int width = 21)
{
    int pos = (v + 100) * (width - 1) / 200;
    var c = new char[width];
    for (int i = 0; i < width; i++) c[i] = i == width / 2 ? '|' : '-';
    c[pos] = '#';
    return new string(c);
}

static int Monitor()
{
    using var dev = OpenDs5();
    var quit = false;
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit = true; };
    Console.Clear();
    while (!quit)
    {
        var s = dev.Latest;
        int lx = DriftAnalyzer.ToGpc(s.LeftX), ly = DriftAnalyzer.ToGpc(s.LeftY);
        int rx = DriftAnalyzer.ToGpc(s.RightX), ry = DriftAnalyzer.ToGpc(s.RightY);
        string pressed = string.Join(' ', new (bool on, string name)[]
        {
            (s.Cross, "CROSS"), (s.Circle, "CIRCLE"), (s.Square, "SQUARE"), (s.Triangle, "TRIANGLE"),
            (s.L1, "L1"), (s.R1, "R1"), (s.L3, "L3"), (s.R3, "R3"), (s.Create, "CREATE"), (s.Options, "OPTIONS"),
            (s.Ps, "PS"), (s.TouchpadClick, "TOUCH"), (s.Mute, "MUTE"),
            (Ds5InputReport.HatUp(s.Hat), "UP"), (Ds5InputReport.HatDown(s.Hat), "DOWN"),
            (Ds5InputReport.HatLeft(s.Hat), "LEFT"), (Ds5InputReport.HatRight(s.Hat), "RIGHT"),
        }.Where(b => b.on).Select(b => b.name));

        Console.SetCursorPosition(0, 0);
        Console.WriteLine($"connected: {dev.Connected,-5}  reports: {dev.ReportCount,-10}");
        Console.WriteLine($"LX {lx,4} {Bar(lx)}   RX {rx,4} {Bar(rx)}");
        Console.WriteLine($"LY {ly,4} {Bar(ly)}   RY {ry,4} {Bar(ry)}");
        Console.WriteLine($"L2 {s.L2 * 100 / 255,4}%  raw {s.L2,3}        R2 {s.R2 * 100 / 255,4}%  raw {s.R2,3}");
        Console.WriteLine($"buttons: {pressed}".PadRight(Console.WindowWidth > 0 ? Console.WindowWidth - 1 : 79));
        Console.WriteLine("Sticks should read 0 at rest and reach +-100; triggers 0 and 100%. Ctrl+C to quit.");
        Thread.Sleep(33);
    }
    return 0;
}

static int Drift(int seconds)
{
    using var dev = OpenDs5();
    Console.WriteLine($"Put the controller down and don't touch the sticks. Sampling for {seconds} s...");
    Thread.Sleep(1000); // let hands come off
    var a = new DriftAnalyzer();
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        if (dev.Connected) a.Add(dev.Latest);
        Thread.Sleep(5);
    }
    if (a.Left.Samples == 0) { Console.Error.WriteLine("No samples; controller not connected."); return 1; }

    foreach (var (name, d) in new[] { ("Left", a.Left), ("Right", a.Right) })
    {
        string verdict = d.MaxRadius <= 3 ? "fine" : d.MaxRadius <= 10 ? "mild drift" : "drift";
        Console.WriteLine($"{name} stick: rests at ({d.MeanX:F1}, {d.MeanY:F1}), wanders up to {d.MaxRadius:F1} -> {verdict}; suggested deadzone {d.SuggestedDeadzone()}");
    }
    Console.WriteLine("Put the suggested values into DEADZONE_L / DEADZONE_R in scripts/anti_drift.gpc.");
    return 0;
}

static IPadSink CreateSink(bool xbox) => xbox ? new VigemX360Sink() : new VigemDs4Sink();

/// <summary>With --only, stops the scheduler once that game closes.</summary>
static GameLock ArmGameLock(GameLockState state, DeadlineScheduler sched)
{
    var gameLock = new GameLock(state);
    gameLock.Tripped += reason =>
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Stopping: {reason}. Virtual controller unplugged and script stopped.");
        sched.Stop();
    };
    gameLock.Start();
    return gameLock;
}

/// <summary>Hide the DualSense from games while we run (needs HidHide set up as in the README).</summary>
static void HideDs5()
{
    var hh = new Gpc.HostWin.HidHide.HidHideCli();
    if (!hh.Installed) return;
    try { hh.CloakOn(); }
    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Could not turn HidHide cloaking on ({e.Message}); games may see two controllers.");
    }
}

/// <summary>On every exit, give the real controller back to games.</summary>
static void UnhideDs5()
{
    var hh = new Gpc.HostWin.HidHide.HidHideCli();
    if (!hh.Installed) return;
    try { hh.CloakOff(); }
    catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
        Console.Error.WriteLine($"Could not turn HidHide cloaking off ({e.Message}). Turn it off in HidHide's app to use your DualSense directly.");
    }
}

static int Passthrough(bool xbox, GameLockState lockState)
{
    var sched = new DeadlineScheduler();
    using var gameLock = ArmGameLock(lockState, sched);
    using var dev = OpenDs5();
    using var sink = CreateSink(xbox);
    sink.Connect();
    sink.Feedback += f => dev.SendFeedback(f.LargeMotor, f.SmallMotor, f.Red, f.Green, f.Blue);
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; sched.Stop(); };
    Console.WriteLine("Passthrough running at 10 ms. Ctrl+C to stop.");
    HideDs5();
    try
    {
        sched.Run(_ =>
        {
            var s = dev.Latest;
            sink.Submit(Ds5ToDs4.Map(s));
            return 10_000;
        });
    }
    finally
    {
        sink.Dispose(); // unplug the virtual pad first
        UnhideDs5();
    }
    Console.WriteLine(sched.Stats);
    return 0;
}

static CompiledScript? Load(string path)
{
    if (!File.Exists(path)) { Console.Error.WriteLine($"no such file: {path}"); return null; }
    var result = GpcCompiler.Compile(File.ReadAllText(path));
    foreach (var d in result.Diagnostics) Console.Error.WriteLine($"{Path.GetFileName(path)}:{d}");
    if (!result.Success) Console.Error.WriteLine($"{result.Errors.Count()} error(s)");
    return result.Script;
}

static int Check(string path)
{
    var s = Load(path);
    if (s == null) return 1;
    Console.WriteLine($"ok: {s.Code.Length} code words, {s.GlobalInit.Length} variable slots, {s.ComboNames.Length} combos");
    return 0;
}

static int Sim(string path, int cycles)
{
    var s = Load(path);
    if (s == null) return 1;
    var m = new Machine(s);
    int[] last = new int[6];
    for (int i = 0; i < cycles; i++)
    {
        m.RunCycle();
        bool changed = false;
        for (int t = 0; t < 6; t++) changed |= m.Trace(t + 1) != last[t];
        if (i == 0 || changed || i == cycles - 1)
        {
            for (int t = 0; t < 6; t++) last[t] = m.Trace(t + 1);
            Console.WriteLine($"cycle {i,5}: TRACE_1..6 = {string.Join(", ", last)}");
        }
    }
    if (m.FaultCount > 0) Console.WriteLine($"faults: {m.FaultCount} (last: {m.LastFault})");
    PrintOled(m.Oled);
    return 0;
}

/// <summary>Prints the OLED as text, two pixel rows per line, if the script drew anything.</summary>
static void PrintOled(Gpc.Engine.Runtime.Oled oled)
{
    if (oled.Version == 0) return;
    Console.WriteLine("OLED:");
    Console.WriteLine("+" + new string('-', Gpc.Engine.Runtime.Oled.Width) + "+");
    for (int y = 0; y < Gpc.Engine.Runtime.Oled.Height; y += 2)
    {
        var line = new char[Gpc.Engine.Runtime.Oled.Width];
        for (int x = 0; x < line.Length; x++)
            line[x] = (oled.Lit(x, y), oled.Lit(x, y + 1)) switch
            {
                (true, true) => '█', (true, false) => '▀', (false, true) => '▄', _ => ' ',
            };
        Console.WriteLine("|" + new string(line) + "|");
    }
    Console.WriteLine("+" + new string('-', Gpc.Engine.Runtime.Oled.Width) + "+");
}

static int RunScript(string path, bool xbox, GameLockState lockState)
{
    var script = Load(path);
    if (script == null) return 1;
    var sched = new DeadlineScheduler();
    using var gameLock = ArmGameLock(lockState, sched);
    var m = new Machine(script);
    using var dev = OpenDs5();
    using var sink = CreateSink(xbox);
    sink.Connect();
    var feedbackGate = new object();
    Ds4Feedback feedback = default;
    sink.Feedback += f => { lock (feedbackGate) feedback = f; };
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; sched.Stop(); };
    Console.WriteLine($"Running {Path.GetFileName(path)} as a virtual {(xbox ? "Xbox 360 pad" : "DS4")}. Ctrl+C to stop.");

    byte lastLarge = 0, lastSmall = 0, lastR = 0, lastG = 0, lastB = 0;
    HideDs5();
    try
    {
    sched.Run(_ =>
    {
        var s = dev.Latest;
        EngineBridge.WriteInput(s, m.Input);
        Ds4Feedback f;
        lock (feedbackGate) f = feedback;
        EngineBridge.WriteFeedback(m, f);
        int next = m.RunCycle();
        sink.Submit(EngineBridge.ReadOutput(m.Output));

        byte large = (byte)(m.RumbleOut(0) * 255 / 100), small = (byte)(m.RumbleOut(1) * 255 / 100);
        byte r = (byte)m.LightbarOut(0), g = (byte)m.LightbarOut(1), b = (byte)m.LightbarOut(2);
        if (large != lastLarge || small != lastSmall || r != lastR || g != lastG || b != lastB)
        {
            dev.SendFeedback(large, small, r, g, b);
            (lastLarge, lastSmall, lastR, lastG, lastB) = (large, small, r, g, b);
        }
        return next * 1000L;
    });
    }
    finally
    {
        sink.Dispose(); // unplug the virtual pad first
        UnhideDs5();
    }
    Console.WriteLine(sched.Stats);
    if (m.FaultCount > 0) Console.WriteLine($"script faults: {m.FaultCount} (last: {m.LastFault})");
    return 0;
}
