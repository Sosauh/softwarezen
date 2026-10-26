namespace Gpc.HostWin.Ds4;

/// <summary>Game-visible DS4 state in device units.</summary>
public struct Ds4State
{
    public byte LeftX, LeftY, RightX, RightY; // 0..255, 128 centre
    public byte L2, R2;                       // 0..255
    public bool Up, Down, Left, Right;
    public bool Square, Cross, Circle, Triangle;
    public bool L1, R1, L3, R3, Share, Options, Ps, Touchpad;

    public static Ds4State Neutral => new() { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128 };
}

/// <summary>Feedback the game sent to the virtual pad.</summary>
public readonly record struct Ds4Feedback(byte LargeMotor, byte SmallMotor, byte Red, byte Green, byte Blue);

/// <summary>
/// The virtual-pad seam. ViGEmBus is the v1 implementation; HIDMaestro is the M2 candidate,
/// so nothing outside this interface may depend on a driver.
/// </summary>
public interface IPadSink : IDisposable
{
    void Connect();
    void Submit(in Ds4State state);
    event Action<Ds4Feedback>? Feedback;
}
