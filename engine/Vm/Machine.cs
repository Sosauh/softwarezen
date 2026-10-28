using Gpc.Engine.Runtime;
using static Gpc.Engine.Runtime.Identifiers;

namespace Gpc.Engine.Vm;

public enum FaultKind { None, IndexOutOfRange, StackOverflow, InstructionBudget }

/// <summary>
/// Runs a compiled script one VM cycle at a time. Time is the cycle index times the cycle
/// interval, so identical input sequences give identical outputs on any machine. Nothing on
/// the cycle path allocates.
/// </summary>
public sealed class Machine
{
    const int StackSize = 1024;
    const int MaxFrames = 256;
    const int HoldCapacity = SlotCount;
    static readonly int NotUse = Identifiers.Constants["NOT_USE"], DzCircle = Identifiers.Constants["DZ_CIRCLE"];
    static readonly int PioPs5 = Identifiers.Constants["PIO_PS5"], PioPs4 = Identifiers.Constants["PIO_PS4"];

    readonly CompiledScript _s;
    readonly int[] _code;
    readonly CompatFlags _f;

    // controller layers
    readonly int[] _input = new int[SlotCount];
    readonly int[] _prevInput = new int[SlotCount];
    readonly int[] _work = new int[SlotCount];
    readonly int[] _out = new int[SlotCount];
    readonly int[] _lastOut = new int[SlotCount];
    readonly int[] _remapScratch = new int[SlotCount];
    readonly bool[] _press = new bool[SlotCount], _release = new bool[SlotCount];
    readonly int[] _ptime = new int[SlotCount], _brtime = new int[SlotCount];
    readonly int[] _blockLeft = new int[SlotCount];
    readonly bool[] _written = new bool[SlotCount]; // set_val reached this slot this cycle (script or combo)
    readonly bool[] _isRemapTarget = new bool[SlotCount];
    // set_remapper: a device-level remap the script can change at run time (identity until changed)
    readonly int[] _deviceRemap = new int[SlotCount];
    readonly bool[] _isDeviceRemapTarget = new bool[SlotCount];
    int _deviceRemapCount;
    // PS5 adaptive-trigger records (R2, L2), 11 bytes each; kept for get/set/compare, not sent to the PC pad
    readonly int[,] _adt = new int[2, 11];

    // script state
    readonly int[] _globals;
    readonly int[] _stack = new int[StackSize];
    readonly int[] _frameRet = new int[MaxFrames], _frameFp = new int[MaxFrames];
    int _sp, _frames;

    // combos
    readonly bool[] _cRunning, _cSuspended, _cEntered;
    readonly int[] _cStep, _cTimeLeft, _cWaitChild;
    readonly long[] _cRanCycle;
    readonly int[,] _holdSlot, _holdValue;
    readonly int[] _holdCount;
    int _recordingCombo = -1;

    // feedback
    readonly int[] _rumbleIn = new int[RumbleCount], _rumbleOverride = new int[RumbleCount];
    readonly bool[] _rumbleOverridden = new bool[RumbleCount];
    bool _rumbleBlocked;
    readonly int[] _lightbarIn = new int[3], _lightbarOverride = new int[3];
    bool _lightbarOverridden;
    readonly int[] _leds = new int[4];

    // persistence (in memory until a host saves it)
    readonly int[] _pvar = new int[65];
    readonly bool[] _pvarSet = new bool[65];
    readonly short[] _bvar = new short[1024];

    readonly int[] _sin = new int[360];
    uint _rng;
    int _tctrl, _cycleMs, _elapsedMs, _budgetLeft;
    long _cycle;
    bool _initDone;

    public Machine(CompiledScript script, uint seed = 0x2545F491)
    {
        _s = script;
        _code = script.Code;
        _f = script.Flags;
        _globals = (int[])script.GlobalInit.Clone();
        int n = script.ComboSteps.Length;
        _cRunning = new bool[n]; _cSuspended = new bool[n]; _cEntered = new bool[n];
        _cStep = new int[n]; _cTimeLeft = new int[n]; _cWaitChild = new int[n]; _cRanCycle = new long[n];
        _holdSlot = new int[n, HoldCapacity]; _holdValue = new int[n, HoldCapacity]; _holdCount = new int[n];
        Array.Fill(_cWaitChild, -1);
        Array.Fill(_cRanCycle, -1);
        Array.Fill(_brtime, _f.InitialBrtime);
        foreach (var r in script.Remaps) if (r.Target >= 0) _isRemapTarget[r.Target] = true;
        for (int i = 0; i < SlotCount; i++) _deviceRemap[i] = i;
        for (int i = 0; i < 360; i++) _sin[i] = (int)Math.Round(Math.Sin(i * Math.PI / 180) * 10000, MidpointRounding.AwayFromZero);
        _rng = seed == 0 ? 1 : seed;
        _cycleMs = _f.BaseCycleMs;
    }

    /// <summary>Physical input for the next cycle, in GPC units. The host writes it before <see cref="RunCycle"/>.</summary>
    public Span<int> Input => _input;
    /// <summary>The report sent at the end of the last cycle.</summary>
    public ReadOnlySpan<int> Output => _out;
    public long CycleCount => _cycle;
    /// <summary>Interval until the next cycle, in ms (base interval plus vm_tctrl).</summary>
    public int NextIntervalMs => _cycleMs;

    public FaultKind LastFault { get; private set; }
    public long FaultCount { get; private set; }

    /// <summary>Rumble the game asked for, 0..100 per motor (RUMBLE_A..RUMBLE_LT).</summary>
    public Span<int> RumbleFromGame => _rumbleIn;
    /// <summary>Lightbar the game asked for, 0..255 RGB.</summary>
    public Span<int> LightbarFromGame => _lightbarIn;

    public int RumbleOut(int motor) =>
        _rumbleOverridden[motor] ? _rumbleOverride[motor] : _rumbleBlocked ? 0 : _rumbleIn[motor];

    public int LightbarOut(int channel) => _lightbarOverridden ? _lightbarOverride[channel] : _lightbarIn[channel];

    public int Trace(int n) => _out[TRACE_1 + n - 1];

    /// <summary>The script's 128x64 screen.</summary>
    public Oled Oled { get; } = new();

    ReadOnlySpan<byte> StringAt(int address) =>
        (uint)address < (uint)_s.Strings.Length ? _s.Strings.AsSpan(address) : ReadOnlySpan<byte>.Empty;

    // ================= cycle =================

    /// <summary>Runs one VM cycle and returns the interval in ms until the next one.</summary>
    public int RunCycle()
    {
        _elapsedMs = _cycle == 0 ? 0 : _cycleMs;
        _budgetLeft = _f.InstructionBudget;

        for (int i = 0; i < SlotCount; i++)
        {
            bool now = _input[i] != 0, before = _prevInput[i] != 0;
            _press[i] = now && !before;
            _release[i] = !now && before;
            _ptime[i] = now != before ? 0 : Sat(_ptime[i] + _elapsedMs);
            _brtime[i] = _release[i] ? 0 : Sat(_brtime[i] + _elapsedMs);
            _work[i] = _input[i];
            _written[i] = false;
        }

        if (!_initDone)
        {
            _initDone = true;
            Exec(_s.InitEntry, out _);
        }
        Exec(_s.MainEntry, out _);
        for (int c = 0; c < _cRunning.Length; c++) RunCombo(c);

        BuildReport();
        Array.Copy(_input, _prevInput, SlotCount);
        _cycle++;

        _cycleMs = Math.Max(1, _f.BaseCycleMs + _tctrl);
        if (!_f.VmTctrlPersists) _tctrl = 0;
        return _cycleMs;
    }

    int Sat(int v) => v < 0 || v > _f.TimeSaturation ? _f.TimeSaturation : v;

    void BuildReport()
    {
        Array.Copy(_work, _out, SlotCount);
        for (int i = 0; i < SlotCount; i++)
        {
            if (_blockLeft[i] <= 0) continue;
            if (!_f.BlockOnlyPhysical || !_written[i]) _out[i] = 0;
            _blockLeft[i] -= _cycleMs;
        }
        if (_f.LvalLayer == RemapLayer.PreRemap) Array.Copy(_out, _lastOut, SlotCount);

        if (_s.Remaps.Length > 0)
        {
            Array.Copy(_out, _remapScratch, SlotCount);
            foreach (var r in _s.Remaps)
            {
                if (r.Target < 0) _out[r.Source] = 0;
                else _out[r.Target] = _remapScratch[r.Source]; // the source keeps its own output unless remapped onto
            }
        }
        if (_deviceRemapCount > 0)
        {
            Array.Copy(_out, _remapScratch, SlotCount);
            for (int i = 0; i < SlotCount; i++)
            {
                int to = _deviceRemap[i];
                if (to == i) continue;
                _out[to] = _remapScratch[i];
            }
        }
        if (_f.LvalLayer == RemapLayer.PostRemap) Array.Copy(_out, _lastOut, SlotCount);
    }

    void SetDeviceRemap(int from, int to)
    {
        if (!ValidSlot(from) || !ValidSlot(to)) return;
        _deviceRemap[from] = to;
        Array.Clear(_isDeviceRemapTarget);
        _deviceRemapCount = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_deviceRemap[i] == i) continue;
            _deviceRemapCount++;
            _isDeviceRemapTarget[_deviceRemap[i]] = true;
        }
    }

    /// <summary>SPVAR_1..SPVAR_64 are -32768..-32705 (cronus.support memory constants); returns 1..64, or -1.</summary>
    static int PvarIndex(int spvar) => spvar is >= -32768 and <= -32705 ? spvar + 32769 : -1;

    static int AdtSide(int trigger) => trigger == R2 ? 0 : trigger == L2 ? 1 : -1;

    // ================= combos =================

    void StartCombo(int c)
    {
        _cRunning[c] = true;
        _cSuspended[c] = false;
        _cStep[c] = 0;
        _cEntered[c] = true;
        _cTimeLeft[c] = 0;
        _cWaitChild[c] = -1;
        _holdCount[c] = 0;
    }

    void Advance(int c)
    {
        _cStep[c]++;
        _cEntered[c] = true;
        _holdCount[c] = 0;
        var steps = _s.ComboSteps[c];
        if (_cStep[c] >= steps.Length || _code[steps[_cStep[c]]] == (int)Op.StepDone)
            _cRunning[c] = false;
    }

    void RunCombo(int c)
    {
        if (!_cRunning[c] || _cSuspended[c] || _cRanCycle[c] == _cycle) return;
        _cRanCycle[c] = _cycle;

        if (_cWaitChild[c] >= 0)
        {
            if (_cRunning[_cWaitChild[c]]) return;
            _cWaitChild[c] = -1;
            Advance(c);
            if (!_cRunning[c]) return;
        }

        bool entered = _cEntered[c];
        Exit exit;
        int value;
        if (!entered && _f.ComboStepMode == ComboStepMode.RecordAndHold)
        {
            for (int i = 0; i < _holdCount[c]; i++) _work[_holdSlot[c, i]] = _holdValue[c, i];
            exit = Exit.StepWait;
            value = 0;
        }
        else
        {
            _recordingCombo = entered && _f.ComboStepMode == ComboStepMode.RecordAndHold ? c : -1;
            exit = Exec(_s.ComboSteps[c][_cStep[c]], out value);
            _recordingCombo = -1;
        }

        switch (exit)
        {
            case Exit.StepWait:
                if (entered) { _cTimeLeft[c] = value; _cEntered[c] = false; }
                _cTimeLeft[c] -= _cycleMs;
                if (_cTimeLeft[c] <= 0) Advance(c);
                break;
            case Exit.StepCall:
                _cEntered[c] = false;
                _cWaitChild[c] = value;
                StartCombo(value);
                _cRanCycle[value] = -1;
                RunCombo(value);
                break;
            default: // StepDone, Fault
                _cRunning[c] = false;
                break;
        }
    }

    void ComboOperation(ComboOp op, int c)
    {
        switch (op)
        {
            case ComboOp.Run: if (!_cRunning[c]) StartCombo(c); break;
            case ComboOp.Restart: StartCombo(c); break;
            case ComboOp.Stop: _cRunning[c] = false; break;
            case ComboOp.Running: Push(_cRunning[c] ? 1 : 0); break;
            case ComboOp.Suspend: if (_cRunning[c]) _cSuspended[c] = true; break;
            case ComboOp.Suspended: Push(_cRunning[c] && _cSuspended[c] ? 1 : 0); break;
            case ComboOp.Resume: _cSuspended[c] = false; break;
            case ComboOp.CurrentStep: Push(_cRunning[c] ? _cStep[c] : 0); break;
            case ComboOp.StepTimeLeft: Push(_cRunning[c] ? Math.Max(0, _cTimeLeft[c]) : 0); break;
            case ComboOp.StopAll: Array.Clear(_cRunning); break;
            case ComboOp.SuspendAll: for (int i = 0; i < _cRunning.Length; i++) if (_cRunning[i]) _cSuspended[i] = true; break;
            case ComboOp.ResumeAll: Array.Clear(_cSuspended); break;
        }
    }

    // ================= interpreter =================

    enum Exit { Halt, StepWait, StepCall, StepDone, Fault }

    void Push(int v) => _stack[_sp++] = v;

    void Fault(FaultKind k)
    {
        LastFault = k;
        FaultCount++;
    }

    int LoadSlot(int index)
    {
        if ((uint)index < (uint)_globals.Length) return _globals[index];
        Fault(FaultKind.IndexOutOfRange);
        return 0;
    }

    void StoreSlot(int index, int value)
    {
        if ((uint)index < (uint)_globals.Length) _globals[index] = value;
        else Fault(FaultKind.IndexOutOfRange);
    }

    Exit Exec(int pc, out int value)
    {
        value = 0;
        _sp = 0;
        _frames = 0;
        int fp = 0;
        var code = _code;
        var st = _stack;

        while (true)
        {
            if (--_budgetLeft < 0)
            {
                Fault(FaultKind.InstructionBudget);
                return Exit.Fault;
            }
            if (_sp >= StackSize - 8)
            {
                Fault(FaultKind.StackOverflow);
                return Exit.Fault;
            }

            var op = (Op)code[pc++];
            switch (op)
            {
                case Op.Halt: return Exit.Halt;
                case Op.Push: st[_sp++] = code[pc++]; break;
                case Op.Pop: _sp--; break;
                case Op.Dup: st[_sp] = st[_sp - 1]; _sp++; break;
                case Op.LoadG: st[_sp++] = _globals[code[pc++]]; break;
                case Op.StoreG: _globals[code[pc++]] = st[_sp - 1]; break;
                case Op.LoadGI: st[_sp - 1] = LoadSlot(unchecked(code[pc++] + st[_sp - 1])); break;
                case Op.StoreGI:
                {
                    int v = st[--_sp];
                    StoreSlot(unchecked(code[pc++] + st[_sp - 1]), v);
                    st[_sp - 1] = v;
                    break;
                }
                case Op.LoadP: st[_sp++] = st[fp + code[pc++]]; break;
                case Op.StoreP: st[fp + code[pc++]] = st[_sp - 1]; break;
                case Op.IncG:
                {
                    int slot = code[pc++], delta = code[pc++], old = code[pc++];
                    int before = _globals[slot];
                    int after = unchecked(before + delta);
                    _globals[slot] = after;
                    st[_sp++] = old != 0 ? before : after;
                    break;
                }
                case Op.IncP:
                {
                    int p = fp + code[pc++], delta = code[pc++], old = code[pc++];
                    int before = st[p];
                    int after = unchecked(before + delta);
                    st[p] = after;
                    st[_sp++] = old != 0 ? before : after;
                    break;
                }
                case Op.IncGI:
                {
                    int slot = unchecked(code[pc++] + st[_sp - 1]), delta = code[pc++], old = code[pc++];
                    int before = LoadSlot(slot);
                    int after = unchecked(before + delta);
                    StoreSlot(slot, after);
                    st[_sp - 1] = old != 0 ? before : after;
                    break;
                }
                case Op.LoadC:
                {
                    var arr = _s.Consts[code[pc++]];
                    int i = st[_sp - 1];
                    if ((uint)i < (uint)arr.Values.Length) st[_sp - 1] = arr.Values[i];
                    else { Fault(FaultKind.IndexOutOfRange); st[_sp - 1] = 0; }
                    break;
                }
                case Op.LoadC2:
                {
                    var arr = _s.Consts[code[pc++]];
                    int col = st[--_sp], row = st[_sp - 1];
                    if ((uint)row < (uint)arr.Rows && (uint)col < (uint)arr.Columns) st[_sp - 1] = arr.Values[row * arr.Columns + col];
                    else { Fault(FaultKind.IndexOutOfRange); st[_sp - 1] = 0; }
                    break;
                }
                case Op.Add: case Op.Sub: case Op.Mul: case Op.Div: case Op.Mod: case Op.Shl: case Op.Shr:
                case Op.And: case Op.Or: case Op.Xor: case Op.LXor:
                case Op.Lt: case Op.Le: case Op.Gt: case Op.Ge: case Op.Eq: case Op.Ne:
                {
                    int b = st[--_sp];
                    st[_sp - 1] = Arith.Binary(op, st[_sp - 1], b, _f);
                    break;
                }
                case Op.Neg: case Op.Not: case Op.BitNot: case Op.Bool:
                    st[_sp - 1] = Arith.Unary(op, st[_sp - 1]);
                    break;
                case Op.Jmp: pc = code[pc]; break;
                case Op.Jz: pc = st[--_sp] == 0 ? code[pc] : pc + 1; break;
                case Op.Jnz: pc = st[--_sp] != 0 ? code[pc] : pc + 1; break;
                case Op.Call:
                {
                    int target = code[pc++], argc = code[pc++];
                    if (_frames >= MaxFrames)
                    {
                        Fault(FaultKind.StackOverflow);
                        return Exit.Fault;
                    }
                    _frameRet[_frames] = pc;
                    _frameFp[_frames] = fp;
                    _frames++;
                    fp = _sp - argc;
                    pc = target;
                    break;
                }
                case Op.Ret:
                {
                    int v = st[--_sp];
                    _sp = fp;
                    _frames--;
                    pc = _frameRet[_frames];
                    fp = _frameFp[_frames];
                    st[_sp++] = v;
                    break;
                }
                case Op.Builtin:
                {
                    var id = (Bi)code[pc++];
                    int argc = code[pc++];
                    _sp -= argc;
                    CallBuiltin(id, _sp);
                    break;
                }
                case Op.Combo:
                {
                    var cop = (ComboOp)code[pc++];
                    ComboOperation(cop, code[pc++]);
                    break;
                }
                case Op.StepWait:
                    value = st[--_sp];
                    return Exit.StepWait;
                case Op.StepCall:
                    value = code[pc];
                    return Exit.StepCall;
                case Op.StepDone:
                    return Exit.StepDone;
                default:
                    throw new InvalidOperationException($"bad opcode {op} at {pc - 1}");
            }
        }
    }

    // ================= built-ins =================

    static bool ValidSlot(int id) => (uint)id < SlotCount;

    void SetVal(int id, int v)
    {
        if (!ValidSlot(id)) return;
        if (id >= TRACE_1 + 3 && id < TRACE_1 + TraceCount) v = unchecked((short)v); // TRACE_4..6 hold 16 bits
        _work[id] = v;
        _written[id] = true;
        int c = _recordingCombo;
        if (c < 0) return;
        for (int i = 0; i < _holdCount[c]; i++)
            if (_holdSlot[c, i] == id) { _holdValue[c, i] = v; return; }
        if (_holdCount[c] < HoldCapacity)
        {
            _holdSlot[c, _holdCount[c]] = id;
            _holdValue[c, _holdCount[c]] = v;
            _holdCount[c]++;
        }
    }

    static int ClampFor(int id, int v) => IsAxis(id) ? Math.Clamp(v, -100, 100) : Math.Clamp(v, 0, 100);

    int Sin(int deg) => _sin[((deg % 360) + 360) % 360];
    int Cos(int deg) => Sin(unchecked(deg + 90));

    static int Deg(double rad) => (int)Math.Round(rad * 180 / Math.PI, MidpointRounding.AwayFromZero);

    uint NextRandom()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return _rng;
    }

    int DataByte(int offset, int width, bool signed)
    {
        var d = _s.Data;
        if (offset < 0 || offset + width > d.Length)
        {
            Fault(FaultKind.IndexOutOfRange);
            return 0;
        }
        int v = 0;
        for (int i = 0; i < width; i++) v |= d[offset + i] << (8 * i);
        if (signed && width < 4)
        {
            int shift = 32 - 8 * width;
            v = (v << shift) >> shift;
        }
        return v;
    }

    (int x, int y) PolarAxes(int stick) => stick switch
    {
        2 => (RX, RY),
        4 => (LX, LY),
        _ => (-1, -1),
    };

    void CallBuiltin(Bi id, int a)
    {
        var st = _stack;
        int A0 = st[a], A1 = st[a + 1], A2 = st[a + 2], A3 = st[a + 3];
        switch (id)
        {
            case Bi.get_val: Push(ValidSlot(A0) ? _work[A0] : 0); break;
            case Bi.get_ival: Push(ValidSlot(A0) ? _input[A0] : 0); break;
            case Bi.get_lval: Push(ValidSlot(A0) ? _lastOut[A0] : 0); break;
            case Bi.set_val: SetVal(A0, A1); break;
            case Bi.get_ptime: Push(ValidSlot(A0) ? _ptime[A0] : 0); break;
            case Bi.get_brtime: Push(ValidSlot(A0) ? _brtime[A0] : 0); break;
            case Bi.event_press: Push(ValidSlot(A0) && _press[A0] ? 1 : 0); break;
            case Bi.event_release: Push(ValidSlot(A0) && _release[A0] ? 1 : 0); break;
            case Bi.swap:
                if (ValidSlot(A0) && ValidSlot(A1)) (_work[A0], _work[A1]) = (_work[A1], _work[A0]);
                break;
            case Bi.block: if (ValidSlot(A0)) _blockLeft[A0] = Math.Max(_blockLeft[A0], A1); break;
            case Bi.block_all_inputs: Array.Clear(_work); break;

            // UNVERIFIED shapes: the guide describes these in words only (probe group 7)
            case Bi.sensitivity:
                if (!ValidSlot(A0)) break;
                if (A1 == NotUse || A1 <= 0 || A1 >= 100)
                    _work[A0] = ClampFor(A0, (int)((long)_work[A0] * A2 / 100));
                else
                {
                    int v = _work[A0], mag = Math.Abs(v), sign = Math.Sign(v);
                    int scaledMid = Math.Min(100, A1 * A2 / 100);
                    int r = mag <= A1 ? mag * A2 / 100 : scaledMid + (mag - A1) * (100 - scaledMid) / (100 - A1);
                    _work[A0] = ClampFor(A0, sign * r);
                }
                break;
            case Bi.deadzone:
            {
                if (!ValidSlot(A0) || !ValidSlot(A1)) break;
                int x = _work[A0], y = _work[A1];
                if (A2 == DzCircle)
                {
                    double m = Math.Sqrt((double)x * x + (double)y * y);
                    if (m == 0) break;
                    double target = m + A3; // pushed outward by the radius, clamped below
                    _work[A0] = ClampFor(A0, (int)Math.Round(x * target / m));
                    _work[A1] = ClampFor(A1, (int)Math.Round(y * target / m));
                }
                else
                {
                    // nonzero input is pushed outward by the deadzone; zero stays zero (CronusMAX/Zen docs)
                    if (x != 0) _work[A0] = ClampFor(A0, x + Math.Sign(x) * A2);
                    if (y != 0) _work[A1] = ClampFor(A1, y + Math.Sign(y) * A3);
                }
                break;
            }
            case Bi.stickize:
            {
                if (!ValidSlot(A0) || !ValidSlot(A1)) break;
                int x = _work[A0], y = _work[A1];
                double m = Math.Sqrt((double)x * x + (double)y * y);
                if (m > A2 && m > 0)
                {
                    _work[A0] = (int)Math.Round(x * A2 / m);
                    _work[A1] = (int)Math.Round(y * A2 / m);
                }
                break;
            }

            case Bi.get_rtime: Push(_elapsedMs == 0 ? _cycleMs : _elapsedMs); break;
            case Bi.vm_tctrl: _tctrl = Math.Clamp(A0, -9, 30); break;
            case Bi.get_slot: Push(1); break;
            case Bi.load_slot: break;
            case Bi.get_ctrlbutton: Push(0); break;

            case Bi.abs: Push(unchecked(A0 < 0 ? -A0 : A0)); break;
            case Bi.inv: Push(unchecked(-A0)); break;
            case Bi.pow:
            {
                int result = 1, b = A0, e = A1;
                if (e < 0) { Push(0); break; }
                unchecked
                {
                    while (e > 0)
                    {
                        if ((e & 1) != 0) result *= b;
                        b *= b;
                        e >>= 1;
                    }
                }
                Push(result);
                break;
            }
            case Bi.isqrt: Push(A0 <= 0 ? 0 : (int)Math.Sqrt(A0)); break;
            case Bi.random:
            {
                long lo = Math.Min(A0, A1), hi = Math.Max(A0, A1);
                Push((int)(lo + (long)(NextRandom() % (ulong)(hi - lo + 1))));
                break;
            }
            case Bi.clamp: Push(A0 < A1 ? A1 : A0 > A2 ? A2 : A0); break;
            case Bi.min: Push(Math.Min(A0, A1)); break;
            case Bi.max: Push(Math.Max(A0, A1)); break;

            case Bi.set_bit: Push(A0 | (1 << (A1 & 31))); break;
            case Bi.clear_bit: Push(A0 & ~(1 << (A1 & 31))); break;
            case Bi.test_bit: Push((A0 >> (A1 & 31)) & 1); break;
            case Bi.set_bits: Push((A0 & ~(A3 << (A2 & 31))) | ((A1 & A3) << (A2 & 31))); break;
            case Bi.get_bits: Push((A0 >> (A1 & 31)) & A2); break;

            case Bi.sin: Push(Sin(A0)); break;
            case Bi.cos: Push(Cos(A0)); break;
            case Bi.tan:
            {
                int d = ((A0 % 180) + 180) % 180;
                Push(d == 90 ? int.MaxValue : (int)Math.Round(Math.Tan(d * Math.PI / 180) * 10000, MidpointRounding.AwayFromZero));
                break;
            }
            case Bi.asin: Push(Deg(Math.Asin(Math.Clamp(A0, -10000, 10000) / 10000.0))); break;
            case Bi.acos: Push(Deg(Math.Acos(Math.Clamp(A0, -10000, 10000) / 10000.0))); break;
            case Bi.atan2: Push(A0 == 0 && A1 == 0 ? 0 : Deg(Math.Atan2(A0, A1))); break;

            case Bi.dint8: Push(DataByte(A0, 1, true)); break;
            case Bi.duint8: Push(DataByte(A0, 1, false)); break;
            case Bi.dint16: Push(DataByte(A0, 2, true)); break;
            case Bi.duint16: Push(DataByte(A0, 2, false)); break;
            case Bi.dint32: Push(DataByte(A0, 4, true)); break;

            case Bi.get_rumble: Push((uint)A0 < RumbleCount ? _rumbleIn[A0] : 0); break;
            case Bi.set_rumble:
                if ((uint)A0 < RumbleCount) { _rumbleOverride[A0] = Math.Clamp(A1, 0, 100); _rumbleOverridden[A0] = true; }
                break;
            case Bi.block_rumble: _rumbleBlocked = true; break;
            case Bi.reset_rumble: _rumbleBlocked = false; Array.Clear(_rumbleOverridden); break;

            case Bi.get_ps4_lbar: Push(A0 switch { 0 => LightbarOut(2), 1 => LightbarOut(0), 2 => LightbarOut(1), _ => 0 }); break;
            case Bi.set_ps4_lbar: // reference order (red, green, blue); a changelog says BRG (probe pending)
            case Bi.set_rgb:
                _lightbarOverride[0] = Math.Clamp(A0, 0, 255);
                _lightbarOverride[1] = Math.Clamp(A1, 0, 255);
                _lightbarOverride[2] = Math.Clamp(A2, 0, 255);
                _lightbarOverridden = true;
                break;
            case Bi.set_hsb:
            {
                double h = ((A0 % 360) + 360) % 360, s = Math.Clamp(A1, 0, 100) / 100.0, v = Math.Clamp(A2, 0, 100) / 100.0;
                double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
                (double r, double g, double b) = h switch
                {
                    < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
                    < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
                };
                _lightbarOverride[0] = (int)Math.Round((r + m) * 255);
                _lightbarOverride[1] = (int)Math.Round((g + m) * 255);
                _lightbarOverride[2] = (int)Math.Round((b + m) * 255);
                _lightbarOverridden = true;
                break;
            }
            case Bi.get_led: Push((uint)A0 < 4 ? _leds[A0] : 0); break;
            case Bi.set_led: if ((uint)A0 < 4) _leds[A0] = Math.Clamp(A1, 0, 3); break;
            case Bi.set_ledx: break;
            case Bi.get_ledx: Push(0); break;
            case Bi.reset_leds: _lightbarOverridden = false; Array.Clear(_leds); break;

            case Bi.get_pvar:
            {
                int n = PvarIndex(A0);
                bool ok = (uint)n < 65 && _pvarSet[n] && _pvar[n] >= A1 && _pvar[n] <= A2;
                Push(ok ? _pvar[n] : A3);
                break;
            }
            case Bi.set_pvar:
            {
                int n = PvarIndex(A0);
                if ((uint)n < 65) { _pvar[n] = A1; _pvarSet[n] = true; }
                break;
            }
            case Bi.get_bvar: Push((uint)A0 < 1024 ? _bvar[A0] : 0); break;
            case Bi.set_bvar: if ((uint)A0 < 1024) _bvar[A0] = unchecked((short)A1); break;
            case Bi.save_eeprom: break;

            case Bi.get_battery: Push(10); break;
            case Bi.get_controller: Push(PioPs5); break;
            case Bi.get_console: Push(PioPs4); break;
            case Bi.turn_off: break;

            // UNVERIFIED: 0 degrees = right, 90 = down (+Y is down on sticks)
            case Bi.set_polar:
            case Bi.set_polar2:
            {
                var (ax, ay) = PolarAxes(A0);
                if (ax < 0) break;
                int full = id == Bi.set_polar ? 32767 : 10000;
                _work[ax] = Math.Clamp((int)((long)Cos(A1) * A2 * 100 / ((long)full * 10000)), -100, 100);
                _work[ay] = Math.Clamp((int)((long)Sin(A1) * A2 * 100 / ((long)full * 10000)), -100, 100);
                break;
            }
            case Bi.get_polar:
            case Bi.get_ipolar:
            {
                var (ax, ay) = PolarAxes(A0);
                if (ax < 0) { Push(0); break; }
                var src = id == Bi.get_polar ? _work : _input;
                int x = src[ax], y = src[ay];
                if (A1 == 1) // POLAR_ANGLE
                    Push(x == 0 && y == 0 ? 0 : (Deg(Math.Atan2(y, x)) + 360) % 360);
                else
                    Push(Math.Min(32767, (int)(Math.Sqrt((double)x * x + (double)y * y) * 32767 / 100)));
                break;
            }

            case Bi.ps4_touchpad: Push(0); break;
            case Bi.ps4_set_touchpad: break;
            case Bi.get_keyboard: Push(0); break;
            case Bi.get_modifiers: Push(0); break;
            case Bi.ps4_authtimeout: Push(0); break;          // no console authentication on PC
            case Bi.output_reconnection: break;
            case Bi.get_info: Push(0); break;                 // M&K / Zen Link info has no PC counterpart
            case Bi.wiir_offscreen: Push(0); break;
            case Bi.set_remapper: SetDeviceRemap(A0, A1); break;
            case Bi.get_adt:
            {
                int side = AdtSide(A0);
                Push(side >= 0 && (uint)A1 < 11 ? _adt[side, A1] : 0);
                break;
            }
            case Bi.set_adt:
            {
                int side = AdtSide(A0);
                if (side >= 0 && (uint)A1 < 11) _adt[side, A1] = Math.Clamp(A2, 0, 255);
                break;
            }
            case Bi.adt_off:
            {
                int side = AdtSide(A0);
                if (side >= 0) for (int i = 0; i < 11; i++) _adt[side, i] = 0;
                break;
            }
            case Bi.adt_cmp:
            case Bi.adt_setx:
            {
                int side = AdtSide(A0);
                var rec = (uint)A1 < (uint)_s.Consts.Length ? _s.Consts[A1].Values : null; // addr(record) is its const id
                if (side < 0 || rec == null || rec.Length < 11) { if (id == Bi.adt_cmp) Push(0); break; }
                if (id == Bi.adt_setx) { for (int i = 0; i < 11; i++) _adt[side, i] = rec[i]; break; }
                bool same = true;
                for (int i = 0; i < 11; i++) same &= _adt[side, i] == rec[i];
                Push(same ? 1 : 0);
                break;
            }
            case Bi.cls_oled: Oled.Clear(A0); break;
            case Bi.pixel_oled: Oled.Pixel(A0, A1, A2); break;
            case Bi.line_oled: Oled.Line(A0, A1, A2, A3, st[a + 4], st[a + 5]); break;
            case Bi.rect_oled: Oled.Rect(A0, A1, A2, A3, st[a + 4] != 0, st[a + 5]); break;
            case Bi.circle_oled: Oled.Circle(A0, A1, A2, A3 != 0, st[a + 4]); break;
            case Bi.putc_oled: Oled.PutChar(A0, A1); break;
            case Bi.puts_oled: Oled.PutBuffer(A0, A1, A2, A3, st[a + 4]); break;
            case Bi.image_oled: Oled.Image(A0, A1, A2, A3 != 0, StringAt(st[a + 4])); break;
            case Bi.print_oled: Oled.Text(A0, A1, A2, A3, StringAt(st[a + 4])); break;
            default: throw new InvalidOperationException($"unhandled builtin {id}");
        }
    }
}
