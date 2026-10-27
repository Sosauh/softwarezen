using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Gpc.HostWin.Ds4;

/// <summary>
/// Virtual Xbox 360 pad through ViGEmBus, for games that only read XInput (many older PC ports).
/// Takes the same DS4-shaped state: Cross = A, Circle = B, Square = X, Triangle = Y.
/// </summary>
public sealed class VigemX360Sink : IPadSink
{
    ViGEmClient? _client;
    IXbox360Controller? _pad;

    public event Action<Ds4Feedback>? Feedback;

    public void Connect()
    {
        _client = new ViGEmClient();
        _pad = _client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Feedback?.Invoke(new Ds4Feedback(e.LargeMotor, e.SmallMotor, 0, 0, 0));
        _pad.Connect();
    }

    /// <summary>0..255 with 128 centre to XInput's signed 16-bit range; XInput Y points up.</summary>
    public static short Axis(byte b, bool invert)
    {
        int v = b - 128;
        int scaled = v < 0 ? v * 256 : v * 32767 / 127;
        if (invert) scaled = -scaled;
        return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
    }

    public void Submit(in Ds4State s)
    {
        var p = _pad ?? throw new InvalidOperationException("not connected");
        p.SetAxisValue(Xbox360Axis.LeftThumbX, Axis(s.LeftX, false));
        p.SetAxisValue(Xbox360Axis.LeftThumbY, Axis(s.LeftY, true));
        p.SetAxisValue(Xbox360Axis.RightThumbX, Axis(s.RightX, false));
        p.SetAxisValue(Xbox360Axis.RightThumbY, Axis(s.RightY, true));
        p.SetSliderValue(Xbox360Slider.LeftTrigger, s.L2);
        p.SetSliderValue(Xbox360Slider.RightTrigger, s.R2);
        p.SetButtonState(Xbox360Button.A, s.Cross);
        p.SetButtonState(Xbox360Button.B, s.Circle);
        p.SetButtonState(Xbox360Button.X, s.Square);
        p.SetButtonState(Xbox360Button.Y, s.Triangle);
        p.SetButtonState(Xbox360Button.LeftShoulder, s.L1);
        p.SetButtonState(Xbox360Button.RightShoulder, s.R1);
        p.SetButtonState(Xbox360Button.LeftThumb, s.L3);
        p.SetButtonState(Xbox360Button.RightThumb, s.R3);
        p.SetButtonState(Xbox360Button.Back, s.Share);
        p.SetButtonState(Xbox360Button.Start, s.Options);
        p.SetButtonState(Xbox360Button.Guide, s.Ps);
        p.SetButtonState(Xbox360Button.Up, s.Up);
        p.SetButtonState(Xbox360Button.Down, s.Down);
        p.SetButtonState(Xbox360Button.Left, s.Left);
        p.SetButtonState(Xbox360Button.Right, s.Right);
        p.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad?.Disconnect(); } catch (Exception) { /* already gone */ }
        _client?.Dispose();
        _pad = null;
        _client = null;
    }
}
