namespace Gpc.Engine.Runtime;

/// <summary>
/// Controller slot indexes and built-in constants.
/// The guide does not publish platform identifier numbers. The 0..29 layout below matches the one
/// table it does publish (the Driving Force wheel: PS 0, R2 4, TRIANGLE 17, CROSS 19 ...) and the
/// classic GPC report layout; everything marked UNVERIFIED must be confirmed on a Zen.
/// </summary>
public static class Identifiers
{
    public const int SlotCount = 64;

    public const int PS = 0, SHARE = 1, OPTIONS = 2, R1 = 3, R2 = 4, R3 = 5, L1 = 6, L2 = 7, L3 = 8;
    public const int RX = 9, RY = 10, LX = 11, LY = 12, UP = 13, DOWN = 14, LEFT = 15, RIGHT = 16;
    public const int TRIANGLE = 17, CIRCLE = 18, CROSS = 19, SQUARE = 20;
    public const int ACCX = 21, ACCY = 22, ACCZ = 23, GYROX = 24, GYROY = 25, GYROZ = 26;
    // published (guide.cronus.support PS4/PS5 identifier tables)
    public const int TOUCH = 27, FINGER1X = 28, FINGER1Y = 29, FINGER1 = 30, FINGER2X = 31, FINGER2Y = 32, FINGER2 = 33;
    public const int TRACE_1 = 38; // UNVERIFIED: no source gives TRACE_* numbers; 38..43 is a free range
    public const int TraceCount = 6;
    public const int MUTE = 50;                                                  // published: PS5_MUTE
    public const int PADDLE_1 = 34, PADDLE_2 = 35, PADDLE_3 = 36, PADDLE_4 = 37; // published: XB1_PR1, PR2, PL1, PL2

    public const int RUMBLE_A = 0, RUMBLE_B = 1, RUMBLE_RT = 2, RUMBLE_LT = 3; // UNVERIFIED
    public const int RumbleCount = 4;

    /// <summary>Buttons are 0/100 and triggers 0..100; these slots are signed axes.</summary>
    public static bool IsAxis(int slot) => slot is RX or RY or LX or LY or (>= ACCX and <= GYROZ) or FINGER1X or FINGER1Y or FINGER2X or FINGER2Y;

    public static readonly IReadOnlyDictionary<string, int> Constants = Build();

    static Dictionary<string, int> Build()
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["TRUE"] = 1, ["FALSE"] = 0,
            ["INT32_MIN"] = int.MinValue, ["INT32_MAX"] = int.MaxValue,
            ["INT16_MIN"] = short.MinValue, ["INT16_MAX"] = short.MaxValue,
            ["INT8_MIN"] = sbyte.MinValue, ["INT8_MAX"] = sbyte.MaxValue,
            ["UINT8_MAX"] = byte.MaxValue, ["UINT16_MAX"] = ushort.MaxValue,
            // published (polar and extended analog selectors page)
            ["POLAR_RADIUS"] = 0, ["POLAR_ANGLE"] = 1, ["POLAR_RS"] = 2, ["POLAR_LS"] = 4, ["POLAR_GHOST"] = 6,
            ["POLAR_RX"] = 44, ["POLAR_RY"] = 45, ["POLAR_LX"] = 46, ["POLAR_LY"] = 47,
            ["ANALOG_RX"] = 54, ["ANALOG_RY"] = 55, ["ANALOG_LX"] = 56, ["ANALOG_LY"] = 57,
            ["ANALOG_GHOSTX"] = 58, ["ANALOG_GHOSTY"] = 59,
            ["OLED_BLACK"] = 0, ["OLED_WHITE"] = 1,
            ["OLED_FONT_SMALL"] = 0, ["OLED_FONT_MEDIUM"] = 1, ["OLED_FONT_LARGE"] = 2,
            ["OLED_WIDTH"] = Oled.Width, ["OLED_HEIGHT"] = Oled.Height,
            // LARGE is from the guide; SMALL and MEDIUM sizes are UNVERIFIED
            ["OLED_FONT_SMALL_WIDTH"] = Oled.Fonts[0].w, ["OLED_FONT_SMALL_HEIGHT"] = Oled.Fonts[0].h,
            ["OLED_FONT_MEDIUM_WIDTH"] = Oled.Fonts[1].w, ["OLED_FONT_MEDIUM_HEIGHT"] = Oled.Fonts[1].h,
            ["OLED_FONT_LARGE_WIDTH"] = Oled.Fonts[2].w, ["OLED_FONT_LARGE_HEIGHT"] = Oled.Fonts[2].h,
            // UNVERIFIED values below
            ["BUTTON_MIN"] = 0, ["BUTTON_MAX"] = TRACE_1 + TraceCount - 1, ["BUTTON_MAX_EXTENDED"] = SlotCount - 1,
            ["VALUE_MIN"] = -100, ["VALUE_MAX"] = 100,
            ["TIME_MAX"] = 32767,
            ["NOT_USE"] = 0, ["DZ_CIRCLE"] = 101, ["ALL_REMAPS"] = -1, // cronus.support misc constants
            ["REMAP_NULL"] = -1,
            ["RUMBLE_A"] = RUMBLE_A, ["RUMBLE_B"] = RUMBLE_B, ["RUMBLE_RT"] = RUMBLE_RT, ["RUMBLE_LT"] = RUMBLE_LT,
            ["RUMBLE_MIN"] = 0, ["RUMBLE_MAX"] = RumbleCount - 1,
            ["PS4_BLUE"] = 0, ["PS4_RED"] = 1, ["PS4_GREEN"] = 2, // cronus.support LED constants
            ["LED_NONE"] = -1, ["LED_1"] = 0, ["LED_2"] = 1, ["LED_3"] = 2, ["LED_4"] = 3,
            ["LED_ID_MIN"] = 0, ["LED_ID_MAX"] = 3, ["LED_MIN"] = 0, ["LED_MAX"] = 3,
            ["LEDX_MIN"] = 0, ["LEDX_MAX"] = 255,
            ["PIO_NONE"] = 0, ["PIO_AUTO"] = 0, ["PIO_PS3"] = 1, ["PIO_XB360"] = 2, ["PIO_WII"] = 3, ["PIO_PS4"] = 4,
            ["PIO_XB1"] = 5, ["PIO_SWITCH"] = 6, ["PIO_PS5"] = 7, ["PIO_XBSX"] = 8,
            ["PS4T_P1"] = 1, ["PS4T_P1X"] = 2, ["PS4T_P1Y"] = 3, ["PS4T_P2"] = 16, ["PS4T_P2X"] = 32, ["PS4T_P2Y"] = 48,
        };

        for (int i = 1; i <= 32; i++) d[$"BITMASK_{i}"] = unchecked(1 << (i - 1));

        // ASCII and OLED glyph codes, as published in the guide's constants reference
        for (int i = 0; i <= 9; i++) d[$"ASCII_DIGIT{i}"] = '0' + i;
        for (char c = 'A'; c <= 'Z'; c++) { d[$"ASCII_UPPER_{c}"] = c; d[$"ASCII_LOWER_{c}"] = char.ToLowerInvariant(c); }
        // Keyboard usages (USB HID, matching the guide's 4..251 range) and modifier masks. UNVERIFIED names beyond
        // KEY_A, KEY_SPACE, MOD_LCTRL, MOD_LSHIFT, MOD_LALT; the guide publishes no values for these categories.
        for (char c = 'A'; c <= 'Z'; c++) d[$"KEY_{c}"] = 4 + (c - 'A');
        for (int i = 1; i <= 9; i++) d[$"KEY_{i}"] = 29 + i;
        d["KEY_0"] = 39;
        for (int i = 1; i <= 12; i++) d[$"KEY_F{i}"] = 57 + i;
        foreach (var (name, value) in new (string, int)[] {
            ("ENTER", 40), ("ESCAPE", 41), ("ESC", 41), ("BACKSPACE", 42), ("TAB", 43), ("SPACE", 44), ("MINUS", 45),
            ("EQUAL", 46), ("LEFTBRACE", 47), ("RIGHTBRACE", 48), ("BACKSLASH", 49), ("SEMICOLON", 51), ("APOSTROPHE", 52),
            ("GRAVE", 53), ("COMMA", 54), ("DOT", 55), ("PERIOD", 55), ("SLASH", 56), ("CAPSLOCK", 57), ("PRINTSCREEN", 70),
            ("SCROLLLOCK", 71), ("PAUSE", 72), ("INSERT", 73), ("HOME", 74), ("PAGEUP", 75), ("DELETE", 76), ("END", 77),
            ("PAGEDOWN", 78), ("RIGHT", 79), ("LEFT", 80), ("DOWN", 81), ("UP", 82), ("NUMLOCK", 83),
            ("LEFTCTRL", 224), ("LEFTSHIFT", 225), ("LEFTALT", 226), ("LEFTGUI", 227),
            ("RIGHTCTRL", 228), ("RIGHTSHIFT", 229), ("RIGHTALT", 230), ("RIGHTGUI", 231) })
            d["KEY_" + name] = value;
        foreach (var (name, bit) in new[] { ("LCTRL", 0), ("LSHIFT", 1), ("LALT", 2), ("LGUI", 3), ("RCTRL", 4), ("RSHIFT", 5), ("RALT", 6), ("RGUI", 7) })
            d["MOD_" + name] = 1 << bit;

        // PS5 adaptive-trigger record fields, in the guide's field order (UNVERIFIED that value = position)
        foreach (var (name, index) in new[] { ("MODE", 0), ("START", 1), ("FORCE1", 2), ("FORCE2", 3), ("STRENGTH_LOW", 4),
                     ("STRENGTH_MID", 5), ("STRENGTH_HIGH", 6), ("UNK1", 7), ("UNK2", 8), ("FREQ", 9), ("UNK3", 10) })
            d["PS5_ADT_" + name] = index;
        d["PS5_ADT_CR"] = 1; // a mode value; mode numbers are not published (UNVERIFIED)
        // PS5 haptics on/off detection ids for get_rumble (stated in the changelog)
        d["PS5_HAPTICS_L"] = 26;
        d["PS5_HAPTICS_R"] = 27;
        d["PS5_MUTE"] = MUTE;
        d["PS4_TOUCHPAD"] = TOUCH;
        d["SWI_CAPTURE"] = TOUCH;
        foreach (var (name, slot) in new[] { ("PR1", PADDLE_1), ("PR2", PADDLE_2), ("PL1", PADDLE_3), ("PL2", PADDLE_4) })
            d["XB1_" + name] = slot; // Elite paddles (UNVERIFIED slots)
        d["ZENLINK_STATUS"] = 0;

        foreach (var (name, value) in new (string, int)[] { ("SPACE", 32), ("EXCLAMATION", 33), ("DOUBLE_QUOTE", 34), ("QUOTE", 34), ("POUND", 35), ("DOLLAR", 36), ("PERCENT", 37), ("AMPERSAND", 38), ("AND", 38), ("SINGLE_QUOTE", 39), ("APOSTROPHE", 39), ("LEFT_PARENTHESIS", 40), ("OPEN_PARENTHESIS", 40), ("RIGHT_PARENTHESIS", 41), ("CLOSE_PARENTHESIS", 41), ("ASTERISK", 42), ("PLUS", 43), ("COMMA", 44), ("MINUS", 45), ("PERIOD", 46), ("SLASH", 47), ("COLON", 58), ("SEMICOLON", 59), ("LESS_THAN", 60), ("EQUAL", 61), ("GREATER_THAN", 62), ("QUESTION", 63), ("AT", 64), ("LEFT_BRACKET", 91), ("LEFT_SQUARE_BRACKET", 91), ("OPEN_SQUARE_BRACKET", 91), ("OPEN_BRACKET", 91), ("BACKSLASH", 92), ("RIGHT_BRACKET", 93), ("RIGHT_SQUARE_BRACKET", 93), ("CLOSE_BRACKET", 93), ("CARAT", 94), ("UNDERSCORE", 95), ("GRAVE", 96), ("BACK_QUOTE", 96), ("LEFT_BRACE", 123), ("OPEN_BRACE", 123), ("LEFT_CURLY_BRACE", 123), ("OPEN_CURLY_BRACE", 123), ("LEFT_CURLY_BRACKET", 123), ("VERTICAL_BAR", 124), ("BAR", 124), ("PIPE", 124), ("RIGHT_BRACE", 125), ("CLOSE_BRACE", 125), ("RIGHT_CURLY_BRACE", 125), ("CLOSE_CURLY_BRACE", 125), ("RIGHT_CURLY_BRACKET", 125), ("TILDE", 126), ("PS_CROSS", 127), ("PS_CIRCLE", 128), ("PS_SQUARE", 129), ("PS_TRIANGLE", 130), ("DPAD_UP", 131), ("DPAD_DOWN", 132), ("DPAD_LEFT", 133), ("DPAD_RIGHT", 134), ("XB_VIEW", 135), ("XB_MENU", 136) })
            d["ASCII_" + name] = value;
        // OLED glyph names for the same icons (UNVERIFIED values; they complete the guide's count of 23 OLED constants)
        foreach (var (name, glyph) in new[] { ("CROSS", 127), ("CIRCLE", 128), ("SQUARE", 129), ("TRIANGLE", 130),
                     ("UP", 131), ("DOWN", 132), ("LEFT", 133), ("RIGHT", 134), ("XB_VIEW", 135), ("XB_MENU", 136) })
            d["OLED_" + name] = glyph;
        for (int i = 1; i <= 64; i++) d[$"SPVAR_{i}"] = -32768 + (i - 1); // cronus.support memory constants
        for (int i = 0; i < TraceCount; i++) d[$"TRACE_{i + 1}"] = TRACE_1 + i;

        void Platform(string p, params (string name, int slot)[] specific)
        {
            foreach (var (name, slot) in specific) d[p + name] = slot;
            d[p + "RX"] = RX; d[p + "RY"] = RY; d[p + "LX"] = LX; d[p + "LY"] = LY;
            d[p + "UP"] = UP; d[p + "DOWN"] = DOWN; d[p + "LEFT"] = LEFT; d[p + "RIGHT"] = RIGHT;
        }

        var sony = new (string, int)[]
        {
            ("PS", PS), ("OPTIONS", OPTIONS), ("R1", R1), ("R2", R2), ("R3", R3), ("L1", L1), ("L2", L2), ("L3", L3),
            ("TRIANGLE", TRIANGLE), ("CIRCLE", CIRCLE), ("CROSS", CROSS), ("SQUARE", SQUARE),
            ("ACCX", ACCX), ("ACCY", ACCY), ("ACCZ", ACCZ), ("GYROX", GYROX), ("GYROY", GYROY), ("GYROZ", GYROZ),
            ("TOUCH", TOUCH), ("FINGER1X", FINGER1X), ("FINGER1Y", FINGER1Y), ("FINGER1", FINGER1),
            ("FINGER2X", FINGER2X), ("FINGER2Y", FINGER2Y), ("FINGER2", FINGER2),
        };
        Platform("PS4_", sony.Append(("SHARE", SHARE)).ToArray());
        Platform("PS5_", sony.Append(("CREATE", SHARE)).Append(("SHARE", SHARE)).ToArray());
        Platform("PS3_", ("PS", PS), ("SELECT", SHARE), ("START", OPTIONS), ("R1", R1), ("R2", R2), ("R3", R3),
            ("L1", L1), ("L2", L2), ("L3", L3), ("TRIANGLE", TRIANGLE), ("CIRCLE", CIRCLE), ("CROSS", CROSS), ("SQUARE", SQUARE),
            ("ACCX", ACCX), ("ACCY", ACCY), ("ACCZ", ACCZ), ("GYRO", GYROX));
        Platform("XB1_", ("XBOX", PS), ("VIEW", SHARE), ("MENU", OPTIONS), ("RB", R1), ("RT", R2), ("RS", R3),
            ("LB", L1), ("LT", L2), ("LS", L3), ("Y", TRIANGLE), ("B", CIRCLE), ("A", CROSS), ("X", SQUARE));
        Platform("XB360_", ("XBOX", PS), ("BACK", SHARE), ("START", OPTIONS), ("RB", R1), ("RT", R2), ("RS", R3),
            ("LB", L1), ("LT", L2), ("LS", L3), ("Y", TRIANGLE), ("B", CIRCLE), ("A", CROSS), ("X", SQUARE));
        Platform("SWI_", ("HOME", PS), ("MINUS", SHARE), ("PLUS", OPTIONS), ("R", R1), ("ZR", R2), ("RS", R3),
            ("L", L1), ("ZL", L2), ("LS", L3), ("X", TRIANGLE), ("A", CIRCLE), ("B", CROSS), ("Y", SQUARE));
        // Published tables override every inferred value above (guide.cronus.support, PublishedConstants.cs).
        foreach (var (name, value) in PublishedConstants.All) d[name] = value;

        // published wheel table
        foreach (var (n, v) in new[] { ("PS", 0), ("SELECT", 1), ("START", 2), ("RPADDLE", 3), ("R2", 4), ("R3", 5),
                     ("LPADDLE", 6), ("L2", 7), ("L3", 8), ("GAS", 9), ("STEERING", 11), ("BRAKE", 12), ("UP", 13),
                     ("DOWN", 14), ("LEFT", 15), ("RIGHT", 16), ("TRIANGLE", 17), ("CIRCLE", 18), ("CROSS", 19), ("SQUARE", 20) })
            d["DF_" + n] = v;
        return d;
    }
}
