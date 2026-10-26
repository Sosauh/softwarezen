namespace Gpc.HostWin.Ds5;

/// <summary>Decoded DualSense state from a USB input report (id 0x01). Raw device units.</summary>
public struct Ds5State
{
    public byte LeftX, LeftY, RightX, RightY; // 0..255, 128 = centre, Y grows downward
    public byte L2, R2;                       // 0..255
    public Ds5Hat Hat;
    public bool Square, Cross, Circle, Triangle;
    public bool L1, R1, L2Button, R2Button, Create, Options, L3, R3;
    public bool Ps, TouchpadClick, Mute;
    public byte Sequence;
}

public enum Ds5Hat : byte { N = 0, NE, E, SE, S, SW, W, NW, None = 8 }

/// <summary>
/// Decodes the DualSense USB input report. Layout follows the public Linux
/// hid-playstation driver: report id, six axes, a sequence byte, then four button bytes.
/// Gyro, accelerometer and touch are out of v1 and are not decoded.
/// </summary>
public static class Ds5InputReport
{
    public const byte ReportId = 0x01;
    public const int UsbLength = 64;

    public static bool TryDecode(ReadOnlySpan<byte> report, out Ds5State state)
    {
        state = default;
        if (report.Length < 12 || report[0] != ReportId)
            return false;

        state.LeftX = report[1];
        state.LeftY = report[2];
        state.RightX = report[3];
        state.RightY = report[4];
        state.L2 = report[5];
        state.R2 = report[6];
        state.Sequence = report[7];

        byte b0 = report[8], b1 = report[9], b2 = report[10];
        int hat = b0 & 0x0F;
        state.Hat = hat <= 7 ? (Ds5Hat)hat : Ds5Hat.None;
        state.Square = (b0 & 0x10) != 0;
        state.Cross = (b0 & 0x20) != 0;
        state.Circle = (b0 & 0x40) != 0;
        state.Triangle = (b0 & 0x80) != 0;

        state.L1 = (b1 & 0x01) != 0;
        state.R1 = (b1 & 0x02) != 0;
        state.L2Button = (b1 & 0x04) != 0;
        state.R2Button = (b1 & 0x08) != 0;
        state.Create = (b1 & 0x10) != 0;
        state.Options = (b1 & 0x20) != 0;
        state.L3 = (b1 & 0x40) != 0;
        state.R3 = (b1 & 0x80) != 0;

        state.Ps = (b2 & 0x01) != 0;
        state.TouchpadClick = (b2 & 0x02) != 0;
        state.Mute = (b2 & 0x04) != 0;
        return true;
    }

    public static bool HatUp(Ds5Hat h) => h is Ds5Hat.N or Ds5Hat.NE or Ds5Hat.NW;
    public static bool HatDown(Ds5Hat h) => h is Ds5Hat.S or Ds5Hat.SE or Ds5Hat.SW;
    public static bool HatLeft(Ds5Hat h) => h is Ds5Hat.W or Ds5Hat.NW or Ds5Hat.SW;
    public static bool HatRight(Ds5Hat h) => h is Ds5Hat.E or Ds5Hat.NE or Ds5Hat.SE;
}
