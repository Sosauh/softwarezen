using Gpc.Engine.Runtime;

namespace Gpc.Engine.Vm;

/// <summary>Integer semantics shared by the VM and the compiler's constant folder, so both agree.</summary>
public static class Arith
{
    public static int Binary(Op op, int a, int b, CompatFlags f) => unchecked(op switch
    {
        Op.Add => a + b,
        Op.Sub => a - b,
        Op.Mul => a * b,
        Op.Div => b == 0 ? f.DivideByZeroResult : (a == int.MinValue && b == -1 ? int.MinValue : a / b),
        Op.Mod => b == 0 ? f.ModuloByZeroResult : (b == -1 ? 0 : a % b),
        Op.Shl => a << (b & 31),
        Op.Shr => f.ArithmeticShiftRight ? a >> (b & 31) : (int)((uint)a >> (b & 31)),
        Op.And => a & b,
        Op.Or => a | b,
        Op.Xor => a ^ b,
        Op.LXor => ((a != 0) != (b != 0)) ? 1 : 0,
        Op.Lt => a < b ? 1 : 0,
        Op.Le => a <= b ? 1 : 0,
        Op.Gt => a > b ? 1 : 0,
        Op.Ge => a >= b ? 1 : 0,
        Op.Eq => a == b ? 1 : 0,
        Op.Ne => a != b ? 1 : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    });

    public static int Unary(Op op, int a) => unchecked(op switch
    {
        Op.Neg => -a,
        Op.Not => a == 0 ? 1 : 0,
        Op.BitNot => ~a,
        Op.Bool => a != 0 ? 1 : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    });
}
