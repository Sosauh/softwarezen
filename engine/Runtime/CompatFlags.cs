namespace Gpc.Engine.Runtime;

public enum ComboStepMode
{
    /// <summary>A step's statements run again every cycle until its wait expires.</summary>
    Reexecute,
    /// <summary>A step's statements run once; its set_val writes are re-applied every cycle until the wait expires.</summary>
    RecordAndHold,
}

public enum RemapLayer { PreRemap, PostRemap }

/// <summary>
/// Every behaviour the Zen guide leaves unclear, in one place. Each default is a guess until
/// the matching probe in /probes has been measured on a real Zen; the comment names the probe group.
/// </summary>
public sealed record CompatFlags
{
    /// <summary>Probe group 1. Result of x / 0 and x % 0.</summary>
    public int DivideByZeroResult { get; init; } = 0;
    public int ModuloByZeroResult { get; init; } = 0;
    /// <summary>Probe group 1. Arithmetic (sign-extending) right shift.</summary>
    public bool ArithmeticShiftRight { get; init; } = true;
    /// <summary>Probe group 2. The current compiler returns the old value; the original one returned the new value.</summary>
    public bool PostIncrementYieldsOld { get; init; } = true;
    /// <summary>Probe group 2. Whether &amp;&amp; and || skip their right operand.</summary>
    public bool ShortCircuit { get; init; } = true;
    /// <summary>Probe group 3. Whether get_lval sees the report before or after remap/unmap/block.</summary>
    public RemapLayer LvalLayer { get; init; } = RemapLayer.PreRemap;
    /// <summary>Probe group 4. The VM interval with vm_tctrl(0), in ms.</summary>
    public int BaseCycleMs { get; init; } = 10;
    /// <summary>Probe group 4. Whether a vm_tctrl offset stays until changed or applies to one cycle.</summary>
    public bool VmTctrlPersists { get; init; } = true;
    /// <summary>Probe group 5.</summary>
    public ComboStepMode ComboStepMode { get; init; } = ComboStepMode.Reexecute;
    /// <summary>Probe group 6. get_brtime before the first release since load.</summary>
    public int InitialBrtime { get; init; } = 32767;
    /// <summary>Saturation value for get_ptime and get_brtime.</summary>
    public int TimeSaturation { get; init; } = 32767;
    /// <summary>Not a Zen behaviour: a runaway loop ends the cycle after this many instructions.</summary>
    public int InstructionBudget { get; init; } = 200_000;

    public static CompatFlags Default { get; } = new();
}
