using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>Production: calcPlotsProd ($4CD2) and its helpers.</summary>
public static class Production
{
    /// <summary>calcPlotsProdWithMissingEnergyMalus ($4CCD).</summary>
    public static void CalcPlotsProdWithMissingEnergyMalus(GameState g, IRandomSource rng)
    {
        g.DoMissingEnergyMalus = 0;
        CalcPlotsProd(g, rng);
    }

    /// <summary>calcPlotsProd ($4CD2): base + economies of scale + learning curve + luck, clamp 0-8,
    /// then the energy shortage when <see cref="GameState.DoMissingEnergyMalus"/> is 0.</summary>
    public static void CalcPlotsProd(GameState g, IRandomSource rng)
    {
        InitVarsFromMaps(g);
        g.ProdMax = 0;
        g.AiMode = 0;
        for (int plot = 44; plot >= 0; plot--)
        {
            g.PlotsProd[plot] = 0;
            byte player = g.PlotsPlayer[plot];
            if ((player & 0x80) != 0) continue;
            byte exp = g.PlotsExp[plot];
            if (exp == Exp.None) continue;
            byte prod = CalcPlotProdWithEcos(g, exp, plot, player);
            prod = rng.Binomial(prod, g.Level);
            if ((prod & 0x80) != 0) prod = 0;
            if (prod >= 9) prod = 8;
            g.PlotsProd[plot] = prod;
            if (prod >= g.ProdMax) g.ProdMax = prod;
        }
        if (g.DoMissingEnergyMalus != 0) return;

        for (int player = 3; player >= 0; player--)
        {
            byte need = CalcEnergyReq(g, player);
            byte held = g.GoodsPlayersNb[Good.Energy * 4 + player];
            if (held >= need) continue;
            int missing = need - held;
            do
            {
                int plot = rng.Below(45);
                // Scan down (wrapping) to the first of the player's plots with a non-energy mule not yet hit.
                while (true)
                {
                    if (g.PlotsPlayer[plot] == player)
                    {
                        byte e = g.PlotsExp[plot];
                        if (e != Exp.None && e != Exp.Energy && (g.PlotsProd[plot] & 0x80) == 0) break;
                    }
                    plot--;
                    if (plot < 0) plot = 44;
                }
                g.PlotsProd[plot] = 0xFF;
            } while (--missing != 0);
        }
        for (int plot = 44; plot >= 0; plot--)
            if ((g.PlotsProd[plot] & 0x80) != 0) g.PlotsProd[plot] = 0;
    }

    /// <summary>
    /// calcPlotProdWithEcos ($4D7A). Uses <see cref="GameState.AiMode"/>: 0 real production,
    /// 1 CPU turn preview, 2 CPU land auction. Result is not capped at 8.
    /// </summary>
    public static byte CalcPlotProdWithEcos(GameState g, byte exp, int plot, byte player)
    {
        int job = exp - 1;
        byte prod;
        if (job == 3 && g.AiMode != 0)
            prod = (byte)(g.PlotsKnownCrystite[plot] & 15);
        else
            prod = g.ExpsPlotsCapacity[(byte)(job * 45) + plot];

        int idx = job * 4 + player;
        byte nbExps = g.ExpsPlayersNb[idx];
        if (g.AiMode != 0) nbExps++;
        byte q = (byte)(nbExps / 3), rem = (byte)(nbExps % 3);
        byte a = (byte)(q + prod);
        if (rem == 0 && g.AiMode != 0) a = (byte)(a + g.ExpsPlayersNb[idx]);
        prod = a;

        byte neighborExp = (byte)(job + 1);
        int neighbors = 0;
        var (row, col) = GameState.Coord(plot);
        neighbors = CountNeighbor(g, row, col - 1, player, neighborExp, neighbors);
        neighbors = CountNeighbor(g, row - 1, col, player, neighborExp, neighbors);
        neighbors = CountNeighbor(g, row + 1, col, player, neighborExp, neighbors);
        neighbors = CountNeighbor(g, row, col + 1, player, neighborExp, neighbors);
        int cap = g.AiMode + 1;
        if (neighbors > cap) neighbors = cap;
        return (byte)(prod + neighbors);
    }

    /// <summary>helpToCountNeighbor ($4E0C).</summary>
    static int CountNeighbor(GameState g, int row, int col, byte player, byte exp, int count)
    {
        if (col < 0 || col >= 9 || row < 0 || row >= 5) return count;
        int p = GameState.Index(row, col);
        if (g.PlotsPlayer[p] != player) return count;
        if (g.AiMode == 2) count += 2;
        if (g.PlotsExp[p] == exp) count++;
        return count & 0xFF;
    }

    /// <summary>initVarsFromMaps ($4E3B): per-player job counts, production totals (from the
    /// current plotsProd), land and mule counts; CPU crystite knowledge for crystite mules.</summary>
    public static void InitVarsFromMaps(GameState g)
    {
        Array.Clear(g.ExpsPlayersNb);
        Array.Clear(g.GoodsPlayersProd);
        Array.Clear(g.PlayersNbLands);
        Array.Clear(g.PlayersNbExps);
        for (int plot = 44; plot >= 0; plot--)
        {
            byte player = g.PlotsPlayer[plot];
            if ((player & 0x80) != 0) continue;
            g.PlayersNbLands[player]++;
            byte exp = g.PlotsExp[plot];
            if (exp == Exp.None) continue;
            g.PlayersNbExps[player]++;
            int idx = (exp - 1) * 4 + player;
            g.ExpsPlayersNb[idx]++;
            g.GoodsPlayersProd[idx] = (byte)(g.GoodsPlayersProd[idx] + g.PlotsProd[plot]);
            if (exp == Exp.Crystite)
                g.PlotsKnownCrystite[plot] = g.ExpsPlotsCapacity[3 * 45 + plot];
        }
    }

    /// <summary>calcEnergyReq ($596B): number of food, smithore and crystite mules of a player.</summary>
    public static byte CalcEnergyReq(GameState g, int player)
    {
        byte n = 0;
        for (int job = 3; job >= 0; job--)
            if (job != 1) n += g.ExpsPlayersNb[job * 4 + player];
        return n;
    }
}
