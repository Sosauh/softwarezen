using Gpc.HostWin.Ds5;

namespace Gpc.HostWin.Tests;

public class Ds5ReportTests
{
    static byte[] Report(Action<byte[]> edit)
    {
        var r = new byte[Ds5InputReport.UsbLength];
        r[0] = 0x01;
        r[1] = r[2] = r[3] = r[4] = 128;
        r[8] = 0x08; // hat released
        edit(r);
        return r;
    }

    [Fact]
    public void NeutralReportDecodesToNothingPressed()
    {
        Assert.True(Ds5InputReport.TryDecode(Report(_ => { }), out var s));
        Assert.Equal(Ds5Hat.None, s.Hat);
        Assert.False(s.Cross || s.Square || s.L1 || s.Ps);
        Assert.Equal(128, s.LeftX);
    }

    [Fact]
    public void ButtonsAndAxesDecode()
    {
        var r = Report(b =>
        {
            b[1] = 0; b[4] = 255; b[5] = 200; b[6] = 10;
            b[8] = 0x20 | 0x80 | 0x02; // cross, triangle, hat E
            b[9] = 0x01 | 0x20 | 0x80; // L1, options, R3
            b[10] = 0x01;              // PS
        });
        Assert.True(Ds5InputReport.TryDecode(r, out var s));
        Assert.Equal(0, s.LeftX);
        Assert.Equal(255, s.RightY);
        Assert.Equal(200, s.L2);
        Assert.Equal(10, s.R2);
        Assert.True(s.Cross && s.Triangle && !s.Square && !s.Circle);
        Assert.Equal(Ds5Hat.E, s.Hat);
        Assert.True(Ds5InputReport.HatRight(s.Hat) && !Ds5InputReport.HatUp(s.Hat));
        Assert.True(s.L1 && s.Options && s.R3 && !s.R1);
        Assert.True(s.Ps);
    }

    [Fact]
    public void WrongReportIdIsRejected()
    {
        var r = Report(b => b[0] = 0x31); // Bluetooth full report, out of v1
        Assert.False(Ds5InputReport.TryDecode(r, out _));
    }

    [Fact]
    public void OutputReportPlacesMotorsAndLightbar()
    {
        var buf = new byte[Ds5OutputReport.UsbLength];
        Ds5OutputReport.Write(buf, large: 200, small: 50, red: 1, green: 2, blue: 3);
        Assert.Equal(0x02, buf[0]);
        Assert.Equal(50, buf[3]);
        Assert.Equal(200, buf[4]);
        Assert.Equal(0x04, buf[2] & 0x04);
        Assert.Equal(new byte[] { 1, 2, 3 }, buf[45..48]);
    }
}
