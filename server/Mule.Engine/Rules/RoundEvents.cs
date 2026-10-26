using Mule.Engine.Core;

namespace Mule.Engine.Rules;

public enum RoundEventKind : byte
{
    PestAttack = 0, PirateShip = 1, AcidRain = 2, Planetquake = 3, Sunspots = 4, Meteorite = 5, Radiation = 6, FireInStore = 7,
}

/// <summary>What a round event did, for the client to show.</summary>
public sealed record RoundEventResult(RoundEventKind Kind, bool Shown, int Plot = -1, int Plot2 = -1, int Row = -1, int MountainQuarter = -1);

/// <summary>roundEvent ($7B28) and the eight event routines. Display and sound are left to the client.</summary>
public static class RoundEvents
{
    /// <summary>
    /// Pest attack hang fix. The original scan never stops when no plot qualifies and the random start
    /// is plot 44, freezing the game. When true that case does nothing, like every other start with no target.
    /// </summary>
    public static bool FixPestHang = true;

    /// <summary>Draws one card from the deck (without replacement) and applies it.</summary>
    public static RoundEventResult? Run(GameState g, IRandomSource rng)
    {
        int n = 0;
        Span<byte> deck = stackalloc byte[32];
        for (int e = 7; e >= 0; e--)
            for (int y = g.RoundEventsProb[e]; y != 0; y--) deck[n++] = (byte)e;
        int x = deck[rng.Below((byte)n)];
        if (g.RoundEventsProb[x] == 0) return null; // unreachable in play: the original just redraws the map
        g.RoundEventsProb[x]--;
        return (RoundEventKind)x switch
        {
            RoundEventKind.PestAttack => PestAttack(g, rng),
            RoundEventKind.PirateShip => PirateShip(g),
            RoundEventKind.AcidRain => AcidRain(g, rng),
            RoundEventKind.Planetquake => Planetquake(g, rng),
            RoundEventKind.Sunspots => Sunspots(g),
            RoundEventKind.Meteorite => Meteorite(g, rng),
            RoundEventKind.Radiation => Radiation(g, rng),
            _ => FireInStore(g),
        };
    }

    /// <summary>$78CB: a random producing food plot of 1st or 2nd place loses all its food.</summary>
    public static RoundEventResult PestAttack(GameState g, IRandomSource rng)
    {
        int start = rng.Below(45);
        int x = start;
        int steps = 0;
        while (true)
        {
            if (g.PlotsExp[x] == Exp.Food && g.PlotsProd[x] != 0)
            {
                byte owner = g.PlotsPlayer[x];
                if (owner != g.OrdersPlayer[3] && owner != g.OrdersPlayer[2]) break;
            }
            x--;
            if (x == start) return new RoundEventResult(RoundEventKind.PestAttack, false);
            if (x < 0) x = 44;
            if (FixPestHang && ++steps > 45) return new RoundEventResult(RoundEventKind.PestAttack, false);
        }
        g.PlotsProd[x] = 0; // the animation eats the food down to exactly 0
        return new RoundEventResult(RoundEventKind.PestAttack, true, Plot: x);
    }

    /// <summary>$76F0 (tournament): all players' crystite and all crystite production are lost.</summary>
    public static RoundEventResult PirateShip(GameState g)
    {
        byte good = Good.Crystite;
        if (g.Level != 2) { good = Good.Smithore; g.GoodsStoreNb[Good.Smithore] = 0; }
        for (int p = 0; p < 4; p++) g.GoodsPlayersNb[good * 4 + p] = 0;
        byte exp = (byte)(good + 1);
        for (int x = 44; x >= 0; x--)
            if (g.PlotsExp[x] == exp) g.PlotsProd[x] = 0;
        return new RoundEventResult(RoundEventKind.PirateShip, true);
    }

    /// <summary>$7A72: one random row gets food +3 / energy -1 (unclamped), then every plot food +1 / energy -1, clamp 0-8.</summary>
    public static RoundEventResult AcidRain(GameState g, IRandomSource rng)
    {
        int row = rng.Below(5);
        int x = row * 9;
        for (int y = 8; y >= 0; y--, x++)
        {
            byte e = g.PlotsExp[x];
            if (e == Exp.Food) g.PlotsProd[x] = (byte)(g.PlotsProd[x] + 3);
            else if (e == Exp.Energy) g.PlotsProd[x] = (byte)(g.PlotsProd[x] - 1);
        }
        for (x = 44; x >= 0; x--)
        {
            byte e = g.PlotsExp[x];
            if (e == Exp.None || e >= Exp.Smithore) continue;
            byte a = (byte)(g.PlotsProd[x] + (e == Exp.Energy ? 0xFF : 1));
            if ((a & 0x80) != 0) a = 0;
            if (a >= 8) a = 8;
            g.PlotsProd[x] = a;
        }
        return new RoundEventResult(RoundEventKind.AcidRain, true, Row: row);
    }

    /// <summary>$722E: 50% a mountain unit in columns 1, 2, 6 or 7 moves one plot sideways (mules on both plots
    /// are lost); always halves smithore and crystite production.</summary>
    public static RoundEventResult Planetquake(GameState g, IRandomSource rng)
    {
        int src = -1, dst = -1, quarter = -1;
        if (!rng.Negative())
        {
            for (byte tries = 0xC8; --tries != 0;)
            {
                int q = rng.Below(19) + 1;
                byte mx = g.MountainsX[q];
                if (mx < 0x48 || mx >= 0xB8) continue;
                if (mx < 0x98 && mx >= 0x68) continue;
                quarter = q;
                int col = (mx - 56) / 16, row = (q * 8) / 32;
                src = GameState.Index(row, col);
                int dir = (rng.NextByte() & 1) != 0 ? 1 : -1;
                dst = src + dir;
                g.MountainsX[q] = (byte)(g.MountainsX[q] + 16 * dir);
                break;
            }
        }
        if (src >= 0)
        {
            int smith = 2 * 45;
            g.ExpsPlotsCapacity[smith + src]--;
            g.PlotsExp[src] = Exp.None;
            if (g.ExpsPlotsCapacity[smith + src] == 1)
            {
                g.ExpsPlotsCapacity[45 + src] = 3;
                g.ExpsPlotsCapacity[src] = 2;
            }
            if (g.ExpsPlotsCapacity[smith + dst] == 1)
            {
                g.ExpsPlotsCapacity[dst] = 1;
                g.ExpsPlotsCapacity[45 + dst] = 1;
            }
            g.ExpsPlotsCapacity[smith + dst]++;
            g.PlotsExp[dst] = Exp.None;
        }
        for (int x = 44; x >= 0; x--)
            if (g.PlotsExp[x] == Exp.Smithore || g.PlotsExp[x] == Exp.Crystite) g.PlotsProd[x] >>= 1;
        return new RoundEventResult(RoundEventKind.Planetquake, true, Plot: src, Plot2: dst, MountainQuarter: quarter);
    }

    /// <summary>$7C12: every energy plot +3, cap 8.</summary>
    public static RoundEventResult Sunspots(GameState g)
    {
        for (int x = 44; x >= 0; x--)
            if (g.PlotsExp[x] == Exp.Energy)
                g.PlotsProd[x] = (byte)Math.Min(g.PlotsProd[x] + 3, 8);
        return new RoundEventResult(RoundEventKind.Sunspots, true);
    }

    /// <summary>$7C95: a uniform random plot with crystite 0-2 (not river or store) becomes crystite 4; its mule is lost.</summary>
    public static RoundEventResult Meteorite(GameState g, IRandomSource rng)
    {
        int x;
        while (true)
        {
            x = rng.Below(45);
            byte c = g.ExpsPlotsCapacity[3 * 45 + x];
            if ((c & 0x80) != 0 || c >= 3) continue;
            break;
        }
        g.ExpsPlotsCapacity[3 * 45 + x] = 4;
        g.PlotsKnownCrystite[x] = 4;
        g.PlotsExp[x] = Exp.None;
        g.PlotsProd[x] = 0;
        return new RoundEventResult(RoundEventKind.Meteorite, true, Plot: x);
    }

    /// <summary>$7EA6: a random plot with a mule, owned by 1st or 2nd place, loses its mule.</summary>
    public static RoundEventResult Radiation(GameState g, IRandomSource rng)
    {
        int start = rng.Below(45);
        int x = start;
        while (true)
        {
            if (g.PlotsExp[x] != Exp.None)
            {
                byte owner = g.PlotsPlayer[x];
                if (owner != g.OrdersPlayer[3] && owner != g.OrdersPlayer[2]) break;
            }
            x--;
            if (x < 0) x = 44;
            if (x == start) return new RoundEventResult(RoundEventKind.Radiation, false);
        }
        g.PlotsExp[x] = Exp.None;
        return new RoundEventResult(RoundEventKind.Radiation, true, Plot: x);
    }

    /// <summary>$7FF3: store food, energy and smithore are lost.</summary>
    public static RoundEventResult FireInStore(GameState g)
    {
        g.GoodsStoreNb[Good.Food] = 0;
        g.GoodsStoreNb[Good.Energy] = 0;
        g.GoodsStoreNb[Good.Smithore] = 0;
        return new RoundEventResult(RoundEventKind.FireInStore, true);
    }
}
