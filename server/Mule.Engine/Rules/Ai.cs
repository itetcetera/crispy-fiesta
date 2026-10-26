using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>CPU evaluation of goods and plots ($33DE-$373B).</summary>
public static class Ai
{
    /// <summary>
    /// calcGoodsQuality ($33DE): simulates <paramref name="rounds"/> rounds of prices as if all food and
    /// energy were sold to the store, and rates each good 0-31 from its average price (price / 8, cap 250).
    /// Leaves the crystite price at round * 4 + 100 (the original never restores it).
    /// </summary>
    public static void CalcGoodsQuality(GameState g, IRandomSource rng, byte rounds)
    {
        byte d0 = rounds;
        SetLow(g.GoodsPriceLimit, 0, 10);
        SetLow(g.GoodsPriceLimit, 1, 10);
        byte numRound = g.NumRound, level = g.Level;
        for (int i = 0; i < 3; i++) g.GoodsPriceTemp[i] = g.GoodsPrice[i];
        Array.Clear(g.GoodsQuality);
        g.Level = 0;
        g.GoodsFuturNb[0] = g.GoodsStoreNb[Good.Food];
        g.GoodsFuturNb[1] = g.GoodsStoreNb[Good.Energy];
        g.GoodsFuturNb[2] = (byte)((g.GoodsStoreNb[Good.Smithore] >> 1) + g.GoodsStoreNb[Good.Mule]);
        // The players' food and energy are parked in typesPlayersScore ($B24B) in the original.
        Span<byte> held = stackalloc byte[8];
        for (int y = 7; y >= 0; y--)
        {
            held[y] = g.GoodsPlayersNb[y];
            g.GoodsFuturNb[y >> 2] += held[y];
            g.GoodsPlayersNb[y] = 0;
        }
        g.DoMissingEnergyMalus = 1;
        Production.CalcPlotsProd(g, rng);
        Prices.CalcGoodsPriceFromWhen(g, rng);
        for (int i = 0; i < 3; i++) g.GoodsPrice[i] = g.GoodsPriceTemp[i];
        while (true)
        {
            Prices.CalcGoodsPriceFromWhen(g, rng);
            for (int y = 2; y >= 0; y--) SetWord(g.GoodsQuality, y, (ushort)(Word(g.GoodsQuality, y) + g.GoodsPrice[y]));
            g.NumRound++;
            if (--d0 == 0) break;
            g.DoMissingEnergyMalus = 1;
            Production.CalcPlotsProd(g, rng);
        }
        for (int x = 2; x >= 0; x--) g.GoodsPrice[x] = (ushort)(Word(g.GoodsQuality, x) / rounds);
        g.Level = level;
        g.NumRound = numRound;
        SetLow(g.GoodsPrice, Good.Crystite, (byte)(numRound * 4 + 100));
        for (int x = 3; x >= 0; x--)
        {
            ushort p = g.GoodsPrice[x];
            byte a = (p >> 8) != 0 || (byte)p >= 250 ? (byte)250 : (byte)p;
            g.GoodsQuality[x] = (byte)(a >> 3);
        }
        for (int i = 0; i < 3; i++) g.GoodsPrice[i] = g.GoodsPriceTemp[i];
        for (int y = 7; y >= 0; y--) g.GoodsPlayersNb[y] = held[y];
        SetLow(g.GoodsPriceLimit, 0, 30);
        SetLow(g.GoodsPriceLimit, 1, 25);
    }

    /// <summary>calcPlayersPlotsQuality ($350E): every player's rating of every free plot, minus the
    /// distance to the store. In round 1 each player favours one random good (or none).</summary>
    public static void CalcPlayersPlotsQuality(GameState g, IRandomSource rng)
    {
        CalcGoodsQuality(g, rng, 3);
        g.AiMode = 2;
        Span<byte> saved = stackalloc byte[3];
        for (int player = 3; player >= 0; player--)
        {
            g.GoodsQuality.AsSpan(0, 3).CopyTo(saved);
            if (g.NumRound == 1)
            {
                int good = rng.NextByte() & 3;
                if (good != 3) g.GoodsQuality[good] = 32;
            }
            for (int plot = 44; plot >= 0; plot--)
            {
                g.PlotsTemp[plot] = 0;
                if ((g.PlotsPlayer[plot] & 0x80) == 0 || plot == GameState.StorePlot) continue;
                byte q = CalcPlotExpAndQualityAi(g, (byte)player, plot, out _);
                byte d = CalcPlotDistFromStore(plot);
                g.PlotsTemp[plot] = q >= d ? (byte)(q - d) : (byte)0;
            }
            g.PlotsTemp.CopyTo(g.PlayersPlotsQuality, player * 45);
            saved.CopyTo(g.GoodsQuality);
        }
    }

    /// <summary>calcPlotExpAndQualityAi ($35A3): the best job for a plot and its rating. Ties go to the
    /// lower job (food over energy, and so on). River plots only consider food and energy.</summary>
    public static byte CalcPlotExpAndQualityAi(GameState g, byte player, int plot, out byte exp)
    {
        byte best = 0;
        exp = 0;
        byte top = g.Level == 2 ? Exp.Crystite : Exp.Smithore;
        if ((g.CrystiteAt(plot) & 0x80) != 0) top = Exp.Energy;
        for (byte e = top; e > 0; e--)
        {
            byte q = CalcPlotQualityAi(g, e, plot, player);
            if (q >= best) { best = q; exp = e; }
        }
        return best;
    }

    /// <summary>
    /// calcPlotQualityAi ($35D5): expected output (cap 8) times the good's rating, cap 255. After round 1
    /// food and energy get a bonus when the player needs them; carries in the original add 1 to the
    /// "surplus" bonus and to the energy stock count, kept here.
    /// </summary>
    public static byte CalcPlotQualityAi(GameState g, byte exp, int plot, byte player)
    {
        byte prod = Production.CalcPlotProdWithEcos(g, exp, plot, player);
        byte energyReq = Production.CalcEnergyReq(g, player);
        int x = exp - 1;
        byte q = g.GoodsQuality[x];
        byte y = q;
        if (g.NumRound != 1 && x <= 1)
        {
            bool surplus, carry;
            if (x == 0)
            {
                byte a = (byte)(g.GoodsPlayersNb[player] + g.GoodsPlayersProd[player]);
                carry = a >= g.FoodReq;
                surplus = carry && g.ExpsPlayersNb[player] == 0;
            }
            else
            {
                byte a = (byte)(g.GoodsPlayersNb[4 + player] + g.GoodsPlayersProd[4 + player] + 1);
                carry = a >= energyReq;
                byte mules = g.ExpsPlayersNb[4 + player];
                surplus = carry && (mules == 0 || (g.PlayersNbExps[player] >> 2) > mules);
            }
            if (surplus)
            {
                int s = q + exp + 1;
                y = (byte)((byte)s + 10 + (s > 255 ? 1 : 0));
            }
            else if (!carry)
            {
                int s = q + 9;
                y = (byte)((byte)s + exp + (s > 255 ? 1 : 0));
                if (g.GoodsShortage[x] != 0) y = (byte)(y + 4);
            }
        }
        byte p = prod >= 9 ? (byte)8 : prod;
        int r = p * y;
        return r > 255 ? (byte)255 : (byte)r;
    }

    /// <summary>calcPlotDistFromStore ($36D1): max(|col-4|, 2*|row-2|), plus 1 on the river column.</summary>
    public static byte CalcPlotDistFromStore(int plot)
    {
        var (row, col) = GameState.Coord(plot);
        int dc = Math.Abs(col - 4), dl = Math.Abs(row - 2) * 2;
        int a = dl >= dc ? dl : dc;
        if (dc == 0) a++;
        return (byte)a;
    }

    /// <summary>
    /// calcPlayerBestPlotAi ($36FB): the best-rated plot from <paramref name="start"/> on. The bottom two
    /// rows lose 1 and 2 points, counted from the start plot. An equal rating replaces the best one only
    /// on a 1-in-4 random roll.
    /// </summary>
    public static byte CalcPlayerBestPlotAi(GameState g, IRandomSource rng, int cpu, int start)
    {
        int x = (byte)(start + 45 * cpu);
        byte best = 0, penalty = 0, bestEval = 0;
        for (int y = start; ;)
        {
            byte q = g.PlayersPlotsQuality[x];
            byte eval = q >= penalty ? (byte)(q - penalty) : (byte)0;
            if (eval >= bestEval && (eval != bestEval || (rng.NextByte() & 0xC0) == 0xC0))
            {
                bestEval = eval;
                best = (byte)y;
            }
            x++; y++;
            if (y == 27) penalty++;
            if (y == 36) penalty++;
            if (y >= 45) break;
        }
        return best;
    }

    static void SetLow(ushort[] a, int i, byte v) => a[i] = (ushort)((a[i] & 0xFF00) | v);
    static ushort Word(byte[] b, int i) => (ushort)(b[2 * i] | (b[2 * i + 1] << 8));
    static void SetWord(byte[] b, int i, ushort v) { b[2 * i] = (byte)v; b[2 * i + 1] = (byte)(v >> 8); }
}
