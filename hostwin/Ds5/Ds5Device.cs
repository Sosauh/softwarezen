using HidSharp;

namespace Gpc.HostWin.Ds5;

/// <summary>
/// A DualSense over USB. A background thread keeps the latest decoded state so the
/// tick loop never blocks on HID reads. Reconnects when the controller is replugged.
/// </summary>
public sealed class Ds5Device : IDisposable
{
    public const int SonyVendorId = 0x054C;
    public static readonly int[] ProductIds = { 0x0CE6 /* DualSense */, 0x0DF2 /* DualSense Edge */ };

    readonly object _gate = new();
    readonly Thread _reader;
    volatile bool _stop;
    HidStream? _stream;
    Ds5State _latest;
    long _reports;

    public bool Connected => _stream != null;
    public long ReportCount => Interlocked.Read(ref _reports);
    public event Action<string>? Log;

    public Ds5Device()
    {
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "ds5-reader", Priority = ThreadPriority.AboveNormal };
        _reader.Start();
    }

    public static IEnumerable<HidDevice> Enumerate() =>
        DeviceList.Local.GetHidDevices(SonyVendorId).Where(d => ProductIds.Contains(d.ProductID));

    public Ds5State Latest
    {
        get { lock (_gate) return _latest; }
    }

    public void SendFeedback(byte large, byte small, byte r, byte g, byte b)
    {
        var stream = _stream;
        if (stream == null) return;
        Span<byte> buf = stackalloc byte[Ds5OutputReport.UsbLength];
        Ds5OutputReport.Write(buf, large, small, r, g, b);
        try { stream.Write(buf.ToArray()); }
        catch (Exception e) when (e is IOException or TimeoutException) { Log?.Invoke($"ds5 write failed: {e.Message}"); }
    }

    void ReadLoop()
    {
        var buf = new byte[Ds5InputReport.UsbLength];
        while (!_stop)
        {
            if (_stream == null && !TryOpen())
            {
                Thread.Sleep(500);
                continue;
            }
            try
            {
                int n = _stream!.Read(buf, 0, buf.Length);
                if (Ds5InputReport.TryDecode(buf.AsSpan(0, n), out var s))
                {
                    lock (_gate) _latest = s;
                    Interlocked.Increment(ref _reports);
                }
            }
            catch (TimeoutException) { }
            catch (Exception e) when (e is IOException or ObjectDisposedException)
            {
                Log?.Invoke("ds5 disconnected");
                _stream?.Dispose();
                _stream = null;
                lock (_gate) _latest = Neutral;
            }
        }
    }

    bool TryOpen()
    {
        foreach (var dev in Enumerate())
        {
            if (dev.GetMaxInputReportLength() < Ds5InputReport.UsbLength) continue; // not the USB gamepad interface
            if (dev.TryOpen(out HidStream s))
            {
                s.ReadTimeout = 100;
                _stream = s;
                Log?.Invoke($"ds5 connected: {dev.DevicePath}");
                return true;
            }
        }
        return false;
    }

    public static Ds5State Neutral => new() { LeftX = 128, LeftY = 128, RightX = 128, RightY = 128, Hat = Ds5Hat.None };

    public void Dispose()
    {
        _stop = true;
        _stream?.Dispose();
        _reader.Join(1000);
    }
}
