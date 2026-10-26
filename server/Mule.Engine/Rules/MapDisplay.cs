using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>The map screen's characters ($B528, 20 columns by 20 rows of mode 6). Each plot is two
/// columns by four rows: a frame in the owner's color with the improvement and its output.</summary>
public static class MapDisplay
{
    public const int Width = 20, Rows = 20;
    static readonly byte[] OwnerColor = { 0x00, 0x40, 0x80, 0xC0 }; // byte_1107

    /// <summary>drawCell ($49AB). <paramref name="hideOutput"/> is the Y argument's bit 7: in the
    /// standard and tournament games the output is then not shown.</summary>
    public static void DrawCell(GameState g, int plot, bool hideOutput = false)
    {
        byte owner = g.PlotsPlayer[plot];
        if ((owner & 0x80) != 0) return;
        byte color = OwnerColor[owner & 3];
        int at = plot / GameState.Columns * 4 * Width + plot % GameState.Columns * 2 + 1;
        var s = g.MapScreen;
        s[at] = (byte)(0x1D | color);
        s[at + Width] = (byte)(0x1E | color);
        s[at + 2 * Width] = (byte)(0x1E | color);
        s[at + 3 * Width] = (byte)(0x1F | color);
        byte exp = g.PlotsExp[plot];
        byte output = 0;
        if (exp != 0 && !(hideOutput && g.Level != 0))
            output = g.ExpsPlotsCapacity[(exp - 1) * GameState.Plots + plot];
        s[at + 3 * Width + 1] = Char((byte)(0xC2 + output), color);
        s[at + 2 * Width + 1] = Char((byte)(0xB7 + exp), color);
        s[at + Width + 1] = Char((byte)(0xB1 + exp), color);
        s[at + 1] = (byte)(0x20 | color);
    }

    /// <summary>drawEmptyCell ($4B2B): blanks the plot's eight characters. The colour is byte_90's,
    /// which the money drawing before the player event leaves at 0.</summary>
    public static void DrawEmptyCell(GameState g, int plot)
    {
        int at = plot / GameState.Columns * 4 * Width + plot % GameState.Columns * 2 + 1;
        for (int r = 0; r < 4; r++) { g.MapScreen[at + r * Width] = 0; g.MapScreen[at + r * Width + 1] = 0; }
    }

    /// <summary>decodeCharToMessage ($1011) for the codes the map uses.</summary>
    static byte Char(byte a, byte color) => a is >= 0xA0 and < 0xE0 ? (byte)((a - 0xA0) | color) : a;

    /// <summary>drawMap ($4B59): every owned plot over what the screen holds.</summary>
    public static void DrawMap(GameState g)
    {
        for (int plot = 0; plot < GameState.Plots; plot++) DrawCell(g, plot);
    }
}
