namespace Gpc.HostWin.Ds5;

/// <summary>Per-stick result, in GPC units (-100..100 per axis).</summary>
public readonly record struct StickDrift(double MeanX, double MeanY, double MaxRadius, int Samples)
{
    /// <summary>Smallest radial deadzone that hides every sample seen at rest, plus a margin.</summary>
    public int SuggestedDeadzone(int margin = 2) => (int)Math.Ceiling(MaxRadius) + margin;
}

/// <summary>
/// Collects stick samples while the controller sits untouched and reports how far they wander
/// from centre. Drift shows as a mean offset (a stick that rests off-centre) or as a large
/// radius (a stick that wanders).
/// </summary>
public sealed class DriftAnalyzer
{
    double _lxSum, _lySum, _rxSum, _rySum, _lMax, _rMax;
    int _n;

    /// <summary>Raw 0..255 axis to GPC -100..100, 128 = 0. Y keeps the device's down-positive sign.</summary>
    public static int ToGpc(byte raw)
    {
        int v = raw - 128;
        int scaled = v < 0 ? v * 100 / 128 : v * 100 / 127;
        return Math.Clamp(scaled, -100, 100);
    }

    public void Add(in Ds5State s)
    {
        int lx = ToGpc(s.LeftX), ly = ToGpc(s.LeftY), rx = ToGpc(s.RightX), ry = ToGpc(s.RightY);
        _lxSum += lx; _lySum += ly; _rxSum += rx; _rySum += ry;
        _lMax = Math.Max(_lMax, Math.Sqrt(lx * lx + ly * ly));
        _rMax = Math.Max(_rMax, Math.Sqrt(rx * rx + ry * ry));
        _n++;
    }

    public StickDrift Left => _n == 0 ? default : new(_lxSum / _n, _lySum / _n, _lMax, _n);
    public StickDrift Right => _n == 0 ? default : new(_rxSum / _n, _rySum / _n, _rMax, _n);
}
