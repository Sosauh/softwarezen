using Gpc.Engine.Vm;
using Gpc.HostWin.Ds4;
using Gpc.HostWin.Ds5;
using static Gpc.Engine.Runtime.Identifiers;

namespace Gpc.HostWin;

/// <summary>Converts between device reports and the engine's GPC-unit slots.</summary>
public static class EngineBridge
{
    public static void WriteInput(in Ds5State s, Span<int> slots)
    {
        slots.Clear();
        slots[LX] = DriftAnalyzer.ToGpc(s.LeftX);
        slots[LY] = DriftAnalyzer.ToGpc(s.LeftY);
        slots[RX] = DriftAnalyzer.ToGpc(s.RightX);
        slots[RY] = DriftAnalyzer.ToGpc(s.RightY);
        slots[L2] = s.L2 * 100 / 255;
        slots[R2] = s.R2 * 100 / 255;
        slots[PS] = Btn(s.Ps);
        slots[SHARE] = Btn(s.Create);
        slots[OPTIONS] = Btn(s.Options);
        slots[R1] = Btn(s.R1);
        slots[R3] = Btn(s.R3);
        slots[L1] = Btn(s.L1);
        slots[L3] = Btn(s.L3);
        slots[UP] = Btn(Ds5InputReport.HatUp(s.Hat));
        slots[DOWN] = Btn(Ds5InputReport.HatDown(s.Hat));
        slots[LEFT] = Btn(Ds5InputReport.HatLeft(s.Hat));
        slots[RIGHT] = Btn(Ds5InputReport.HatRight(s.Hat));
        slots[TRIANGLE] = Btn(s.Triangle);
        slots[CIRCLE] = Btn(s.Circle);
        slots[CROSS] = Btn(s.Cross);
        slots[SQUARE] = Btn(s.Square);
        slots[TOUCH] = Btn(s.TouchpadClick);
    }

    static int Btn(bool b) => b ? 100 : 0;

    /// <summary>GPC -100..100 back to a 0..255 axis byte (inverse of <see cref="DriftAnalyzer.ToGpc"/>).</summary>
    public static byte AxisByte(int v)
    {
        v = Math.Clamp(v, -100, 100);
        return (byte)(v < 0 ? 128 + v * 128 / 100 : 128 + v * 127 / 100);
    }

    static byte TriggerByte(int v) => (byte)(Math.Clamp(v, 0, 100) * 255 / 100);

    public static Ds4State ReadOutput(ReadOnlySpan<int> o) => new()
    {
        LeftX = AxisByte(o[LX]), LeftY = AxisByte(o[LY]), RightX = AxisByte(o[RX]), RightY = AxisByte(o[RY]),
        L2 = TriggerByte(o[L2]), R2 = TriggerByte(o[R2]),
        Up = o[UP] > 0, Down = o[DOWN] > 0, Left = o[LEFT] > 0, Right = o[RIGHT] > 0,
        Square = o[SQUARE] > 0, Cross = o[CROSS] > 0, Circle = o[CIRCLE] > 0, Triangle = o[TRIANGLE] > 0,
        L1 = o[L1] > 0, R1 = o[R1] > 0, L3 = o[L3] > 0, R3 = o[R3] > 0,
        Share = o[SHARE] > 0, Options = o[OPTIONS] > 0, Ps = o[PS] > 0, Touchpad = o[TOUCH] > 0,
    };

    /// <summary>Game feedback into the engine; DS4 motors are 0..255, GPC rumble is 0..100.</summary>
    public static void WriteFeedback(Machine m, in Ds4Feedback f)
    {
        m.RumbleFromGame[RUMBLE_A] = f.LargeMotor * 100 / 255;
        m.RumbleFromGame[RUMBLE_B] = f.SmallMotor * 100 / 255;
        m.LightbarFromGame[0] = f.Red;
        m.LightbarFromGame[1] = f.Green;
        m.LightbarFromGame[2] = f.Blue;
    }
}
