using System.Diagnostics;

namespace Gpc.HostWin.HidHide;

/// <summary>
/// Thin wrapper over HidHideCLI.exe (HidHide 1.5.230.0 or later). Only the engine executable
/// is whitelisted, so games see the virtual DS4 and not the real DS5.
/// Option names follow the HidHide CLI help; verify with "HidHideCLI.exe --help" on your install.
/// </summary>
public sealed class HidHideCli
{
    public static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");

    readonly string _exe;
    public HidHideCli(string? exe = null) => _exe = exe ?? DefaultPath;

    public bool Installed => File.Exists(_exe);

    public string RegisterApp(string imagePath) => Run("--app-reg", Quote(imagePath));
    public string UnregisterApp(string imagePath) => Run("--app-unreg", Quote(imagePath));
    public string HideDevice(string instancePath) => Run("--dev-hide", Quote(instancePath));
    public string UnhideDevice(string instancePath) => Run("--dev-unhide", Quote(instancePath));
    public string CloakOn() => Run("--cloak-on");
    public string CloakOff() => Run("--cloak-off");
    /// <summary>JSON list of gaming devices with their instance paths.</summary>
    public string ListGamingDevices() => Run("--dev-gaming");

    static string Quote(string s) => "\"" + s.Replace("\"", "") + "\"";

    string Run(params string[] args)
    {
        if (!Installed) throw new FileNotFoundException("HidHideCLI.exe not found; install HidHide 1.5.230.0 or later", _exe);
        var psi = new ProcessStartInfo(_exe, string.Join(' ', args))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"HidHideCLI {string.Join(' ', args)} failed ({p.ExitCode}): {output}");
        return output;
    }
}
