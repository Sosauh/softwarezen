using Gpc.Engine.Runtime;
using Gpc.Engine.Syntax;
using Gpc.Engine.Vm;

namespace Gpc.Engine.Compile;

public sealed record CompileResult(CompiledScript? Script, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Script != null;
    public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == Severity.Error);
}

public static class GpcCompiler
{
    public const int MaxVariableSlots = 1020;

    public static CompileResult Compile(string source, CompatFlags? flags = null)
    {
        var diag = new DiagnosticBag();
        var unit = Parser.Parse(source, diag);
        var script = new Emitter(unit, flags ?? CompatFlags.Default, diag).Run();
        return new CompileResult(diag.HasErrors ? null : script, diag.Items);
    }
}

sealed class Emitter
{
    sealed class FuncInfo
    {
        public required FunctionDecl Decl;
        public int Addr = -1;
        public bool ReturnsValue, ReturnsBare;
        public readonly List<int> CallPatches = new();
    }

    sealed record Global(int Slot, int Size);

    sealed class LoopCtx
    {
        public readonly List<int> Breaks = new(), Continues = new();
        public required bool IsSwitch;
    }

    enum LKind { Global, Indexed, Param }
    readonly record struct LValue(LKind Kind, int Operand, Expr? Index);

    readonly CompilationUnit _unit;
    readonly CompatFlags _flags;
    readonly DiagnosticBag _diag;
    readonly List<int> _code = new();

    readonly Dictionary<string, DefineDecl> _defines = new();
    readonly Dictionary<string, int> _defineValues = new();
    readonly HashSet<string> _evaluating = new();
    readonly Dictionary<string, int> _enumValues = new();
    readonly Dictionary<string, int> _constIds = new();
    readonly List<ConstArray> _consts = new();
    readonly Dictionary<string, Global> _globals = new();
    readonly List<int> _globalInit = new();
    readonly Dictionary<string, FuncInfo> _funcs = new();
    readonly Dictionary<string, int> _comboIds = new();
    readonly List<ComboDecl> _combos = new();
    readonly List<byte> _data = new();
    /// <summary>Strings and images, addressed by byte offset (what addr() and print() use).</summary>
    readonly List<byte> _strings = new();
    readonly List<Remap> _remaps = new();
    int _slots;

    FuncInfo? _curFunc;
    int _curCombo = -1;
    readonly List<LoopCtx> _loops = new();

    public Emitter(CompilationUnit unit, CompatFlags flags, DiagnosticBag diag)
    {
        _unit = unit;
        _flags = flags;
        _diag = diag;
    }

    void Error(string code, string msg, SourceSpan at) => _diag.Error(code, msg, at);
    void Warn(string code, string msg, SourceSpan at) => _diag.Warning(code, msg, at);

    // ======================== driver ========================

    public CompiledScript Run()
    {
        CollectNames();
        CollectData();

        var inits = _unit.Decls.OfType<EntryDecl>().Where(e => e.IsInit).ToList();
        var mains = _unit.Decls.OfType<EntryDecl>().Where(e => !e.IsInit).ToList();
        if (mains.Count == 0 || mains.All(m => m.Body.Statements.Count == 0))
            Warn("GZ6037", "the script has no main code", new SourceSpan(0, 0, 1, 1));

        int initEntry = _code.Count;
        foreach (var b in inits) EmitBlock(b.Body);
        Emit(Op.Halt);
        int mainEntry = _code.Count;
        foreach (var b in mains) EmitBlock(b.Body);
        Emit(Op.Halt);

        var steps = new int[_combos.Count][];
        for (int i = 0; i < _combos.Count; i++) steps[i] = EmitCombo(_combos[i]);

        foreach (var f in _funcs.Values) EmitFunction(f);
        foreach (var f in _funcs.Values)
            foreach (int site in f.CallPatches) _code[site] = f.Addr;

        return new CompiledScript
        {
            Code = _code.ToArray(),
            InitEntry = initEntry,
            MainEntry = mainEntry,
            GlobalInit = _globalInit.ToArray(),
            Consts = _consts.ToArray(),
            Data = _data.ToArray(),
            Strings = _strings.ToArray(),
            Remaps = _remaps.ToArray(),
            ComboSteps = steps,
            ComboNames = _combos.Select(c => c.Name).ToArray(),
            Flags = _flags,
            Diagnostics = _diag.Items,
        };
    }

    // ======================== symbols ========================

    bool DataNameTaken(string name) =>
        _defines.ContainsKey(name) || _enumValues.ContainsKey(name) || _constIds.ContainsKey(name) || _globals.ContainsKey(name);

    bool CheckNewDataName(string name, SourceSpan at)
    {
        if (Identifiers.Constants.ContainsKey(name))
        {
            Error("GZ4001", $"'{name}' is a built-in constant and cannot be redeclared", at);
            return false;
        }
        if (DataNameTaken(name))
        {
            Error("GZ4000", $"'{name}' is already declared", at);
            return false;
        }
        return true;
    }

    void CollectNames()
    {
        foreach (var d in _unit.Decls)
        {
            switch (d)
            {
                case DefineDecl def:
                    if (CheckNewDataName(def.Name, def.Span)) _defines[def.Name] = def;
                    break;
                case FunctionDecl fn:
                    if (Builtins.ByName.ContainsKey(fn.Name) || Builtins.ComboOps.ContainsKey(fn.Name))
                        Error("GZ4001", $"'{fn.Name}' is a built-in function name", fn.Span);
                    else if (!_funcs.TryAdd(fn.Name, new FuncInfo { Decl = fn }))
                        Error("GZ4000", $"function '{fn.Name}' is already declared", fn.Span);
                    else
                    {
                        var seen = new HashSet<string>();
                        foreach (var p in fn.Parameters)
                            if (!seen.Add(p)) Error("GZ4000", $"duplicate parameter '{p}'", fn.Span);
                        ScanReturns(fn.Body, _funcs[fn.Name]);
                        if (_funcs[fn.Name].ReturnsValue && _funcs[fn.Name].ReturnsBare)
                            Error("GZ5125", $"function '{fn.Name}' mixes 'return value;' and bare 'return;'", fn.Span);
                    }
                    break;
                case ComboDecl c:
                    if (_comboIds.ContainsKey(c.Name)) Error("GZ4000", $"combo '{c.Name}' is already declared", c.Span);
                    else { _comboIds[c.Name] = _combos.Count; _combos.Add(c); }
                    break;
            }
        }

        // enums, consts and variables in source order (each may use defines declared anywhere)
        foreach (var d in _unit.Decls)
        {
            switch (d)
            {
                case EnumDecl en:
                {
                    int next = 0;
                    foreach (var m in en.Members)
                    {
                        if (m.Value != null) next = EvalConst(m.Value) ?? next;
                        if (CheckNewDataName(m.Name, m.Span)) _enumValues[m.Name] = next;
                        next = unchecked(next + 1);
                    }
                    break;
                }
                case ConstDecl c:
                    CollectConst(c);
                    break;
                case VarDecl v:
                    foreach (var item in v.Items) CollectVar(item);
                    break;
            }
        }
        if (_slots > GpcCompiler.MaxVariableSlots)
            Error("GZ5143", $"the script uses {_slots} variable slots; the limit is {GpcCompiler.MaxVariableSlots}", new SourceSpan(0, 0, 1, 1));
    }

    static void ScanReturns(Stmt s, FuncInfo f)
    {
        switch (s)
        {
            case ReturnStmt r: if (r.Value != null) f.ReturnsValue = true; else f.ReturnsBare = true; break;
            case Block b: foreach (var x in b.Statements) ScanReturns(x, f); break;
            case IfStmt i: ScanReturns(i.Then, f); if (i.Else != null) ScanReturns(i.Else, f); break;
            case WhileStmt w: ScanReturns(w.Body, f); break;
            case DoWhileStmt dw: ScanReturns(dw.Body, f); break;
            case ForStmt fo: ScanReturns(fo.Body, f); break;
            case SwitchStmt sw: foreach (var c in sw.Cases) ScanReturns(c.Body, f); break;
        }
    }

    void CollectVar(VarItem item)
    {
        if (!CheckNewDataName(item.Name, item.Span)) return;
        int size = 1;
        if (item.ArraySize != null)
        {
            int? n = EvalConst(item.ArraySize);
            if (n == null) Error("GZ5103", "array size must be a compile-time constant", item.ArraySize.Span);
            else if (n < 0) Error("GZ5123", "array size cannot be negative", item.ArraySize.Span);
            else size = n.Value;
            if (size == 0) Warn("GZ6031", $"'{item.Name}[0]' takes no slot and aliases the next variable", item.Span);
        }
        _globals[item.Name] = new Global(_slots, size);
        for (int i = 0; i < size; i++) _globalInit.Add(0);
        if (item.Init != null && item.ArraySize == null)
        {
            int? v = EvalConst(item.Init);
            if (v == null) Error("GZ5102", "variable initializers must be compile-time constants", item.Init.Span);
            else _globalInit[_slots] = v.Value;
        }
        _slots += size;
    }

    static (long min, long max)? ElementRange(string type) => type switch
    {
        "int8" or "char" => (sbyte.MinValue, sbyte.MaxValue),
        "uint8" or "byte" or "image" or "ps5adt" or "string" => (0, byte.MaxValue),
        "int16" or "int" => (short.MinValue, short.MaxValue),
        "uint16" => (0, ushort.MaxValue),
        "int24" => (-8388608, 8388607),
        "uint24" => (0, 16777215),
        "int32" => (int.MinValue, int.MaxValue),
        _ => null,
    };

    void CollectConst(ConstDecl c)
    {
        if (!CheckNewDataName(c.Name, c.Span)) return;
        if (c.ElementType is "int" or "char" or "byte")
            Warn("GZ3001", $"const element type '{c.ElementType}' is deprecated; use a sized type such as int16", c.Span);
        var range = ElementRange(c.ElementType) ?? (int.MinValue, int.MaxValue);

        int Value(ConstInit init)
        {
            if (init is ConstValue cv)
            {
                int? v = EvalConst(cv.Value);
                if (v == null) { Error("GZ5102", "const data must be compile-time constants", cv.Span); return 0; }
                if (v < range.min || v > range.max)
                    Error("GZ5114", $"{v} does not fit in {c.ElementType} ({range.min}..{range.max})", cv.Span);
                return v.Value;
            }
            Error("GZ2100", "expected a value", init.Span);
            return 0;
        }

        ConstArray arr;
        if (c.Init is ConstString s)
        {
            if (c.ElementType != "string") Error("GZ2100", "only const string takes a string initializer", c.Span);
            if (c.Dimensions != 0) Error("GZ2100", "a string array needs '{ \"a\", \"b\" }'", c.Span);
            arr = new ConstArray(c.Name, "string", 0, 1, s.Value.Length, new[] { AddString(s.Value) }) { HoldsAddresses = true };
        }
        else if (c.ElementType == "string" && c.Init is ConstList strings)
        {
            // each element is the address of its string, which is what print() takes
            var addrs = new List<int>();
            foreach (var item in strings.Items)
            {
                if (item is ConstString str) addrs.Add(AddString(str.Value));
                else Error("GZ2100", "const string arrays hold string literals", item.Span);
            }
            if (addrs.Count == 0) Error("GZ4017", "const arrays cannot be empty", c.Span);
            arr = new ConstArray(c.Name, "string", 1, 1, addrs.Count, addrs.ToArray()) { HoldsAddresses = true };
        }
        else if (c.ElementType == "image" && c.Init is ConstList image && c.Dimensions == 0)
        {
            var bytes = image.Items.Select(Value).ToArray();
            if (bytes.Length < 3 || bytes.Length - 2 != (bytes[0] * bytes[1] + 7) / 8)
                Error("GZ5116", "an image needs width, height and exactly ceil(width*height/8) pixel bytes", c.Span);
            int at = _strings.Count;
            _strings.AddRange(bytes.Select(b => (byte)b));
            arr = new ConstArray(c.Name, "image", 0, 1, bytes.Length, new[] { at }) { HoldsAddresses = true };
        }
        else if (c.Init is ConstList list && c.Dimensions == 2)
        {
            var rows = list.Items.Select(r => r is ConstList rl ? rl.Items.Select(Value).ToArray() : new[] { Value(r) }).ToList();
            if (rows.Count == 0 || rows.Any(r => r.Length == 0)) Error("GZ4017", "const arrays and their rows cannot be empty", c.Span);
            int cols = rows.Count == 0 ? 0 : rows[0].Length;
            if (rows.Any(r => r.Length != cols)) Error("GZ4018", "every row of a 2D const array needs the same length", c.Span);
            arr = new ConstArray(c.Name, c.ElementType, 2, rows.Count, cols, rows.SelectMany(r => r.Take(cols).Concat(Enumerable.Repeat(0, Math.Max(0, cols - r.Length)))).ToArray());
        }
        else if (c.Init is ConstList list1)
        {
            if (c.Dimensions == 0 && c.ElementType is not ("image" or "ps5adt"))
                Error("GZ2112", $"const '{c.Name}' needs '[]' for an array initializer", c.Span);
            var values = list1.Items.Select(Value).ToArray();
            if (values.Length == 0) Error("GZ4017", "const arrays cannot be empty", c.Span);
            if (c.ElementType == "ps5adt" && values.Length != 11) Error("GZ4019", "a ps5adt record needs exactly 11 values", c.Span);
            arr = new ConstArray(c.Name, c.ElementType, 1, 1, values.Length, values);
        }
        else
        {
            Error("GZ2100", "const data needs a '{ ... }' or string initializer", c.Span);
            return;
        }
        _constIds[c.Name] = _consts.Count;
        _consts.Add(arr);
    }

    int AddString(string value)
    {
        int at = _strings.Count;
        foreach (char ch in value) _strings.Add((byte)ch);
        _strings.Add(0);
        return at;
    }

    void CollectData()
    {
        foreach (var d in _unit.Decls)
        {
            switch (d)
            {
                case DataDecl data:
                    foreach (var e in data.Values)
                    {
                        int v = EvalConst(e) ?? 0;
                        if (EvalConst(e) == null) Error("GZ5102", "data() values must be compile-time constants", e.Span);
                        if (v is >= -128 and <= 255) _data.Add((byte)v);
                        else if (v is >= short.MinValue and <= ushort.MaxValue)
                        {
                            Warn("GZ6032", $"data value {v} takes 2 bytes; later offsets shift by 1", e.Span);
                            _data.Add((byte)v); _data.Add((byte)(v >> 8));
                        }
                        else
                        {
                            Warn("GZ6034", $"data value {v} takes 4 bytes; later offsets shift by 3", e.Span);
                            for (int i = 0; i < 4; i++) _data.Add((byte)(v >> (8 * i)));
                        }
                    }
                    break;
                case RemapDecl r:
                {
                    int? src = EvalConst(r.Source);
                    int? dst = r.Target == null ? -1 : EvalConst(r.Target);
                    if (src == null || dst == null) { Error("GZ5102", "remap identifiers must be constants", r.Span); break; }
                    if (dst == Identifiers.Constants["REMAP_NULL"] && r.Target != null)
                        Warn("GZ3000", "remap to REMAP_NULL is deprecated; use unmap", r.Span);
                    if (src < 0 || src >= Identifiers.SlotCount || dst >= Identifiers.SlotCount)
                        Error("GZ5153", "remap identifier out of range", r.Span);
                    else _remaps.Add(new Remap(src.Value, dst < 0 ? -1 : dst.Value));
                    break;
                }
            }
        }
    }

    // ======================== constant evaluation ========================

    bool TryConstName(string name, out int value)
    {
        if (_curFunc != null && _curFunc.Decl.Parameters.Contains(name)) { value = 0; return false; }
        if (_globals.ContainsKey(name)) { value = 0; return false; }
        if (_enumValues.TryGetValue(name, out value)) return true;
        if (_defines.TryGetValue(name, out var def))
        {
            if (_defineValues.TryGetValue(name, out value)) return true;
            if (!_evaluating.Add(name))
            {
                Error("GZ5102", $"define '{name}' refers to itself", def.Span);
                return false;
            }
            int? v = EvalConst(def.Value);
            _evaluating.Remove(name);
            if (v == null)
            {
                if (def.Value is StringLit) Error("GZ4029", "a define cannot hold a string", def.Span);
                else Error("GZ5102", $"define '{name}' is not a compile-time constant", def.Span);
                v = 0;
            }
            _defineValues[name] = value = v.Value;
            return true;
        }
        return Identifiers.Constants.TryGetValue(name, out value);
    }

    /// <summary>Folds a constant expression, or returns null without reporting when it is not constant.</summary>
    int? EvalConst(Expr e)
    {
        switch (e)
        {
            case IntLit i: return i.Value;
            case NameExpr n: return TryConstName(n.Name, out int v) ? v : null;
            case UnaryExpr u:
            {
                int? a = EvalConst(u.Operand);
                return a == null ? null : Arith.Unary(UnaryOp(u.Op), a.Value);
            }
            case BinaryExpr b:
            {
                int? l = EvalConst(b.Left), r = EvalConst(b.Right);
                if (l == null || r == null) return null;
                return b.Op switch
                {
                    TokenKind.AmpAmp => (l != 0 && r != 0) ? 1 : 0,
                    TokenKind.PipePipe => (l != 0 || r != 0) ? 1 : 0,
                    _ => Arith.Binary(BinaryOp(b.Op), l.Value, r.Value, _flags),
                };
            }
            case TernaryExpr t:
            {
                int? c = EvalConst(t.Condition);
                if (c == null) return null;
                return c != 0 ? EvalConst(t.WhenTrue) : EvalConst(t.WhenFalse);
            }
            case SizeofExpr s: return Sizeof(s);
            default: return null;
        }
    }

    int? Sizeof(SizeofExpr s)
    {
        if (s.TypeName != null)
            return s.TypeName switch { "int8" or "uint8" => 1, "int16" or "uint16" => 2, "int24" or "uint24" => 3, _ => 4 };
        switch (s.Operand)
        {
            case NameExpr n when _constIds.TryGetValue(n.Name, out int id):
                return _consts[id].ElementType == "string" && _consts[id].Dimensions == 0 ? _consts[id].Columns : _consts[id].Length;
            case NameExpr n when _globals.TryGetValue(n.Name, out var g):
                return g.Size;
            case NameExpr n when _curFunc != null && _curFunc.Decl.Parameters.Contains(n.Name):
                return 1;
            case IndexExpr { Target: NameExpr n } when _constIds.TryGetValue(n.Name, out int id2) && _consts[id2].Dimensions == 2:
                return _consts[id2].Columns;
            case IndexExpr:
                return 1;
        }
        Error("GZ4030", "sizeof needs a type, variable or const array", s.Span);
        return 0;
    }

    static Op UnaryOp(TokenKind k) => k switch
    {
        TokenKind.Minus => Op.Neg,
        TokenKind.Bang => Op.Not,
        TokenKind.Tilde => Op.BitNot,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    static Op BinaryOp(TokenKind k) => k switch
    {
        TokenKind.Plus or TokenKind.PlusAssign => Op.Add,
        TokenKind.Minus or TokenKind.MinusAssign => Op.Sub,
        TokenKind.Star or TokenKind.StarAssign => Op.Mul,
        TokenKind.Slash or TokenKind.SlashAssign => Op.Div,
        TokenKind.Percent or TokenKind.PercentAssign => Op.Mod,
        TokenKind.Shl or TokenKind.ShlAssign => Op.Shl,
        TokenKind.Shr or TokenKind.ShrAssign => Op.Shr,
        TokenKind.Amp or TokenKind.AmpAssign => Op.And,
        TokenKind.Pipe or TokenKind.PipeAssign => Op.Or,
        TokenKind.Caret or TokenKind.CaretAssign => Op.Xor,
        TokenKind.CaretCaret or TokenKind.CaretCaretAssign => Op.LXor,
        TokenKind.Lt => Op.Lt, TokenKind.Le => Op.Le, TokenKind.Gt => Op.Gt, TokenKind.Ge => Op.Ge,
        TokenKind.EqEq => Op.Eq, TokenKind.NotEq => Op.Ne,
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, null),
    };

    // ======================== emission helpers ========================

    void Emit(Op op, params int[] operands)
    {
        _code.Add((int)op);
        _code.AddRange(operands);
    }

    /// <summary>Emits a jump and returns the operand position to patch.</summary>
    int EmitJump(Op op)
    {
        Emit(op, -1);
        return _code.Count - 1;
    }

    void Patch(int site) => _code[site] = _code.Count;
    void PatchTo(int site, int target) => _code[site] = target;

    // ======================== statements ========================

    void EmitBlock(Block b)
    {
        foreach (var s in b.Statements) EmitStmt(s);
    }

    void EmitStmt(Stmt s)
    {
        switch (s)
        {
            case Block b: EmitBlock(b); break;
            case EmptyStmt: break;
            case ExprStmt es: EmitExprStatement(es.Expr); break;
            case IfStmt i:
            {
                int? c = EvalConst(i.Condition);
                EmitExpr(i.Condition);
                int toElse = EmitJump(Op.Jz);
                EmitStmt(i.Then);
                if (i.Else != null)
                {
                    int toEnd = EmitJump(Op.Jmp);
                    Patch(toElse);
                    EmitStmt(i.Else);
                    Patch(toEnd);
                }
                else Patch(toElse);
                if (c == 0) Warn("GZ3007", "condition is always false", i.Condition.Span);
                break;
            }
            case WhileStmt w:
            {
                CheckEndless(w.Condition, w.Body, "GZ4027", "while");
                int top = _code.Count;
                EmitExpr(w.Condition);
                int exit = EmitJump(Op.Jz);
                var ctx = PushLoop(false);
                EmitStmt(w.Body);
                PopLoop(ctx, continueTarget: top);
                Emit(Op.Jmp, top);
                Patch(exit);
                PatchBreaks(ctx);
                break;
            }
            case DoWhileStmt dw:
            {
                CheckEndless(dw.Condition, dw.Body, "GZ4026", "do-while");
                int top = _code.Count;
                var ctx = PushLoop(false);
                EmitStmt(dw.Body);
                int cond = _code.Count;
                EmitExpr(dw.Condition);
                Emit(Op.Jnz, top);
                PopLoop(ctx, continueTarget: cond);
                PatchBreaks(ctx);
                break;
            }
            case ForStmt f:
            {
                CheckEndless(f.Condition, f.Body, "GZ4028", "for");
                if (f.Init != null) EmitExprStatement(f.Init, allowNoEffect: true);
                int top = _code.Count;
                int exit = -1;
                if (f.Condition != null)
                {
                    EmitExpr(f.Condition);
                    exit = EmitJump(Op.Jz);
                }
                var ctx = PushLoop(false);
                EmitStmt(f.Body);
                int step = _code.Count;
                if (f.Step != null) EmitExprStatement(f.Step, allowNoEffect: true);
                Emit(Op.Jmp, top);
                PopLoop(ctx, continueTarget: step);
                if (exit >= 0) Patch(exit);
                PatchBreaks(ctx);
                break;
            }
            case SwitchStmt sw: EmitSwitch(sw); break;
            case BreakStmt br:
            {
                if (_loops.Count == 0) { Error("GZ4005", "'break' outside a loop or switch", br.Span); break; }
                _loops[^1].Breaks.Add(EmitJump(Op.Jmp));
                break;
            }
            case ContinueStmt co:
            {
                var loop = _loops.LastOrDefault(l => !l.IsSwitch);
                if (loop == null) { Error("GZ4005", "'continue' outside a loop", co.Span); break; }
                loop.Continues.Add(EmitJump(Op.Jmp));
                break;
            }
            case ReturnStmt r:
            {
                if (_curFunc == null) { Error("GZ4005", "'return' is only allowed inside a function", r.Span); break; }
                if (r.Value != null)
                {
                    if (r.Value is IncDecExpr { Prefix: false }) Warn("GZ6029", "postfix ++/-- in return: the old value is returned", r.Span);
                    EmitExpr(r.Value);
                }
                else Emit(Op.Push, 0);
                Emit(Op.Ret);
                break;
            }
            case WaitStmt w:
                Error("GZ4010", "wait() is only allowed at the top level of a combo", w.Span);
                break;
            case CallComboStmt c:
                Error("GZ4010", "call() is only allowed at the top level of a combo", c.Span);
                break;
        }
    }

    void CheckEndless(Expr? cond, Stmt body, string code, string kind)
    {
        bool alwaysTrue = cond == null || EvalConst(cond) is int v && v != 0;
        if (alwaysTrue && !ContainsExit(body))
            Error(code, $"this {kind} loop never ends: it would lock the VM cycle", body.Span);
    }

    static bool ContainsExit(Stmt s) => s switch
    {
        BreakStmt or ReturnStmt => true,
        Block b => b.Statements.Any(ContainsExit),
        IfStmt i => ContainsExit(i.Then) || (i.Else != null && ContainsExit(i.Else)),
        SwitchStmt sw => sw.Cases.Any(c => c.Body.Statements.Any(x => x is ReturnStmt || (x is not BreakStmt && ContainsExit(x)))),
        WhileStmt or DoWhileStmt or ForStmt => ContainsReturn(s),
        _ => false,
    };

    static bool ContainsReturn(Stmt s) => s switch
    {
        ReturnStmt => true,
        Block b => b.Statements.Any(ContainsReturn),
        IfStmt i => ContainsReturn(i.Then) || (i.Else != null && ContainsReturn(i.Else)),
        WhileStmt w => ContainsReturn(w.Body),
        DoWhileStmt d => ContainsReturn(d.Body),
        ForStmt f => ContainsReturn(f.Body),
        SwitchStmt sw => sw.Cases.Any(c => ContainsReturn(c.Body)),
        _ => false,
    };

    LoopCtx PushLoop(bool isSwitch)
    {
        var ctx = new LoopCtx { IsSwitch = isSwitch };
        _loops.Add(ctx);
        return ctx;
    }

    void PopLoop(LoopCtx ctx, int continueTarget)
    {
        _loops.Remove(ctx);
        foreach (int site in ctx.Continues) PatchTo(site, continueTarget);
    }

    void PatchBreaks(LoopCtx ctx)
    {
        foreach (int site in ctx.Breaks) Patch(site);
    }

    void EmitSwitch(SwitchStmt sw)
    {
        if (sw.Cases.Count == 0) Warn("GZ6026", "empty switch", sw.Span);
        EmitExpr(sw.Value);
        var seen = new HashSet<int>();
        var stubs = new List<(int site, int caseIndex)>();
        int defaultIndex = -1;
        for (int i = 0; i < sw.Cases.Count; i++)
        {
            var c = sw.Cases[i];
            if (c.Value == null)
            {
                if (defaultIndex >= 0) Error("GZ5139", "switch has more than one default", c.Span);
                defaultIndex = i;
                continue;
            }
            int? v = EvalConst(c.Value);
            if (v == null) { Error("GZ5138", "case values must be compile-time constants", c.Value.Span); continue; }
            if (!seen.Add(v.Value)) Error("GZ5139", $"duplicate case {v}", c.Value.Span);
            Emit(Op.Dup);
            Emit(Op.Push, v.Value);
            Emit(Op.Eq);
            stubs.Add((EmitJump(Op.Jnz), i));
        }
        Emit(Op.Pop);
        int toDefault = EmitJump(Op.Jmp);

        // each matching case first drops the switch value, then enters its body
        var bodyJumps = new List<(int site, int caseIndex)>();
        foreach (var (site, idx) in stubs)
        {
            Patch(site);
            Emit(Op.Pop);
            bodyJumps.Add((EmitJump(Op.Jmp), idx));
        }

        var ctx = PushLoop(true);
        var bodyStart = new int[sw.Cases.Count];
        for (int i = 0; i < sw.Cases.Count; i++)
        {
            bodyStart[i] = _code.Count;
            EmitBlock(sw.Cases[i].Body);
            var last = sw.Cases[i].Body.Statements.LastOrDefault();
            if (i < sw.Cases.Count - 1 && last is not (BreakStmt or ReturnStmt or ContinueStmt))
                Warn("GZ6018", "case falls through to the next case", sw.Cases[i].Span);
        }
        _loops.Remove(ctx);
        foreach (var (site, idx) in bodyJumps) PatchTo(site, bodyStart[idx]);
        if (defaultIndex >= 0) PatchTo(toDefault, bodyStart[defaultIndex]);
        else Patch(toDefault);
        // continue inside a switch belongs to the enclosing loop
        if (ctx.Continues.Count > 0)
        {
            var outer = _loops.LastOrDefault(l => !l.IsSwitch);
            if (outer != null) outer.Continues.AddRange(ctx.Continues);
        }
        PatchBreaks(ctx);
    }

    int[] EmitCombo(ComboDecl c)
    {
        _curCombo = _comboIds[c.Name];
        var steps = new List<int> { _code.Count };
        foreach (var s in c.Body.Statements)
        {
            switch (s)
            {
                case WaitStmt w:
                    EmitExpr(w.Duration);
                    if (EvalConst(w.Duration) is int ms && (ms < 0 || ms > Identifiers.Constants["TIME_MAX"]))
                        Error("GZ5132", $"wait({ms}) is out of range", w.Span);
                    Emit(Op.StepWait);
                    steps.Add(_code.Count);
                    break;
                case CallComboStmt call:
                    if (!_comboIds.TryGetValue(call.Combo, out int child))
                        Error("GZ5129", $"undefined combo '{call.Combo}'", call.Span);
                    else if (child == _comboIds[c.Name])
                        Error("GZ4015", $"combo '{c.Name}' calls itself", call.Span);
                    Emit(Op.StepCall, child);
                    steps.Add(_code.Count);
                    break;
                case ReturnStmt r:
                    Error("GZ4005", "'return' is not allowed in a combo", r.Span);
                    break;
                default:
                    EmitStmt(s);
                    break;
            }
        }
        Emit(Op.StepDone);
        if (c.Body.Statements.Count == 0) Warn("GZ6038", $"combo '{c.Name}' is empty", c.Span);
        _curCombo = -1;
        return steps.ToArray();
    }

    void EmitFunction(FuncInfo f)
    {
        _curFunc = f;
        f.Addr = _code.Count;
        foreach (var p in f.Decl.Parameters)
            if (_globals.ContainsKey(p)) Warn("GZ3009", $"parameter '{p}' shadows a global", f.Decl.Span);
        EmitBlock(f.Decl.Body);
        Emit(Op.Push, 0);
        Emit(Op.Ret);
        _curFunc = null;
    }

    // ======================== expressions ========================

    void EmitExprStatement(Expr e, bool allowNoEffect = false)
    {
        switch (e)
        {
            case AssignExpr or IncDecExpr:
                EmitExpr(e);
                Emit(Op.Pop);
                break;
            case CallExpr call:
                if (EmitCall(call, asStatement: true)) Emit(Op.Pop);
                break;
            case TernaryExpr when !allowNoEffect:
                Error("GZ4032", "a conditional expression cannot be used as a statement; use if/else", e.Span);
                break;
            default:
                if (!allowNoEffect) Error("GZ5145", "this expression has no effect", e.Span);
                EmitExpr(e);
                Emit(Op.Pop);
                break;
        }
    }

    void EmitExpr(Expr e)
    {
        // fold constants, except names (a parameter or global may shadow a define)
        if (e is UnaryExpr or BinaryExpr or TernaryExpr or SizeofExpr && EvalConst(e) is int folded)
        {
            Emit(Op.Push, folded);
            return;
        }

        switch (e)
        {
            case IntLit i: Emit(Op.Push, i.Value); break;
            case StringLit s:
                Error("GZ5106", "string literals are only allowed in const string declarations", s.Span);
                Emit(Op.Push, 0);
                break;
            case NameExpr n: EmitName(n); break;
            case IndexExpr ix: EmitIndex(ix); break;
            case CallExpr c:
                if (!EmitCall(c, asStatement: false)) Emit(Op.Push, 0);
                break;
            case UnaryExpr u:
                EmitExpr(u.Operand);
                Emit(UnaryOp(u.Op));
                break;
            case BinaryExpr b: EmitBinary(b); break;
            case TernaryExpr t:
            {
                EmitExpr(t.Condition);
                int toElse = EmitJump(Op.Jz);
                EmitExpr(t.WhenTrue);
                int toEnd = EmitJump(Op.Jmp);
                Patch(toElse);
                EmitExpr(t.WhenFalse);
                Patch(toEnd);
                break;
            }
            case AssignExpr a: EmitAssign(a); break;
            case IncDecExpr inc: EmitIncDec(inc); break;
            case SizeofExpr s: Emit(Op.Push, Sizeof(s) ?? 0); break;
            case AddrExpr ad:
                if (!_constIds.TryGetValue(ad.Name, out int id))
                    Error("GZ5126", $"addr() needs a const item; '{ad.Name}' is not one", ad.Span);
                // strings and images: their byte address; other const arrays: their id
                Emit(Op.Push, id < _consts.Count && _consts[id].HoldsAddresses ? _consts[id].Values[0] : id);
                break;
            default:
                Error("GZ2102", "unsupported expression", e.Span);
                Emit(Op.Push, 0);
                break;
        }
    }

    void EmitBinary(BinaryExpr b)
    {
        if (b.Op is TokenKind.AmpAmp or TokenKind.PipePipe)
        {
            bool and = b.Op == TokenKind.AmpAmp;
            if (!_flags.ShortCircuit)
            {
                EmitExpr(b.Left); Emit(Op.Bool);
                EmitExpr(b.Right); Emit(Op.Bool);
                Emit(and ? Op.And : Op.Or);
                return;
            }
            EmitExpr(b.Left);
            int shortSite = EmitJump(and ? Op.Jz : Op.Jnz);
            EmitExpr(b.Right);
            Emit(Op.Bool);
            int end = EmitJump(Op.Jmp);
            Patch(shortSite);
            Emit(Op.Push, and ? 0 : 1);
            Patch(end);
            return;
        }
        EmitExpr(b.Left);
        EmitExpr(b.Right);
        var op = BinaryOp(b.Op);
        if (op is Op.Div or Op.Mod && EvalConst(b.Right) is 0)
            Warn("GZ6015", "division by zero", b.Span);
        Emit(op);
    }

    bool IsParam(string name, out int index)
    {
        index = _curFunc?.Decl.Parameters.IndexOf(name) ?? -1;
        return index >= 0;
    }

    void EmitName(NameExpr n)
    {
        if (IsParam(n.Name, out int p)) { Emit(Op.LoadP, p); return; }
        if (_globals.TryGetValue(n.Name, out var g)) { Emit(Op.LoadG, g.Slot); return; }
        if (TryConstName(n.Name, out int v)) { Emit(Op.Push, v); return; }
        if (_constIds.TryGetValue(n.Name, out int cid) && _consts[cid].HoldsAddresses && _consts[cid].Dimensions == 0)
        {
            Emit(Op.Push, _consts[cid].Values[0]); // a single string or image stands for its address
            return;
        }
        if (_constIds.ContainsKey(n.Name))
            Error("GZ4031", $"const array '{n.Name}' needs an index", n.Span);
        else if (_comboIds.ContainsKey(n.Name))
            Error("GZ4010", $"combo '{n.Name}' cannot be used as a value", n.Span);
        else if (_funcs.ContainsKey(n.Name) || Builtins.ByName.ContainsKey(n.Name))
            Error("GZ4004", $"function '{n.Name}' needs '(...)' to be called", n.Span);
        else
            Error("GZ4002", $"undefined identifier '{n.Name}'", n.Span);
        Emit(Op.Push, 0);
    }

    void EmitIndex(IndexExpr ix)
    {
        if (ix.Target is NameExpr n)
        {
            if (IsParam(n.Name, out _))
            {
                Error("GZ4007", $"parameter '{n.Name}' cannot be indexed", ix.Span);
                Emit(Op.Push, 0);
                return;
            }
            if (_globals.TryGetValue(n.Name, out var g))
            {
                CheckSlotIndex(g, ix.Index);
                EmitExpr(ix.Index);
                Emit(Op.LoadGI, g.Slot);
                return;
            }
            if (_constIds.TryGetValue(n.Name, out int id))
            {
                var arr = _consts[id];
                if (arr.Dimensions == 2)
                {
                    Error("GZ4031", $"2D const array '{n.Name}' needs two indexes", ix.Span);
                    Emit(Op.Push, 0);
                    return;
                }
                if (arr.HoldsAddresses && arr.Dimensions == 0)
                {
                    // TITLE[i]: the address of character i, so print(..., TITLE[0]) prints the whole string
                    EmitExpr(ix.Index);
                    Emit(Op.Push, arr.Values[0]);
                    Emit(Op.Add);
                    return;
                }
                CheckConstIndex(ix.Index, arr.Length);
                EmitExpr(ix.Index);
                Emit(Op.LoadC, id);
                return;
            }
        }
        if (ix.Target is IndexExpr { Target: NameExpr n2 } inner && _constIds.TryGetValue(n2.Name, out int id2))
        {
            var arr = _consts[id2];
            if (arr.Dimensions != 2)
            {
                Error("GZ4006", $"'{n2.Name}' is not a 2D const array", ix.Span);
                Emit(Op.Push, 0);
                return;
            }
            CheckConstIndex(inner.Index, arr.Rows);
            CheckConstIndex(ix.Index, arr.Columns);
            EmitExpr(inner.Index);
            EmitExpr(ix.Index);
            Emit(Op.LoadC2, id2);
            return;
        }
        if (ix.Target is NameExpr { Name: var name } && !DataNameTaken(name))
            Error("GZ4002", $"undefined identifier '{name}'", ix.Target.Span);
        else
            Error("GZ4006", "this cannot be indexed", ix.Span);
        Emit(Op.Push, 0);
    }

    void CheckSlotIndex(Global g, Expr index)
    {
        if (EvalConst(index) is not int i) return;
        if (g.Slot + i < 0) Error("GZ4022", "constant index reaches before the first variable slot", index.Span);
        else if (g.Slot + i >= _slots) Error("GZ4023", "constant index reaches past the last variable slot", index.Span);
    }

    void CheckConstIndex(Expr index, int length)
    {
        if (EvalConst(index) is not int i) return;
        if (i < 0) Error("GZ4020", "negative const index", index.Span);
        else if (i >= length) Error("GZ4021", $"index {i} is past the end (size {length})", index.Span);
    }

    bool TryLValue(Expr target, out LValue lv)
    {
        lv = default;
        switch (target)
        {
            case NameExpr n when IsParam(n.Name, out int p):
                lv = new LValue(LKind.Param, p, null);
                return true;
            case NameExpr n when _globals.TryGetValue(n.Name, out var g):
                lv = new LValue(LKind.Global, g.Slot, null);
                return true;
            case IndexExpr { Target: NameExpr n } ix when _globals.TryGetValue(n.Name, out var g):
                CheckSlotIndex(g, ix.Index);
                lv = new LValue(LKind.Indexed, g.Slot, ix.Index);
                return true;
        }
        if (target is NameExpr { Name: var name } && !DataNameTaken(name) && !Identifiers.Constants.ContainsKey(name))
            Error("GZ4002", $"undefined identifier '{name}'", target.Span);
        else
            Error("GZ4007", "only variables and variable array elements can be assigned", target.Span);
        return false;
    }

    void EmitLoad(LValue lv)
    {
        switch (lv.Kind)
        {
            case LKind.Global: Emit(Op.LoadG, lv.Operand); break;
            case LKind.Param: Emit(Op.LoadP, lv.Operand); break;
            case LKind.Indexed: Emit(Op.LoadGI, lv.Operand); break; // index already on stack
        }
    }

    void EmitStore(LValue lv)
    {
        switch (lv.Kind)
        {
            case LKind.Global: Emit(Op.StoreG, lv.Operand); break;
            case LKind.Param: Emit(Op.StoreP, lv.Operand); break;
            case LKind.Indexed: Emit(Op.StoreGI, lv.Operand); break;
        }
    }

    void EmitAssign(AssignExpr a)
    {
        if (!TryLValue(a.Target, out var lv))
        {
            EmitExpr(a.Value);
            return;
        }
        if (lv.Kind == LKind.Indexed) EmitExpr(lv.Index!);

        if (a.Op == TokenKind.Assign)
        {
            EmitExpr(a.Value);
            EmitStore(lv);
            return;
        }

        if (lv.Kind == LKind.Indexed) Emit(Op.Dup);
        EmitLoad(lv);
        switch (a.Op)
        {
            case TokenKind.AmpAmpAssign:
            case TokenKind.PipePipeAssign:
                Emit(Op.Bool);
                EmitExpr(a.Value);
                Emit(Op.Bool);
                Emit(a.Op == TokenKind.AmpAmpAssign ? Op.And : Op.Or);
                break;
            default:
                EmitExpr(a.Value);
                Emit(BinaryOp(a.Op));
                break;
        }
        EmitStore(lv);
    }

    void EmitIncDec(IncDecExpr inc)
    {
        if (!TryLValue(inc.Target, out var lv))
        {
            Emit(Op.Push, 0);
            return;
        }
        int delta = inc.Increment ? 1 : -1;
        int yieldOld = !inc.Prefix && _flags.PostIncrementYieldsOld ? 1 : 0;
        switch (lv.Kind)
        {
            case LKind.Global: Emit(Op.IncG, lv.Operand, delta, yieldOld); break;
            case LKind.Param: Emit(Op.IncP, lv.Operand, delta, yieldOld); break;
            case LKind.Indexed:
                EmitExpr(lv.Index!);
                Emit(Op.IncGI, lv.Operand, delta, yieldOld);
                break;
        }
    }

    /// <summary>Emits a call. Returns true when it left a value on the stack.</summary>
    bool EmitCall(CallExpr c, bool asStatement)
    {
        if (Builtins.ComboOps.TryGetValue(c.Callee, out var cop))
        {
            bool takesCombo = Builtins.ComboOpTakesCombo(cop);
            int id = 0;
            if (c.Args.Count != (takesCombo ? 1 : 0))
                Error("GZ5130", $"{c.Callee} takes {(takesCombo ? 1 : 0)} argument(s)", c.Span);
            else if (takesCombo)
            {
                if (c.Args[0] is not NameExpr n || !_comboIds.TryGetValue(n.Name, out id))
                    Error("GZ4009", $"{c.Callee} needs a combo name", c.Args[0].Span);
                else if (cop is ComboOp.Run or ComboOp.Resume && id == _curCombo)
                    Warn("GZ3008", "a combo starting itself has no effect", c.Span);
            }
            Emit(Op.Combo, (int)cop, id);
            bool returns = Builtins.ComboOpReturns(cop);
            if (returns && asStatement) Error("GZ5135", $"the result of {c.Callee} must be used", c.Span);
            if (!returns && !asStatement) Error("GZ5134", $"{c.Callee} does not return a value", c.Span);
            return returns;
        }

        if (_funcs.TryGetValue(c.Callee, out var f))
        {
            if (c.Args.Count != f.Decl.Parameters.Count)
                Error("GZ5130", $"'{c.Callee}' takes {f.Decl.Parameters.Count} argument(s), got {c.Args.Count}", c.Span);
            if (!asStatement && !f.ReturnsValue)
                Error("GZ5134", $"'{c.Callee}' does not return a value", c.Span);
            foreach (var a in c.Args) EmitExpr(a);
            Emit(Op.Call, -1, c.Args.Count);
            f.CallPatches.Add(_code.Count - 2);
            return true;
        }

        if (Builtins.ByName.TryGetValue(c.Callee, out var bi))
        {
            if (c.Args.Count < bi.Argc || c.Args.Count > bi.MaxArgc)
            {
                Error("GZ5130", $"{c.Callee} takes {bi.Argc} argument(s), got {c.Args.Count}", c.Span);
                return false;
            }
            if (c.Args.Count > bi.Argc) Warn("GZ9002", $"extra {c.Callee} arguments are ignored", c.Span);
            if (bi.Returns && asStatement) Error("GZ5135", $"the result of {c.Callee} must be used", c.Span);
            if (!bi.Returns && !asStatement) Error("GZ5134", $"{c.Callee} does not return a value", c.Span);

            if (bi.FirstArg == ArgKind.VarRef)
            {
                // read-modify-write: the builtin returns the updated value, which is stored back
                if (!TryLValue(c.Args[0], out var lv)) return false;
                if (lv.Kind == LKind.Indexed) { EmitExpr(lv.Index!); Emit(Op.Dup); }
                EmitLoad(lv);
                for (int i = 1; i < bi.Argc; i++) EmitExpr(c.Args[i]);
                Emit(Op.Builtin, (int)bi.Id, bi.Argc);
                EmitStore(lv);
                Emit(Op.Pop);
                return false;
            }

            for (int i = 0; i < c.Args.Count; i++)
            {
                if (c.Args[i] is StringLit s)
                {
                    // a literal passed as a whole argument becomes an unnamed const string (Zen Studio Live allows this)
                    Emit(Op.Push, AddString(s.Value));
                }
                else EmitExpr(c.Args[i]);
            }
            for (int i = bi.Argc; i < c.Args.Count; i++) Emit(Op.Pop);
            Emit(Op.Builtin, (int)bi.Id, bi.Argc);
            return bi.Returns;
        }

        Error("GZ5128", $"undefined function '{c.Callee}'", c.Span);
        foreach (var a in c.Args) EmitExpr(a);
        for (int i = 0; i < c.Args.Count; i++) Emit(Op.Pop);
        return false;
    }
}
