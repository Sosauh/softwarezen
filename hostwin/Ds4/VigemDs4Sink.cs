using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace Gpc.HostWin.Ds4;

/// <summary>Virtual DS4 through ViGEmBus (driver pinned to 1.22.0; the client library talks to it).</summary>
public sealed class VigemDs4Sink : IPadSink
{
    ViGEmClient? _client;
    IDualShock4Controller? _pad;

    public event Action<Ds4Feedback>? Feedback;

    public void Connect()
    {
        _client = new ViGEmClient();
        _pad = _client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) =>
            Feedback?.Invoke(new Ds4Feedback(e.LargeMotor, e.SmallMotor, e.LightbarColor.Red, e.LightbarColor.Green, e.LightbarColor.Blue));
        _pad.Connect();
    }

    public void Submit(in Ds4State s)
    {
        var p = _pad ?? throw new InvalidOperationException("not connected");
        p.SetAxisValue(DualShock4Axis.LeftThumbX, s.LeftX);
        p.SetAxisValue(DualShock4Axis.LeftThumbY, s.LeftY);
        p.SetAxisValue(DualShock4Axis.RightThumbX, s.RightX);
        p.SetAxisValue(DualShock4Axis.RightThumbY, s.RightY);
        p.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        p.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);
        p.SetButtonState(DualShock4Button.Square, s.Square);
        p.SetButtonState(DualShock4Button.Cross, s.Cross);
        p.SetButtonState(DualShock4Button.Circle, s.Circle);
        p.SetButtonState(DualShock4Button.Triangle, s.Triangle);
        p.SetButtonState(DualShock4Button.ShoulderLeft, s.L1);
        p.SetButtonState(DualShock4Button.ShoulderRight, s.R1);
        p.SetButtonState(DualShock4Button.TriggerLeft, s.L2 > 0);
        p.SetButtonState(DualShock4Button.TriggerRight, s.R2 > 0);
        p.SetButtonState(DualShock4Button.ThumbLeft, s.L3);
        p.SetButtonState(DualShock4Button.ThumbRight, s.R3);
        p.SetButtonState(DualShock4Button.Share, s.Share);
        p.SetButtonState(DualShock4Button.Options, s.Options);
        p.SetButtonState(DualShock4SpecialButton.Ps, s.Ps);
        p.SetButtonState(DualShock4SpecialButton.Touchpad, s.Touchpad);
        p.SetDPadDirection(DPad(s));
        p.SubmitReport();
    }

    static DualShock4DPadDirection DPad(in Ds4State s) => (s.Up, s.Down, s.Left, s.Right) switch
    {
        (true, false, false, false) => DualShock4DPadDirection.North,
        (true, false, false, true) => DualShock4DPadDirection.Northeast,
        (false, false, false, true) => DualShock4DPadDirection.East,
        (false, true, false, true) => DualShock4DPadDirection.Southeast,
        (false, true, false, false) => DualShock4DPadDirection.South,
        (false, true, true, false) => DualShock4DPadDirection.Southwest,
        (false, false, true, false) => DualShock4DPadDirection.West,
        (true, false, true, false) => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };

    public void Dispose()
    {
        try { _pad?.Disconnect(); } catch (Exception) { /* already gone */ }
        _client?.Dispose();
        _pad = null;
        _client = null;
    }
}
