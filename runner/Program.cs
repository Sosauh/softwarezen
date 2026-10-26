using System.Diagnostics;
using Gpc.HostWin.Ds4;
using Gpc.HostWin.Ds5;
using Gpc.HostWin.Timing;

return args.Length == 0 ? Usage() : args[0] switch
{
    "monitor" => Monitor(),
    "drift" => Drift(args.Length > 1 && int.TryParse(args[1], out int s) ? s : 10),
    "passthrough" => Passthrough(),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("""
        gpcrun monitor          live DS5 values in GPC units; Ctrl+C to quit
        gpcrun drift [seconds]  leave the controller untouched; measures stick drift and suggests a deadzone (default 10 s)
        gpcrun passthrough      DS5 -> virtual DS4 with no script (needs ViGEmBus); prints timing stats on exit
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

static int Passthrough()
{
    using var dev = OpenDs5();
    using var sink = new VigemDs4Sink();
    sink.Connect();
    sink.Feedback += f => dev.SendFeedback(f.LargeMotor, f.SmallMotor, f.Red, f.Green, f.Blue);
    var sched = new DeadlineScheduler();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; sched.Stop(); };
    Console.WriteLine("Passthrough running at 10 ms. Ctrl+C to stop.");
    sched.Run(_ =>
    {
        var s = dev.Latest;
        sink.Submit(Ds5ToDs4.Map(s));
        return 10_000;
    });
    Console.WriteLine(sched.Stats);
    return 0;
}
