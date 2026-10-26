namespace Gpc.HostWin.Ds5;

/// <summary>
/// Builds the DualSense USB output report (id 0x02, 48 bytes) for rumble and lightbar.
/// Offsets follow the public Linux hid-playstation driver's common output report.
/// </summary>
public static class Ds5OutputReport
{
    public const byte ReportId = 0x02;
    public const int UsbLength = 48;

    const byte Flag0CompatibleVibration = 0x01;
    const byte Flag0HapticsSelect = 0x02;
    const byte Flag1LightbarControl = 0x04;
    const byte Flag2CompatibleVibration2 = 0x04;

    /// <param name="large">Left (low-frequency) motor, 0..255.</param>
    /// <param name="small">Right (high-frequency) motor, 0..255.</param>
    public static void Write(Span<byte> report, byte large, byte small, byte red, byte green, byte blue)
    {
        if (report.Length < UsbLength)
            throw new ArgumentException($"need {UsbLength} bytes", nameof(report));
        report[..UsbLength].Clear();
        report[0] = ReportId;
        report[1] = Flag0CompatibleVibration | Flag0HapticsSelect;
        report[2] = Flag1LightbarControl;
        report[3] = small;
        report[4] = large;
        report[39] = Flag2CompatibleVibration2;
        report[45] = red;
        report[46] = green;
        report[47] = blue;
    }
}
