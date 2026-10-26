using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

public static partial class PlayerTurn
{
    /// <summary>
    /// playerTurnAi ($374B): a CPU player's turn, worked out on the map without walking. While its
    /// time allows it installs mules where a job gains the most, then in the tournament it assays.
    /// </summary>
    static IEnumerable PlayerTurnAi(Game game, Turn t)
    {
        var g = game.State;
        byte cp = g.CurrentPlayer;
        ShowStatic(game, 0);
        yield return CpuWait(game, 80);
        int done = 0;                          // byte_4A
        var installed = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }; // byte_B1B5
        // NextExp ($3763).
        while ((t.Time & 0x80) == 0 && t.Time >= 35)
        {
            byte bestGain = 0, bestExp = 0, bestPlot = 0;  // byte_B323, byte_B324, successScore
            Ai.CalcGoodsQuality(g, game.Rng, 2);
            for (int plot = 44; plot >= 0; plot--)
            {
                if (g.PlotsPlayer[plot] != cp || Array.IndexOf(installed, (byte)plot) >= 0) continue;
                int cash = g.PlayersCash[cp];
                byte q1 = 0;
                if (g.PlotsExp[plot] == 0)
                {
                    if (g.GoodsStoreNb[Good.Mule] == 0) continue;
                    cash -= g.MulePrice;
                    if (cash < 0) continue;
                }
                // Left at 1 after the first plot looked at: the later ratings count a mule more.
                else q1 = Ai.CalcPlotQualityAi(g, g.PlotsExp[plot], plot, cp);
                g.AiMode = 1;
                byte q2 = Ai.CalcPlotExpAndQualityAi(g, cp, plot, out byte exp);
                if (g.PlotsExp[plot] == exp || q2 < q1) continue;
                byte gain = (byte)(q2 - q1);
                if (gain < 2 || gain < bestGain) continue;
                // The high byte of the price is taken as 0.
                if (cash - g.OutfitsPrice[exp] < 0) continue;
                bestExp = exp; bestGain = gain; bestPlot = (byte)plot;
            }
            // The working out runs over several frames.
            yield return game.Frames(game.Think?.Invoke("install " + ThinkKey(g, cp)) ?? ThinkFrames(g, cp));
            if (bestGain == 0) break;
            installed[done] = bestPlot;
            if (g.PlotsExp[bestPlot] == 0)
            {
                g.PlayersCash[cp] -= g.MulePrice;
                g.GoodsStoreNb[Good.Mule]--;
                game.Emit(new MuleBoughtEvent(cp));
            }
            g.PlayersCash[cp] -= g.OutfitsPrice[bestExp];
            g.PlotsExp[bestPlot] = bestExp;
            game.Trace?.Invoke($"install {bestExp} {bestPlot}");
            MapDisplay.DrawCell(g, bestPlot, hideOutput: true);
            game.Emit(new PlotChangedEvent(bestPlot, bestExp));
            // Drawing the plot can run past the vertical blank.
            if (game.Race?.Invoke($"time late {TimeAfter(t, 25, bestPlot)}") == true) yield return null;
            DecreaseTime(t, 25, bestPlot);
            game.Emit(new CpuActionEvent(cp, bestPlot, false));
            yield return CpuWait(game, 0x64, sync: true);
            ShowStatic(game, 0);
            done++;
        }
        // NextPhaseAssay ($3868).
        if (g.Level != 2) yield break;
        int high = 0;
        for (int x = 44; x >= 0; x--) if (g.PlotsKnownCrystite[x] == 3) high++;
        if (high >= 3) yield break;
        while (true)
        {
            // NextAssay ($3881): its own plots first, then the free ones.
            g.AiMode = 1;
            if ((t.Time & 0x80) != 0 || t.Time < 22) yield break;
            while (true)
            {
                byte score = 0, at = 0;
                for (int x = 44; x >= 0; x--)
                {
                    byte owner = g.PlotsPlayer[x];
                    if (g.AiMode != 0 ? owner != cp : (owner & 0x80) == 0) continue;
                    if ((g.CrystiteAt(x) & 0x80) != 0) continue;
                    byte known = g.PlotsKnownCrystite[x];
                    if ((known & 0x80) == 0 || known < score) continue;
                    if (known == score && game.Rng.NextByte() >= 32) continue;
                    score = known; at = (byte)x;
                }
                if (score != 0)
                {
                    game.Trace?.Invoke($"assay {at}");
                    CrystiteFound(game, at);
                    game.Emit(new CpuActionEvent(cp, at, true));
                    yield return CpuWait(game, 0xC8, sync: true);
                    ShowStatic(game, 0);
                    DecreaseTime(t, 10, at);
                    ExpandKnownCrystite(g);
                    yield return game.Frames(game.Think?.Invoke("assay " + ThinkKey(g, cp)) ?? 0);
                    break;
                }
                if ((sbyte)--g.AiMode < 0) yield break;
            }
        }
    }

    static string ThinkKey(GameState g, byte cp)
    {
        int plots = 0, mules = 0;
        for (int x = 0; x < GameState.Plots; x++)
            if (g.PlotsPlayer[x] == cp) { plots++; if (g.PlotsExp[x] != 0) mules++; }
        return $"plots {plots} mules {mules} round {g.NumRound}";
    }

    /// <summary>The frames the original's working out takes (calcGoodsQuality and the plot ratings), an
    /// estimate fitted to the turn tests (it grows with the plots the player owns; exact within a frame).</summary>
    static int ThinkFrames(GameState g, byte cp)
    {
        int plots = 0;
        for (int x = 0; x < GameState.Plots; x++) if (g.PlotsPlayer[x] == cp) plots++;
        return plots == 0 ? 1 : (int)Math.Round(2 + 0.4 * plots);
    }

    /// <summary>sub_38F7/sub_391B: twenty frames, then <paramref name="frames"/> more unless a human presses.</summary>
    static IEnumerable CpuWait(Game game, byte frames, bool sync = false)
    {
        var v = game.Vbi;
        if (sync)
        {
            // sub_38F7 first waits for the screen's line 242 (sub_5E1B), the next frame's when the
            // drawing before it ran past it.
            if (game.Race?.Invoke("sync late") == true) yield return null;
            game.Trace?.Invoke("sync");
        }
        v.Sound = game.State.CurrentPlayer;
        yield return game.WaitJiffy(20);
        byte end = (byte)(frames + v.RtClock);
        while (v.RtClock != end && !AnyHumanPressing(game.State, v)) yield return null;
    }

    /// <summary>decreaseTimeFromAction ($3936): the cost plus four per plot from the store.</summary>
    static void DecreaseTime(Turn t, byte cost, int plot) => t.Time = TimeAfter(t, cost, plot);

    static byte TimeAfter(Turn t, byte cost, int plot) => (byte)(t.Time - cost - Ai.CalcPlotDistFromStore(plot) * 4);

    /// <summary>expandKnownCrystite ($3665): the CPU guesses the crystite around the medium and high
    /// finds it knows; a guess only replaces a lower one.</summary>
    static void ExpandKnownCrystite(GameState g)
    {
        bool any = false;
        for (int x = 44; x >= 0; x--)
        {
            g.PlotsTemp[x] = 0;
            g.PlotsTemp3[x] = 0;
            if (g.PlotsKnownCrystite[x] == 2) { g.PlotsTemp[x] = 2; any = true; }
        }
        if (any) Setup.ExpandCrystite(g);
        for (int x = 44; x >= 0; x--)
            if (g.PlotsTemp3[x] != 2) g.PlotsTemp3[x] |= 0x80;
        any = false;
        for (int x = 44; x >= 0; x--)
        {
            g.PlotsTemp[x] = 0;
            if (g.PlotsKnownCrystite[x] == 3) { g.PlotsTemp[x] = 3; any = true; }
        }
        if (any) Setup.ExpandCrystite(g);
        for (int x = 44; x >= 0; x--)
        {
            byte known = g.PlotsKnownCrystite[x];
            if ((known & 0x80) != 0 && (known & 0x0F) < g.PlotsTemp3[x]) g.PlotsKnownCrystite[x] = g.PlotsTemp3[x];
        }
    }
}
