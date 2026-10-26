using Gpc.HostWin.Ds5;

namespace Gpc.HostWin.Ds4;

/// <summary>Straight DS5 to DS4 mapping used by passthrough mode (M1, no script).</summary>
public static class Ds5ToDs4
{
    public static Ds4State Map(in Ds5State s) => new()
    {
        LeftX = s.LeftX, LeftY = s.LeftY, RightX = s.RightX, RightY = s.RightY,
        L2 = s.L2, R2 = s.R2,
        Up = Ds5InputReport.HatUp(s.Hat), Down = Ds5InputReport.HatDown(s.Hat),
        Left = Ds5InputReport.HatLeft(s.Hat), Right = Ds5InputReport.HatRight(s.Hat),
        Square = s.Square, Cross = s.Cross, Circle = s.Circle, Triangle = s.Triangle,
        L1 = s.L1, R1 = s.R1, L3 = s.L3, R3 = s.R3,
        Share = s.Create, Options = s.Options, Ps = s.Ps, Touchpad = s.TouchpadClick,
    };
}
