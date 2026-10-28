namespace Gpc.Engine.Vm;

public sealed record ConstArray(string Name, string ElementType, int Dimensions, int Rows, int Columns, int[] Values)
{
    /// <summary>Strings and images: Values are byte offsets into <see cref="CompiledScript.Strings"/>.</summary>
    public bool HoldsAddresses { get; init; }

    public int Length => Dimensions == 2 ? Rows : Values.Length;
}

public readonly record struct Remap(int Source, int Target); // Target -1 = unmap

/// <summary>Immutable result of compiling a script: bytecode plus everything the VM needs to run it.</summary>
public sealed class CompiledScript
{
    public required int[] Code { get; init; }
    public required int InitEntry { get; init; }
    public required int MainEntry { get; init; }
    public required int[] GlobalInit { get; init; }
    public required ConstArray[] Consts { get; init; }
    public required byte[] Data { get; init; }
    /// <summary>Zero-terminated strings and image records, addressed by byte offset.</summary>
    public byte[] Strings { get; init; } = Array.Empty<byte>();
    public required Remap[] Remaps { get; init; }
    /// <summary>Per combo, the code address of each step.</summary>
    public required int[][] ComboSteps { get; init; }
    public required string[] ComboNames { get; init; }
    public required Runtime.CompatFlags Flags { get; init; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}
