using System.Diagnostics;

namespace Gpc.HostWin.Safety;

/// <summary>
/// Ties the runtime to one game (--only): waits for it to start, then says stop once it closes,
/// so the runtime can't be left running after you finish playing. Pure logic, testable anywhere.
/// </summary>
public sealed class GameLockState
{
    readonly string? _game;
    bool _seen;

    public GameLockState(string? game) => _game = game == null ? null : Normalize(game);

    public static string Normalize(string processName)
    {
        var n = processName.Trim().ToLowerInvariant();
        return n.EndsWith(".exe") ? n[..^4] : n;
    }

    /// <summary>Returns a reason to shut down, or null to keep running.</summary>
    public string? Check(IEnumerable<string> runningProcesses)
    {
        if (_game == null) return null;
        if (runningProcesses.Any(p => Normalize(p) == _game)) { _seen = true; return null; }
        return _seen ? $"'{_game}' closed" : null;
    }
}

/// <summary>Polls running processes once a second on its own thread and fires once when the lock says stop.</summary>
public sealed class GameLock : IDisposable
{
    readonly GameLockState _state;
    readonly Thread _thread;
    volatile bool _stop;

    public event Action<string>? Tripped;

    public GameLock(GameLockState state)
    {
        _state = state;
        _thread = new Thread(Loop) { IsBackground = true, Name = "game-lock" };
    }

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
