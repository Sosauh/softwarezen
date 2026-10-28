using Gpc.Engine.Runtime;
using Gpc.Engine.Vm;
using static Gpc.Engine.Runtime.Identifiers;
using static Gpc.Engine.Tests.Harness;

namespace Gpc.Engine.Tests;

public class OledTests
{
    static int LitCount(Oled o, int x0 = 0, int y0 = 0, int x1 = Oled.Width, int y1 = Oled.Height)
    {
        int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (o.Lit(x, y)) n++;
        return n;
    }

    [Fact]
    public void ShapesDrawAndClear()
    {
        var m = Run("init { cls_oled(OLED_WHITE); cls_oled(OLED_BLACK); rect_oled(10, 10, 20, 5, TRUE, OLED_WHITE); pixel_oled(0, 0, OLED_WHITE); } main { }");
        Assert.Equal(20 * 5 + 1, LitCount(m.Oled));
        Assert.True(m.Oled.Lit(0, 0));
        Assert.True(m.Oled.Lit(29, 14));
        Assert.False(m.Oled.Lit(30, 14));
    }

    [Fact]
    public void OutlineRectLineAndCircle()
    {
        var m = Run("init { rect_oled(0, 0, 10, 10, FALSE, OLED_WHITE); } main { }");
        Assert.Equal(36, LitCount(m.Oled));
        Assert.False(m.Oled.Lit(5, 5));
        m = Run("init { line_oled(0, 63, 127, 63, 1, OLED_WHITE); } main { }");
        Assert.Equal(128, LitCount(m.Oled));
        m = Run("init { circle_oled(64, 32, 10, TRUE, OLED_WHITE); } main { }");
        Assert.True(m.Oled.Lit(64, 32));
        Assert.True(m.Oled.Lit(74, 32));
        Assert.False(m.Oled.Lit(76, 32));
    }

    [Fact]
    public void PrintDrawsConstStringsArraysAndLiterals()
    {
        // 'A' column 0 is 0x7E: rows 1..6 lit, row 0 dark; small font draws at offset (1, 1)
        var m = Run("const string S = \"A\"; init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, S[0]); } main { }");
        Assert.False(m.Oled.Lit(1, 1));
        Assert.True(m.Oled.Lit(1, 2));
        Assert.True(m.Oled.Lit(1, 7));

        var a = Run("const string NAMES[] = { \"I\", \"MMM\" }; init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, NAMES[1]); } main { }");
        var b = Run("init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, \"MMM\"); } main { }");
        var c = Run("const string T = \"MMM\"; init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, addr(T)); } main { }");
        Assert.True(LitCount(a.Oled) > 30);
        Assert.Equal(LitCount(a.Oled), LitCount(b.Oled));
        Assert.Equal(LitCount(a.Oled), LitCount(c.Oled));
        Assert.True(LitCount(a.Oled, 14, 0, 21, 10) > 0); // third character in the third 7-pixel cell
    }

    [Fact]
    public void LargerFontsScaleUp()
    {
        var small = Run("init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, \"H\"); } main { }");
        var large = Run("init { print(0, 0, OLED_FONT_LARGE, OLED_WHITE, \"H\"); } main { }");
        Assert.Equal(LitCount(small.Oled) * 9, LitCount(large.Oled));
        Assert.Equal(16, Eval("OLED_FONT_LARGE_WIDTH"));
        Assert.Equal(26, Eval("OLED_FONT_LARGE_HEIGHT"));
    }

    [Fact]
    public void TextBufferPrintsDigits()
    {
        var a = Run("init { putc_oled(1, 52); putc_oled(2, 50); puts_oled(0, 0, OLED_FONT_SMALL, 2, OLED_WHITE); } main { }");
        var b = Run("init { print(0, 0, OLED_FONT_SMALL, OLED_WHITE, \"42\"); } main { }");
        Assert.Equal(LitCount(b.Oled), LitCount(a.Oled));
    }

    [Fact]
    public void ImagesUnpackMsbFirst()
    {
        var m = Run("const image DOT = { 8, 1, 0x81 }; init { image_oled(0, 0, OLED_WHITE, FALSE, addr(DOT)); } main { }");
        Assert.True(m.Oled.Lit(0, 0));
        Assert.True(m.Oled.Lit(7, 0));
        Assert.Equal(2, LitCount(m.Oled));
    }

    [Fact]
    public void BadImageIsACompileError() =>
        Assert.Contains("GZ5116", ErrorCodes("const image BAD = { 8, 8, 0x01 }; main { }"));

    [Fact]
    public void VersionChangesOnlyWhenDrawing()
    {
        var m = new Machine(Build("int t; main { t++; if (t == 2) pixel_oled(1, 1, OLED_WHITE); }"));
        m.RunCycle();
        int v = m.Oled.Version;
        m.RunCycle();
        Assert.NotEqual(v, m.Oled.Version);
        v = m.Oled.Version;
        m.RunCycle();
        Assert.Equal(v, m.Oled.Version);
    }
}

public class SettingsMenuScriptTests
{
    static void Press(Machine m, int slot, params int[] alsoHeld)
    {
        foreach (var h in alsoHeld) m.Input[h] = 100;
        m.Input[slot] = 100;
        m.RunCycle();
        m.Input[slot] = 0;
        foreach (var h in alsoHeld) m.Input[h] = 0;
        m.RunCycle();
    }

    [Fact]
    public void MenuOpensChangesSpeedAndSaves()
    {
        var m = new Machine(Build(ScriptTests.RepoFile("scripts/settings_menu.gpc")));
        m.RunCycle();
        int statusVersion = m.Oled.Version;

        Press(m, OPTIONS, L2);                       // open the menu
        Assert.NotEqual(statusVersion, m.Oled.Version);
        Assert.True(m.Oled.Lit(0, 15));              // first row highlighted

        Press(m, DOWN);                              // select "Speed"
        Assert.True(m.Oled.Lit(0, 29));
        Assert.False(m.Oled.Lit(0, 15));

        for (int i = 0; i < 12; i++)                 // speed 10 -> 20 (capped)
        {
            m.Input[RIGHT] = 100;
            m.RunCycle();
            Assert.Equal(0, m.Output[RIGHT]);        // menu keys don't reach the game
            m.Input[RIGHT] = 0;
            m.RunCycle();
        }
        Press(m, OPTIONS, L2);                       // close and save

        // turbo now runs at 20 presses per second: 25 ms down, 25 ms up
        m.Input[CROSS] = 100;
        var seen = new List<int>();
        for (int i = 0; i < 20; i++) { m.RunCycle(); seen.Add(m.Output[CROSS]); }
        int presses = seen.Zip(seen.Skip(1), (a, b) => a == 0 && b == 100 ? 1 : 0).Sum() + (seen[0] == 100 ? 1 : 0);
        Assert.Equal(4, presses); // 200 ms at 20/s
    }

    [Fact]
    public void DpadWorksNormallyWhenMenuIsClosed()
    {
        var m = new Machine(Build(ScriptTests.RepoFile("scripts/settings_menu.gpc")));
        m.Input[DOWN] = 100;
        m.RunCycle();
        Assert.Equal(100, m.Output[DOWN]);
    }
}

public class OledGlyphNameTests
{
    [Theory]
    [InlineData("OLED_CROSS", "ASCII_PS_CROSS")]
    [InlineData("OLED_TRIANGLE", "ASCII_PS_TRIANGLE")]
    [InlineData("OLED_UP", "ASCII_DPAD_UP")]
    [InlineData("OLED_RIGHT", "ASCII_DPAD_RIGHT")]
    public void OledGlyphNamesMatchTheAsciiGlyphs(string oled, string ascii) =>
        Assert.Equal(Harness.Eval(ascii), Harness.Eval(oled));

    [Fact]
    public void GlyphsCanBeDrawnWithPutc()
    {
        var m = Harness.Run("init { putc_oled(1, OLED_CROSS); puts_oled(0, 0, OLED_FONT_SMALL, 1, OLED_WHITE); } main { }");
        Assert.True(m.Oled.Lit(1, 1)); // the cross glyph's top-left corner
    }
}
