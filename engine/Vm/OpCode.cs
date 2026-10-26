namespace Gpc.Engine.Vm;

/// <summary>Stack-machine opcodes. Operands follow the opcode inline in the int code array.</summary>
public enum Op
{
    Halt,
    Push,       // k
    Pop,
    Dup,
    LoadG,      // slot
    StoreG,     // slot        (pops value, stores, pushes it back)
    LoadGI,     // base        (pops index)
    StoreGI,    // base        (pops value, pops index, stores, pushes value)
    LoadP,      // param
    StoreP,     // param
    IncG,       // slot, delta, yieldOld
    IncGI,      // base, delta, yieldOld  (pops index)
    IncP,       // param, delta, yieldOld
    LoadC,      // const id    (pops index)
    LoadC2,     // const id    (pops column, pops row)
    Add, Sub, Mul, Div, Mod, Shl, Shr, And, Or, Xor, LXor,
    Neg, Not, BitNot, Bool,
    Lt, Le, Gt, Ge, Eq, Ne,
    Jmp,        // addr
    Jz,         // addr (pops)
    Jnz,        // addr (pops)
    Call,       // addr, argc
    Ret,        // (pops return value)
    Builtin,    // id, argc
    Combo,      // ComboOp, combo id
    StepWait,   // (pops ms) ends a combo step
    StepCall,   // combo id; ends a combo step
    StepDone,   // ends the combo
}

public enum ComboOp
{
    Run, Stop, Restart, Running, Suspend, Suspended, Resume, CurrentStep, StepTimeLeft,
    StopAll, SuspendAll, ResumeAll,
}

public static class OpInfo
{
    public static int OperandCount(Op op) => op switch
    {
        Op.Push or Op.LoadG or Op.StoreG or Op.LoadGI or Op.StoreGI or Op.LoadP or Op.StoreP
            or Op.LoadC or Op.LoadC2 or Op.Jmp or Op.Jz or Op.Jnz or Op.StepCall => 1,
        Op.Call or Op.Builtin or Op.Combo => 2,
        Op.IncG or Op.IncGI or Op.IncP => 3,
        _ => 0,
    };
}
