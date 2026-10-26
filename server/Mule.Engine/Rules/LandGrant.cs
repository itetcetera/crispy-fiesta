using Mule.Engine.Core;

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
}
