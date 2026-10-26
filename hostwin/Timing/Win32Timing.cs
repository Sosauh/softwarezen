using System.Runtime.InteropServices;

namespace Gpc.HostWin.Timing;

static partial class Win32Timing
{
    public const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    public const uint TIMER_ALL_ACCESS = 0x001F0003;
    public const uint INFINITE = 0xFFFFFFFF;
    public const int THREAD_PRIORITY_HIGHEST = 2;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWaitableTimer(IntPtr timer, in long dueTime, int period, IntPtr completion, IntPtr arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(IntPtr handle, uint ms);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentThread();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetThreadPriority(IntPtr thread, int priority);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);

    [LibraryImport("kernel32.dll")]
    public static partial IntPtr GetCurrentProcess();

    // PROCESS_POWER_THROTTLING_STATE, ProcessPowerThrottling = 4
    [StructLayout(LayoutKind.Sequential)]
    public struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }
    public const int ProcessPowerThrottling = 4;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint ms);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint ms);

    [LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr AvSetMmThreadCharacteristics(string task, ref uint taskIndex);

    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AvRevertMmThreadCharacteristics(IntPtr handle);
}
