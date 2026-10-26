using System.Globalization;

namespace Gpc.Engine;

/// <summary>Signed 16.16 fixed point stored in an int.</summary>
public static class FixedPoint
{
    public const int One = 1 << 16;

    /// <summary>Parses a decimal literal, truncating toward zero (rounding mode is probe group 1).</summary>
    public static int Parse(string text)
    {
        decimal d = decimal.Parse(text, CultureInfo.InvariantCulture);
        decimal scaled = decimal.Truncate(d * One);
        return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
    }

    public static int FromInt(int v) => unchecked(v << 16);
    public static int ToInt(int f) => f >> 16;
    public static int Mul(int a, int b) => unchecked((int)(((long)a * b) >> 16));
    public static int Div(int a, int b) => b == 0 ? 0 : unchecked((int)(((long)a << 16) / b));
    public static double ToDouble(int f) => f / (double)One;
}
