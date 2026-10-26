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

    /// <summary>The event texts (EVENTS_PLAYER_TEXT_ADDR $8308, from the snapshot): the amounts go
    /// between the parts, the unit amount first for the per-mule events.</summary>
    static readonly string[][] Texts =
    {
        new[] { "YOU JUST RECEIVED A PACKAGE FROM YOUR HOME-WORLD RELATIVES CONTAINING 3 FOOD AND 2 ENERGY UNITS." },
        new[] { "A WANDERING SPACE TRAVELER REPAID YOUR HOSPITALITY BY LEAVING TWO BARS OF SMITHORE." },
        new[] { "MISCHIEVOUS GLAC-ELVES BROKE INTO YOUR STORAGE SHED AND STOLE HALF YOUR FOOD." },
        new[] { "YOUR MULE WAS JUDGED \"BEST BUILT\" AT THE COLONY FAIR.  YOU WON $", "." },
        new[] { "ONE OF YOUR MULES LOST A BOLT.  REPAIRS COST YOU $", "." },
        new[] { "YOUR MULE WON THE COLONY TAP-DANCING CONTEST.  YOU COLLECTED $", "." },
        new[] { "YOUR MINING MULES HAVE DETERIORATED FROM HEAVY USE AND COST $", " EACH TO REPAIR.  THE TOTAL COST IS $", "." },
        new[] { "THE SOLAR COLLECTORS ON YOUR ENERGY MULES ARE DIRTY.  CLEANING COST YOU $", " EACH FOR A TOTAL OF $", "." },
        new[] { "THE COLONY COUNCIL FOR AGRICULTURE AWARDED YOU $", " FOR EACH FOOD PLOT YOU HAVE DEVELOPED.  THE TOTAL GRANT IS $", "." },
        new[] { "THE COLONY AWARDED YOU $", " FOR STOPPING THE WART WORM INFESTATION." },
        new[] { "THE MUSEUM BOUGHT YOUR ANTIQUE PERSONAL COMPUTER FOR $", "." },
        new[] { "YOU WON THE COLONY SWAMP EEL EATING CONTEST AND COLLECTED $", ". (YUCK!)" },
        new[] { "A CHARITY FROM YOUR HOME-WORLD TOOK PITY ON YOU AND SENT $", "." },
        new[] { "YOUR OFFWORLD INVESTMENTS IN ARTIFICIAL DUMBNESS PAID $", " IN DIVIDENDS." },
        new[] { "A DISTANT RELATIVE DIED AND LEFT YOU A VAST FORTUNE.  BUT AFTER TAXES YOU ONLY GOT $", "." },
        new[] { "YOU FOUND A DEAD MOOSE RAT AND SOLD THE HIDE FOR $", "." },
        new[] { "YOUR SPACE GYPSY INLAWS MADE A MESS OF THE TOWN.  IT COST YOU $", " TO CLEAN IT UP." },
        new[] { "FLYING CAT-BUGS ATE THE ROOF OFF YOUR HOUSE.  REPAIRS COST $", "." },
        new[] { "YOU LOST $", " BETTING ON THE TWO-LEGGED KAZINGA RACES." },
        new[] { "YOUR CHILD WAS BITTEN BY A BAT LIZARD AND THE HOSPITAL BILL COST YOU $", "." },
        new[] { "YOU LOST A PLOT OF LAND BECAUSE THE CLAIM WAS NOT RECORDED." },
        new[] { "YOU RECEIVED AN EXTRA PLOT OF LAND TO ENCOURAGE COLONY DEVELOPMENT." },
    };

    /// <summary>The scrolling text the turn shows (addMessageStart $80EA to addMessageEnd $810F).</summary>
    public static string Text(PlayerEventResult r)
    {
        var t = Texts[r.Event];
        string body = t.Length switch
        {
            1 => t[0],
            2 => t[0] + r.Amount + t[1],
            _ => t[0] + r.Each + t[1] + r.Amount + t[2],
        };
        return "  ***  " + body + "  ***  ";
    }
}
