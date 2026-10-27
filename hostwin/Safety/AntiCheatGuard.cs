using System.Diagnostics;

namespace Gpc.HostWin.Safety;

/// <summary>
/// Decides when the runtime must shut itself down. Pure logic, so it is testable off Windows:
/// feed it the running process names, it answers whether to stop and why.
/// </summary>
public sealed class GuardState
{
    /// <summary>
    /// Process names (lower case, no ".exe") of anti-cheat clients and services. Not exhaustive:
    /// a game whose anti-cheat is not listed is not caught, which is what --only is for.
    /// </summary>
    public static readonly string[] KnownAntiCheat =
    {
        "easyanticheat", "easyanticheat_eos", "easyanticheat_eos_setup",   // Epic EAC (Fortnite, Apex, many others)
        "beservice", "beservice_x64",                                      // BattlEye
        "vgc", "vgtray",                                                   // Riot Vanguard (Valorant, LoL)
        "eaanticheat.gameservice", "eaanticheat.gameservicelauncher",      // EA Javelin
        "faceit", "faceitclient", "faceitservice",                         // FACEIT
        "esportal", "eseaclient",                                          // other CS:GO/CS2 platforms
        "gamemon", "gamemon64", "gameguard",                               // nProtect GameGuard
        "xigncode", "x3",                                                  // XIGNCODE3
        "mhyprot2",                                                        // miHoYo games
        "cod", "blackops6", "blackopscoldwar", "modernwarfare",            // Call of Duty (RICOCHET runs inside the game)
        "valorant", "valorant-win64-shipping",
    };

    readonly HashSet<string> _blocked;
    readonly string? _only;
    bool _sawOnly;

    /// <param name="onlyGame">With a value, the runtime lives only while this process runs.</param>
    public GuardState(string? onlyGame = null, IEnumerable<string>? extraBlocked = null)
    {
        _only = onlyGame == null ? null : Normalize(onlyGame);
        _blocked = new HashSet<string>(KnownAntiCheat.Concat(extraBlocked?.Select(Normalize) ?? Enumerable.Empty<string>()));
    }

    public static string Normalize(string processName)
    {
        var n = processName.Trim().ToLowerInvariant();
        return n.EndsWith(".exe") ? n[..^4] : n;
    }

    /// <summary>Returns a reason to shut down, or null to keep running.</summary>
    public string? Check(IEnumerable<string> runningProcesses)
    {
        bool onlySeen = false;
        foreach (var raw in runningProcesses)
        {
            var name = Normalize(raw);
            if (_blocked.Contains(name)) return $"anti-cheat process '{raw}' is running";
            if (name == _only) onlySeen = true;
        }
        if (_only == null) return null;
        if (onlySeen) { _sawOnly = true; return null; }
        return _sawOnly ? $"'{_only}' closed" : null;
    }
}

/// <summary>Polls running processes once a second on its own thread and fires once when the guard says stop.</summary>
public sealed class AntiCheatGuard : IDisposable
{
    readonly GuardState _state;
    readonly Thread _thread;
    volatile bool _stop;

    public event Action<string>? Tripped;

    public AntiCheatGuard(GuardState state)
    {
        _state = state;
        _thread = new Thread(Loop) { IsBackground = true, Name = "anti-cheat-guard" };
    }

    /// <summary>Checks once right now; returns the reason if the runtime must not start.</summary>
    public string? CheckNow() => _state.Check(ProcessNames());

    public void Start() => _thread.Start();

    static IEnumerable<string> ProcessNames()
    {
        foreach (var p in Process.GetProcesses())
        {
            string name;
            try { name = p.ProcessName; } catch (InvalidOperationException) { continue; } // exited meanwhile
            finally { p.Dispose(); }
            yield return name;
        }
    }

    void Loop()
    {
        while (!_stop)
        {
            var reason = _state.Check(ProcessNames());
            if (reason != null)
            {
                Tripped?.Invoke(reason);
                return;
            }
            Thread.Sleep(1000);
        }
    }

    public void Dispose() => _stop = true;
}
