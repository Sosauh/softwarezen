namespace Gpc.Engine.Vm;

public enum Bi
{
    get_val, get_ival, get_lval, set_val, get_ptime, get_brtime, event_press, event_release,
    swap, block, block_all_inputs, sensitivity, deadzone, stickize,
    get_rtime, vm_tctrl, get_slot, load_slot, get_ctrlbutton,
    abs, inv, pow, isqrt, random, clamp, min, max,
    set_bit, clear_bit, test_bit, set_bits, get_bits,
    sin, cos, tan, asin, acos, atan2,
    dint8, duint8, dint16, duint16, dint32,
    get_rumble, set_rumble, block_rumble, reset_rumble,
    get_ps4_lbar, set_ps4_lbar, set_rgb, set_hsb, get_led, set_led, set_ledx, get_ledx, reset_leds,
    get_pvar, set_pvar, get_bvar, set_bvar, save_eeprom,
    get_battery, get_controller, get_console, turn_off,
    set_polar, set_polar2, get_polar, get_ipolar,
    ps4_touchpad, ps4_set_touchpad, get_keyboard, get_modifiers,
    oled_noop, oled_noop_value,
}

/// <summary>How the compiler treats an argument.</summary>
public enum ArgKind { Value, VarRef }

public sealed record BuiltinInfo(string Name, Bi Id, int Argc, bool Returns, ArgKind FirstArg = ArgKind.Value)
{
    /// <summary>For variadic legacy forms (6-arg sensitivity): the most args accepted; extras are ignored.</summary>
    public int MaxArgc { get; init; } = Argc;
}

public static class Builtins
{
    public static readonly IReadOnlyDictionary<string, BuiltinInfo> ByName = Build();

    public static readonly IReadOnlyDictionary<string, ComboOp> ComboOps = new Dictionary<string, ComboOp>
    {
        ["combo_run"] = ComboOp.Run, ["combo_stop"] = ComboOp.Stop, ["combo_restart"] = ComboOp.Restart,
        ["combo_running"] = ComboOp.Running, ["combo_suspend"] = ComboOp.Suspend, ["combo_suspended"] = ComboOp.Suspended,
        ["combo_resume"] = ComboOp.Resume, ["combo_current_step"] = ComboOp.CurrentStep,
        ["combo_step_time_left"] = ComboOp.StepTimeLeft,
        ["combo_stop_all"] = ComboOp.StopAll, ["combo_suspend_all"] = ComboOp.SuspendAll, ["combo_resume_all"] = ComboOp.ResumeAll,
    };

    public static bool ComboOpReturns(ComboOp op) =>
        op is ComboOp.Running or ComboOp.Suspended or ComboOp.CurrentStep or ComboOp.StepTimeLeft;

    public static bool ComboOpTakesCombo(ComboOp op) =>
        op is not (ComboOp.StopAll or ComboOp.SuspendAll or ComboOp.ResumeAll);

    static Dictionary<string, BuiltinInfo> Build()
    {
        var list = new List<BuiltinInfo>
        {
            new("get_val", Bi.get_val, 1, true), new("get_ival", Bi.get_ival, 1, true), new("get_lval", Bi.get_lval, 1, true),
            new("set_val", Bi.set_val, 2, false), new("get_ptime", Bi.get_ptime, 1, true), new("get_brtime", Bi.get_brtime, 1, true),
            new("event_press", Bi.event_press, 1, true), new("event_release", Bi.event_release, 1, true),
            new("swap", Bi.swap, 2, false), new("block", Bi.block, 2, false), new("block_all_inputs", Bi.block_all_inputs, 0, false),
            new("sensitivity", Bi.sensitivity, 3, false) { MaxArgc = 6 },
            new("deadzone", Bi.deadzone, 4, false), new("stickize", Bi.stickize, 3, false),
            new("get_rtime", Bi.get_rtime, 0, true), new("vm_tctrl", Bi.vm_tctrl, 1, false),
            new("get_slot", Bi.get_slot, 0, true), new("load_slot", Bi.load_slot, 1, false), new("get_ctrlbutton", Bi.get_ctrlbutton, 0, true),
            new("abs", Bi.abs, 1, true), new("inv", Bi.inv, 1, true), new("pow", Bi.pow, 2, true), new("isqrt", Bi.isqrt, 1, true),
            new("random", Bi.random, 2, true), new("clamp", Bi.clamp, 3, true), new("min", Bi.min, 2, true), new("max", Bi.max, 2, true),
            new("set_bit", Bi.set_bit, 2, false, ArgKind.VarRef), new("clear_bit", Bi.clear_bit, 2, false, ArgKind.VarRef),
            new("test_bit", Bi.test_bit, 2, true), new("set_bits", Bi.set_bits, 4, false, ArgKind.VarRef), new("get_bits", Bi.get_bits, 3, true),
            new("sin", Bi.sin, 1, true), new("cos", Bi.cos, 1, true), new("tan", Bi.tan, 1, true),
            new("asin", Bi.asin, 1, true), new("acos", Bi.acos, 1, true), new("atan2", Bi.atan2, 2, true),
            new("dint8", Bi.dint8, 1, true), new("duint8", Bi.duint8, 1, true), new("dint16", Bi.dint16, 1, true),
            new("duint16", Bi.duint16, 1, true), new("dint32", Bi.dint32, 1, true),
            new("dchar", Bi.dint8, 1, true), new("dbyte", Bi.duint8, 1, true), new("dword", Bi.dint16, 1, true),
            new("get_rumble", Bi.get_rumble, 1, true), new("set_rumble", Bi.set_rumble, 2, false),
            new("block_rumble", Bi.block_rumble, 0, false), new("reset_rumble", Bi.reset_rumble, 0, false),
            new("get_ps4_lbar", Bi.get_ps4_lbar, 1, true), new("set_ps4_lbar", Bi.set_ps4_lbar, 3, false),
            new("set_rgb", Bi.set_rgb, 3, false), new("set_hsb", Bi.set_hsb, 3, false),
            new("get_led", Bi.get_led, 1, true), new("set_led", Bi.set_led, 2, false), new("set_ledx", Bi.set_ledx, 2, false),
            new("get_ledx", Bi.get_ledx, 0, true), new("reset_leds", Bi.reset_leds, 0, false),
            new("get_pvar", Bi.get_pvar, 4, true), new("set_pvar", Bi.set_pvar, 2, false),
            new("get_bvar", Bi.get_bvar, 1, true), new("set_bvar", Bi.set_bvar, 2, false), new("save_eeprom", Bi.save_eeprom, 0, false),
            new("get_battery", Bi.get_battery, 0, true), new("get_controller", Bi.get_controller, 0, true),
            new("get_console", Bi.get_console, 0, true), new("turn_off", Bi.turn_off, 0, false),
            new("set_polar", Bi.set_polar, 3, false), new("set_polar2", Bi.set_polar2, 3, false),
            new("get_polar", Bi.get_polar, 2, true), new("get_ipolar", Bi.get_ipolar, 2, true),
            new("ps4_touchpad", Bi.ps4_touchpad, 1, true), new("ps4_set_touchpad", Bi.ps4_set_touchpad, 2, false),
            new("get_keyboard", Bi.get_keyboard, 1, true), new("get_modifiers", Bi.get_modifiers, 1, true),
            // OLED has no PC counterpart in v1: accepted and ignored
            new("cls_oled", Bi.oled_noop, 1, false), new("pixel_oled", Bi.oled_noop, 3, false), new("line_oled", Bi.oled_noop, 6, false),
            new("rect_oled", Bi.oled_noop, 6, false), new("circle_oled", Bi.oled_noop, 5, false), new("putc_oled", Bi.oled_noop, 2, false),
            new("puts_oled", Bi.oled_noop, 5, false), new("image_oled", Bi.oled_noop, 5, false), new("print", Bi.oled_noop, 5, false),
            new("printf", Bi.oled_noop, 5, false),
        };
        return list.ToDictionary(b => b.Name);
    }
}
