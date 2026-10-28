using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>grantLand ($4E84): the land grant.</summary>
public static class LandGrant
{
    /// <summary>
    /// $4E87-$4EEC: each CPU picks the plot it will press its button on (playersPlotSelectedByCpu, $9F;
    /// $FF for humans). When at least 4 plots are free, a CPU that wants the same plot as a CPU ranked
    /// below it gives way: it marks that plot worthless for itself and picks again.
    /// </summary>
    public static void ChooseCpuPlots(GameState g, IRandomSource rng, byte[] selected)
    {
        if ((g.MaxIndexCpu & 0x80) != 0) return;
        Ai.CalcPlayersPlotsQuality(g, rng);
        for (int p = 3; p >= 0; p--)
            selected[p] = p <= g.MaxIndexCpu ? Ai.CalcPlayerBestPlotAi(g, rng, p, 0) : (byte)0xFF;
        Prices.RandomFreePlot(g, rng, out int free);
        if (free < 4) return;
        while (true)
        {
            bool conflict = false;
            for (int o1 = 2; o1 >= 0 && !conflict; o1--)
            {
                byte p1 = g.OrdersPlayer[o1];
                byte plot1 = selected[p1];
                if ((plot1 & 0x80) != 0) continue;
                for (int o2 = o1 + 1; o2 < 4; o2++)
                {
                    if (selected[g.OrdersPlayer[o2]] != plot1) continue;
                    g.PlayersPlotsQuality[(byte)(plot1 + 45 * p1)] = 0;
                    selected[p1] = Ai.CalcPlayerBestPlotAi(g, rng, p1, 0);
                    conflict = true;
                    break;
                }
            }
            if (!conflict) return;
        }
    }

    /// <summary>
    /// Frames the original spends before its first wait: one for the map and title, and with CPUs
    /// playing their thinking time, about one frame per free plot plus five (measured on the original
    /// over 60 games: within 3 frames). This is presentation timing only.
    /// </summary>
    static int IntroFrames(GameState g)
    {
        if ((g.MaxIndexCpu & 0x80) != 0) return 1;
        int free = 0;
        for (int i = 0; i < 45; i++) if (i != GameState.StorePlot && (g.PlotsPlayer[i] & 0x80) != 0) free++;
        return 1 + free + 5;
    }

    /// <summary>
    /// The land grant. The cursor stops on every free plot for cursorWait timer ticks; the first latched
    /// button, checked from last place to first, takes it and the cursor then rests 4 times as long.
    /// On the tick the cursor leaves a plot only the last-place player is checked. CPUs "press" on the
    /// plot they chose, re-choosing when theirs has gone by.
    /// </summary>
    public static IEnumerable Run(Game game)
    {
        var g = game.State;
        var v = game.Vbi;
        game.Emit(new MapShownEvent());
        var selected = new byte[4];
        int intro = IntroFrames(g);
        ChooseCpuPlots(g, game.Rng, selected);
        game.Emit(new TitleEvent(" LAND GRANT"));
        game.Emit(new MessageEvent("PRESS YOUR BUTTON TO SELECT A PLOT."));
        yield return game.Frames(game.Think?.Invoke("grant intro") ?? intro);
        game.Trace?.Invoke("grant wait");
        yield return game.WaitJiffyLevel();
        yield return game.WaitSoundThenBeep();
        yield return game.WaitJiffy(6);

        var hasPlot = new byte[4]; // $B1B5
        int nbSelected = 0;        // $8B
        int plot = 0;              // $CB
        bool cpus = (g.MaxIndexCpu & 0x80) == 0;
        while (true)
        {
            if (plot != GameState.StorePlot && (g.PlotsPlayer[plot] & 0x80) != 0)
            {
                v.InitButtons();
                if (cpus)
                    for (int cpu = g.MaxIndexCpu; cpu >= 0; cpu--)
                        if (plot == selected[cpu]) v.ButtonPushed[cpu]--;
                game.Emit(new GrantCursorEvent(plot));
                v.Timers[0] = g.CursorWait;
                bool leave = false;
                while (!leave)
                {
                    yield return game.WaitJiffy(1);
                    for (int cur = 3; cur >= 0; cur--)
                    {
                        byte y = g.OrdersPlayer[cur];
                        if ((hasPlot[y] | v.ButtonPushed[y]) == 0)
                        {
                            game.Play(y);
                            nbSelected++;
                            g.PlotsPlayer[plot] = y;
                            hasPlot[y] = 1;
                            game.Emit(new PlotGrantedEvent(plot, y));
                            v.Timers[0] = (byte)(g.CursorWait * 4);
                            while (v.Timers[0] != 0) yield return null;
                        }
                        if (v.Timers[0] == 0) { leave = true; break; }
                    }
                }
            }
            // sub_4C61 clears two pages and always runs past the next vertical blank.
            yield return null;
            game.Emit(new GrantCursorHiddenEvent());
            if (nbSelected == 4) break;
            plot++;
            if (plot == 45) break;
            int left = 45 - plot;
            if (!cpus) continue;
            for (int p = g.MaxIndexCpu; p >= 0; p--)
                if (hasPlot[p] == 0 && selected[p] < plot)
                    selected[p] = Ai.CalcPlayerBestPlotAi(g, game.Rng, p, plot);
            // When the plots left are no more than the players still without one, the next player in
            // order (if a CPU, and if a human comes after it) takes the current plot.
            if (4 - nbSelected < left) continue;
            int i = nbSelected;
            byte player = g.OrdersPlayer[i];
            if (player > g.MaxIndexCpu) continue;
            while (true)
            {
                if (++i >= 4) break;
                if (g.OrdersPlayer[i] <= g.MaxIndexCpu) continue;
                selected[player] = (byte)plot;
                break;
            }
        }
        yield return game.Beeps(2);
        yield return game.WaitJiffyLevel();
    }
}
