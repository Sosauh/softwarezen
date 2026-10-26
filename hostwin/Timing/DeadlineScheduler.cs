using System.Diagnostics;
using static Gpc.HostWin.Timing.Win32Timing;

namespace Gpc.HostWin.Timing;

/// <summary>
/// Free-running deadline clock. Tick n starts at start + sum of earlier periods (absolute QPC
/// deadlines, never "now + period"), so a late wake delays one tick without drifting the rest.
/// Sleeps on a high-resolution waitable timer and spins the last <see cref="SpinUs"/>.
/// </summary>
public sealed class DeadlineScheduler : IDisposable
{
    public int SpinUs { get; init; } = 500;
    public int? PinnedCore { get; init; }
    public bool UseMmcss { get; init; } = true;
    public TickStats Stats { get; } = new();

    volatile bool _stop;

    public void Stop() => _stop = true;

    /// <param name="tick">Called once per tick with the tick index; returns the next period in microseconds.</param>
    public void Run(Func<long, long> tick, long firstPeriodUs = 10_000)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DeadlineScheduler uses Win32 timers; run the engine with a simulated clock off Windows.");

        IntPtr timer = CreateWaitableTimerEx(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        timeBeginPeriod(1);
        OptOutOfPowerThrottling();
        IntPtr thread = GetCurrentThread();
        SetThreadPriority(thread, THREAD_PRIORITY_HIGHEST);
        if (PinnedCore is int core) SetThreadAffinityMask(thread, (UIntPtr)(1UL << core));
        uint mmIndex = 0;
        IntPtr mm = UseMmcss ? AvSetMmThreadCharacteristics("Games", ref mmIndex) : IntPtr.Zero;

        try
        {
            long freq = Stopwatch.Frequency;
            long deadline = Stopwatch.GetTimestamp();
            long periodUs = firstPeriodUs;
            for (long n = 0; !_stop; n++)
            {
                WaitUntil(timer, deadline, freq);
                long now = Stopwatch.GetTimestamp();
                Stats.Record((now - deadline) * 1_000_000 / freq);
                periodUs = tick(n);
                deadline += periodUs * freq / 1_000_000;
            }
        }
        finally
        {
            if (mm != IntPtr.Zero) AvRevertMmThreadCharacteristics(mm);
            timeEndPeriod(1);
            if (timer != IntPtr.Zero) CloseHandle(timer);
        }
    }

    void WaitUntil(IntPtr timer, long deadline, long freq)
    {
        long spinTicks = SpinUs * freq / 1_000_000;
        long remaining = deadline - Stopwatch.GetTimestamp();
        if (remaining > spinTicks && timer != IntPtr.Zero)
        {
            long sleep100ns = -((remaining - spinTicks) * 10_000_000 / freq); // negative = relative
            if (SetWaitableTimer(timer, sleep100ns, 0, IntPtr.Zero, IntPtr.Zero, false))
                WaitForSingleObject(timer, INFINITE);
        }
        while (Stopwatch.GetTimestamp() < deadline)
            Thread.SpinWait(20);
    }

    static void OptOutOfPowerThrottling()
    {
        var state = new PowerThrottlingState
        {
            Version = 1,
            ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            StateMask = 0, // control it, and turn it off
        };
        SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<PowerThrottlingState>());
    }

    public void Dispose() => Stop();
}
