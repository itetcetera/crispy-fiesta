using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>What the GTIA latches for the walker (player 0) over one frame of the map: the mountains
/// are player 2, the river and the store player 3, each moved to its quarter-row's x by the display
/// list interrupt ($2D43); the plot frames are the playfield. Scanlines and colour clocks as in the
/// original.</summary>
public static class MapCollisions
{
    const int FirstLine = 8, EndLine = 248;
    const int MapTop = 45, RowLines = 8;   // the twenty mode 6 rows of the map
    const int PlayfieldLeft = 48, PlayfieldRight = 208;
    const int PlayerLeft = 34, PlayerRight = 222; // where the GTIA checks collisions

    /// <summary>The quarter-row whose positions a scanline shows. The first interrupt comes three
    /// lines before the map; the last one leaves the positions alone, so the lines above and below
    /// the map show the bottom row's.</summary>
    static int Band(int line) => line < 42 ? 19 : line < MapTop + RowLines ? 0 : Math.Min(19, (line - MapTop) / RowLines);

    /// <summary>P0PL (bit 2 mountains, bit 3 river or store) and whether P0PF is set.</summary>
    public static (byte Players, bool Playfield) Compute(GameState g, Turn t)
    {
        byte players = 0;
        bool playfield = false;
        for (int line = FirstLine; line < EndLine; line++)
        {
            byte p0 = t.Pm0[line];
            if (p0 == 0) continue;
            int band = Band(line);
            for (int b = 0; b < 8; b++)
            {
                if ((p0 & (0x80 >> b)) == 0) continue;
                int x = t.Hpos0 + b;
                if (x < PlayerLeft || x >= PlayerRight) continue;
                if (Covers(g.SprMount[line], g.MountainsX[band], 1, x)) players |= 4;
                if (Covers(g.SprRiver[line], g.RiverStoreX[band], band is >= 8 and < 12 ? 2 : 1, x)) players |= 8;
                if (!playfield && x >= PlayfieldLeft && x < PlayfieldRight && PlayfieldAt(g, line, x)) playfield = true;
            }
        }
        return (players, playfield);
    }

    static bool Covers(byte graphics, byte hpos, int width, int x)
    {
        int b = x - hpos;
        if (b < 0 || b >= 8 * width) return false;
        return (graphics & (0x80 >> (b / width))) != 0;
    }

    static bool PlayfieldAt(GameState g, int line, int x)
    {
        if (line < MapTop || line >= MapTop + MapDisplay.Rows * RowLines) return false;
        int row = (line - MapTop) / RowLines, glyphRow = (line - MapTop) % RowLines;
        int col = (x - PlayfieldLeft) / 8, bit = (x - PlayfieldLeft) % 8;
        byte name = g.MapScreen[row * MapDisplay.Width + col];
        byte bits = MapGraphics.MapFont[(name & 0x3F) * 8 + glyphRow];
        return (bits & (0x80 >> bit)) != 0;
    }
}
