using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>What a player event did, for the client to show (text index = event number).</summary>
public sealed record PlayerEventResult(int Event, int Amount, int Each = 0, int Plot = -1);

/// <summary>playerEvent ($8131): the start-of-turn personal event (the wampus move before it is separate).</summary>
public static class PlayerEvents
{
    /// <summary>turnEventsQuality ($8334): sign = good/bad, magnitude = multiple of 25 (or goods for 0-2).</summary>
    public static readonly sbyte[] Quality = { 1, 1, -1, 2, -3, 4, -2, -1, 2, 4, 8, 2, 3, 6, 4, 2, -6, -4, -4, -4, -1, 1 };

    /// <summary>Runs the event roll for <paramref name="player"/>. Returns null when nothing happens.</summary>
    public static PlayerEventResult? Run(GameState g, IRandomSource rng, int player)
    {
        byte bc = 0;
        return Run(g, rng, player, ref bc);
    }

    /// <summary>Also tracks byte_BC, which the event search leaves behind (the turn reuses the byte).</summary>
    public static PlayerEventResult? Run(GameState g, IRandomSource rng, int player, ref byte bc)
    {
        byte a = rng.Below(80);
        if (a >= 22) return null;
        bc = a;
        int x = a;
        int count = 20;
        while (true)
        {
            if (g.TurnEventsHappened[x] == 0)
            {
                var r = TryEvent(g, rng, player, x, ref bc, out bool tryAnother);
                if (!tryAnother) return r;
            }
            x--;
            if (x < 0) x = 21;
            if (--count == 0) return null;
        }
    }

    static PlayerEventResult? TryEvent(GameState g, IRandomSource rng, int player, int x, ref byte bc, out bool tryAnother)
    {
        tryAnother = true;
        if (Quality[x] < 0)
        {
            if (player == g.OrdersPlayer[3] || player == g.OrdersPlayer[2]) return null;
        }
        else if (player == g.OrdersPlayer[0]) return null;

        tryAnother = false;
        bc = (byte)x;
        int food = Good.Food * 4 + player;
        // A non-leader with no food gets the care package instead of any other event ($81A4).
        if (x == 0 || (player != g.OrdersPlayer[0] && g.GoodsPlayersNb[food] == 0))
        {
            g.GoodsPlayersNb[food] += 3;
            g.GoodsPlayersNb[Good.Energy * 4 + player] += 2;
            g.TurnEventsHappened[0]++;
            bc = 0;
            return new PlayerEventResult(0, 0);
        }
        switch (x)
        {
            case 1:
                g.GoodsPlayersNb[Good.Smithore * 4 + player] += 2;
                return Happened(g, x, 0);
            case 2:
                g.GoodsPlayersNb[food] >>= 1;
                return Happened(g, x, 0);
            case 3: case 4: case 5:
                if (g.PlayersNbExps[player] == 0) { tryAnother = true; return null; }
                return Cash(g, player, x, 1);
            case 6:
                {
                    byte n = (byte)(g.ExpsPlayersNb[8 + player] + g.ExpsPlayersNb[12 + player]);
                    if (n == 0) { tryAnother = true; return null; }
                    return Cash(g, player, x, n);
                }
            case 7:
                if (g.ExpsPlayersNb[4 + player] == 0) { tryAnother = true; return null; }
                return Cash(g, player, x, g.ExpsPlayersNb[4 + player]);
            case 8:
                if (g.ExpsPlayersNb[player] == 0) { tryAnother = true; return null; }
                return Cash(g, player, x, g.ExpsPlayersNb[player]);
            case 20:
                {
                    if (g.PlayersNbExps[player] == 0) { tryAnother = true; return null; }
                    int plot = 44;
                    while (plot >= 0 && g.PlotsPlayer[plot] != player) plot--;
                    if (plot < 0) { tryAnother = true; return null; }
                    g.PlotsExp[plot] = Exp.None;
                    g.PlotsPlayer[plot] = GameState.Nobody;
                    MapDisplay.DrawEmptyCell(g, plot);
                    return Happened(g, x, 0, plot);
                }
            case 21:
                {
                    byte plot = Prices.RandomFreePlot(g, rng, out _);
                    if ((plot & 0x80) != 0) { tryAnother = true; return null; }
                    g.PlotsPlayer[plot] = (byte)player;
                    MapDisplay.DrawCell(g, plot);
                    return Happened(g, x, 0, plot);
                }
            default: // 9-19
                return Cash(g, player, x, 1);
        }
    }

    static PlayerEventResult Happened(GameState g, int x, int amount, int plot = -1, int each = 0)
    {
        g.TurnEventsHappened[x]++;
        return new PlayerEventResult(x, amount, each, plot);
    }

    /// <summary>calcCashQualityPlayerEvent ($8285): |quality| * 25 * (round / 4 + 1).</summary>
    public static ushort Unit(GameState g, int x)
    {
        int q = Math.Abs((int)Quality[x]);
        byte q25 = (byte)(q * 25);
        return (ushort)(q25 * ((g.NumRound >> 2) + 1));
    }

    /// <summary>Cash events. A penalty the player cannot pay does nothing and is not used up ($8261).</summary>
    static PlayerEventResult? Cash(GameState g, int player, int x, byte count)
    {
        ushort unit = Unit(g, x);
        bool perMule = x >= 6 && x <= 8;
        // Per-mule events multiply the low byte of the unit amount by the count (doVarQualityPlayerEvent $82DB).
        ushort amount = perMule ? (ushort)((byte)unit * count) : unit;
        if (Quality[x] < 0)
        {
            if (g.PlayersCash[player] < amount) return null;
            g.PlayersCash[player] -= amount;
        }
        else g.PlayersCash[player] = (ushort)(g.PlayersCash[player] + amount);
        return Happened(g, x, amount, each: perMule ? unit : 0);
    }
}
