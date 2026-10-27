using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>The status summary: players in ranking order with money, land, goods and total, and the colony total.</summary>
public sealed record ScoreShownEvent(int Round, byte[] Order, int[] Scores, int Colony) : GameEvent;

/// <summary>A player of the score screen walks in to its row (slot = ranking place, x/y in screen units).</summary>
public sealed record ScoreWalkEvent(int Slot, byte X, byte Y) : GameEvent;

/// <summary>The best player in a successful colony is named first founder (round 12, rating 3 or more).</summary>
public sealed record FirstFounderEvent(int Player) : GameEvent;

/// <summary>The colony failed for a total lack of food or energy: the game is over.</summary>
public sealed record ColonyFailedEvent(int Good) : GameEvent;

/// <summary>The score screen ($6CDB to $6F6B): the ranking, the walk in, the colony message and the wait.</summary>
public static class ScoreScreen
{
    // Frames the original's score works out in BCD floats ($6CDB-$6EA4), then draws the screen before
    // its VCOUNT sync ($6EA5-$71B0), then writes the table ($6EAB-$6F18). Measured on the original.
    const int CalcFrames = 3, DrawFrames = 2, TextFrames = 3;

    const string Stars = "  ***  ";
    static readonly string[] GoodNames = { " FOOD ", " ENERGY ", " SMITHORE ", " CRYSTITE " }; // $3A2B

    // successesMessage ($0600-$07E6), by rating 0-6.
    static readonly string[] Ratings =
    {
        "FAILED... DISMALLY.  THE FEDERATION DEBTORS' PRISON IS YOUR NEXT HOME",
        "FAILED... THE FEDERATION WILL NO LONGER SEND TRADE SHIPS.  YOU ARE ON YOUR OWN",
        "SURVIVED... BARELY.  YOU WILL BE LIVING IN TENTS.  FEW TRADING SHIPS WILL COME YOUR WAY",
        "WAS A SUCCESS.  YOU HAVE MET THE MINIMUM STANDARDS SET BY THE FEDERATION, BUT YOUR LIFE WILL NOT BE EASY",
        "SUCCEEDED.  THE FEDERATION IS PLEASED BY YOUR EFFORTS.  YOU WILL LIVE COMFORTABLY",
        "SUCCEEDED... EXTREMELY WELL.  YOU CAN NOW RETIRE IN ELEGANT ESTATES",
        "DELIGHTED THE FEDERATION WITH YOUR EXCEPTIONAL ACHIEVEMENT.  YOUR RETIREMENT WILL BE LUXURIOUS",
    };

    /// <summary>The outcome of the score: the colony message, and whether the colony failed.</summary>
    public sealed class Outcome
    {
        public string Message = "";
        public bool Failed;
        public int FailedGood;
    }

    /// <summary>calcAndDrawScore ($6CDB): works out the score, then shows it (drawScore).</summary>
    public static IEnumerable CalcAndDraw(Game game, Outcome result)
    {
        game.Vbi.Handler = null;
        Scoring.CalcScore(game.State, game.Rng);
        yield return game.Frames(game.Think?.Invoke("score calc") ?? CalcFrames);
        yield return Draw(game, result);
    }

    /// <summary>drawScore ($6EA5).</summary>
    public static IEnumerable Draw(Game game, Outcome result)
    {
        var g = game.State;
        var v = game.Vbi;
        game.Trace?.Invoke("score shown");
        g.CurrentPlayer = g.OrdersPlayer[3]; // left by the table's loop ($6EC7)
        game.Emit(new ScoreShownEvent(g.NumRound, g.OrdersPlayer.ToArray(), g.TypesPlayersScore.ToArray(), g.TotalScore));
        yield return game.Frames(game.Think?.Invoke("score draw") ?? DrawFrames);
        var x = new byte[] { 0xC3, 0xD7, 0xEB, 0xFF };   // $9F
        game.Trace?.Invoke("score walk");
        yield return WalkIn(game, x);
        if (g.NumRound == 12 && g.SuccessScore >= 3) yield return FirstFounder(game, x[0]);
        yield return game.Frames(game.Think?.Invoke("score text") ?? TextFrames);
        MessageScore(game, result);
        game.Emit(new MessageEvent(result.Message));
        if (result.Failed) game.Emit(new ColonyFailedEvent(result.FailedGood));
        v.InitButtons();
        game.Trace?.Invoke("score wait");
        yield return Wait(game);
        // loc_6F60: the screen cleared, no handler, the sound off.
        v.Handler = null;
        game.Trace?.Invoke("score end");
    }

    /// <summary>sub_714F with the handler $0A (v11, $7086): every six frames each player in turn takes a
    /// 4-unit step, left to column $3C (the first founder stops at $78), then up past its row.</summary>
    static IEnumerable WalkIn(Game game, byte[] x)
    {
        var g = game.State;
        var v = game.Vbi;
        var y = new byte[] { 0xB2, 0xB2, 0xB2, 0xB2 };   // $A3
        var row = new byte[] { 50, 86, 122, 158 };       // $B1B5, $FF once there
        byte count = 8;                                  // $A9
        bool founder = g.NumRound == 12 && g.SuccessScore >= 3;
        v.Handler = () =>
        {
            v.Presses = 0;
            count--;
            if ((count & 0x80) != 0) { count = 5; return; }
            if (count >= 4) return;
            int s = count;
            if ((founder && s == 0 && x[s] < 0x78) || x[s] < 0x3C)
            {
                if (y[s] < row[s]) { row[s] = 0xFF; return; }
                y[s] -= 4;
            }
            else x[s] -= 4;
            game.Emit(new ScoreWalkEvent(s, x[s], y[s]));
        };
        while ((row[0] & row[1] & row[2] & row[3]) != 0xFF) yield return null;
        // The music handler ($02) takes over.
        v.Handler = null;
    }

    /// <summary>animFirstFounder ($7000): the leader's colour pulses up and down 17 times, a frame a
    /// step, then it walks left (handler $0F, v16: a step every six frames after the first fifteen).</summary>
    static IEnumerable FirstFounder(Game game, byte x)
    {
        var g = game.State;
        var v = game.Vbi;
        game.Emit(new FirstFounderEvent(g.OrdersPlayer[0]));
        g.PlayersGoodShort[0] = 4;  // byte_B1AD, shared with the goods auction
        int lum = g.PlayersColor[g.OrdersPlayer[0]] & 0x0F;
        for (int k = 17; k > 0; k--)
        {
            for (; lum != 0x0F; lum++) yield return game.WaitJiffy(1);
            for (; lum != 0; lum--) yield return game.WaitJiffy(1);
        }
        byte count = 0x0F;
        v.Handler = () =>
        {
            if (--count != 0) return;
            count = 6;
            x -= 4;
            if (x < 0x35) g.PlayersGoodShort[0] = 0;
            game.Emit(new ScoreWalkEvent(0, x, 0));
        };
        while (g.PlayersGoodShort[0] != 0) yield return null;
        v.Handler = null;
    }

    /// <summary>drawMessageScore ($89D3): after round 12 the colony's rating; before, any shortage of
    /// food, energy or mules (setting goodsShortage for the CPUs), or the end of the game when there
    /// is no food (or energy) left at all and nobody grows it.</summary>
    static void MessageScore(Game game, Outcome result)
    {
        var g = game.State;
        var m = new System.Text.StringBuilder(Stars);
        if (g.NumRound == 12)
        {
            m.Append("OVERALL, THE COLONY ").Append(Ratings[Scoring.RatingIndex(g)]).Append('!').Append(Stars);
            result.Message = m.ToString();
            return;
        }
        bool any = false; // byte_BC
        Array.Clear(g.GoodsShortage);
        byte food = Total(g, Good.Food), energy = Total(g, Good.Energy);
        for (int good = 0; good < 2; good++)
        {
            if ((good == 0 ? food : energy) != 0) continue;
            bool grown = false;
            for (int p = 3; p >= 0; p--) if (g.ExpsPlayersNb[good * 4 + p] != 0) grown = true;
            if (grown) continue;
            m.Append("THE COLONY FAILED BECAUSE OF A TOTAL LACK OF").Append(GoodNames[good]);
            m.Length--; // addMessageEnd2 writes the '!' over the name's last space
            m.Append('!').Append(Stars);
            result.Message = m.ToString();
            result.Failed = true;
            result.FailedGood = good;
            return;
        }
        void Shortage(int good)
        {
            m.Append(any ? "AND" : "THE COLONY HAS A SHORTAGE OF").Append(GoodNames[good]);
            any = true;
        }
        if ((byte)(g.FoodReq * 4) > food) { g.GoodsShortage[0]++; Shortage(0); }
        byte need = 0;
        for (int p = 3; p >= 0; p--) need = (byte)(need + Production.CalcEnergyReq(g, p) + 1);
        if (need > energy) { g.GoodsShortage[1]++; Shortage(1); }
        int mules = Prices.CalcMuleReq(g, game.Rng) - g.GoodsStoreNb[Good.Mule];
        if (mules > 0)
        {
            g.GoodsShortage[2]++;
            if (any) m.Append("!  ");
            m.Append("THE STORE HAS A SHORTAGE OF SMITHORE FOR MULES!").Append(Stars);
        }
        else if (any)
        {
            m.Length--; // addMessageEnd2 writes the '!' over the name's last space
            m.Append('!').Append(Stars);
        }
        // With nothing to say the message is never ended, so it does not show.
        result.Message = any || mules > 0 ? m.ToString() : "";
    }

    /// <summary>The store's and the players' units of a good, stopping at 255.</summary>
    static byte Total(GameState g, int good)
    {
        int t = g.GoodsStoreNb[good];
        for (int p = 3; p >= 0; p--) t = Math.Min(255, t + g.GoodsPlayersNb[good * 4 + p]);
        return (byte)t;
    }

    /// <summary>scoreWait ($6F19): "press all player buttons" for 100 ticks and "press option and start
    /// to restart" for 30, in turn, until every human has pressed; with only CPUs 510 frames (1020 after
    /// round 12) after the first message.</summary>
    public static IEnumerable Wait(Game game)
    {
        var g = game.State;
        var v = game.Vbi;
        for (int k = 0; ; k++)
        {
            bool press = (k & 1) == 0;
            game.Emit(new MessageEvent(press ? " PRESS ALL PLAYER BUTTONS TO GO ON. " : " * PRESS OPTION & START TO RESTART *"));
            yield return LandAuction.WaitButtonsTick(game, press ? (byte)0x64 : (byte)0x1E);
            if (v.Presses >= 4) yield break;
            if (g.MaxIndexCpu != 3) continue;
            yield return game.WaitJiffy(255);
            yield return game.WaitJiffy(255);
            if (g.NumRound == 12)
            {
                yield return game.WaitJiffy(255);
                yield return game.WaitJiffy(255);
            }
            yield break;
        }
    }
}
