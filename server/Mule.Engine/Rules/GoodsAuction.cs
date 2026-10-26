using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>
/// One good's trading: the status screen (usage, spoilage, production, surplus and shortage), the
/// choice between buying and selling, and the auction ($97CA goodAuction).
/// </summary>
public static class GoodsAuction
{
    /// <summary>goodAuction ($97CA): nothing happens for smithore or crystite when nobody holds or
    /// produces any and the store has none.</summary>
    public static IEnumerable Run(Game game, byte good)
    {
        var g = game.State;
        if (good >= 2)
        {
            bool any = g.GoodsStoreNb[good] != 0;
            for (int p = 3; p >= 0 && !any; p--)
                any = (g.GoodsPlayersNb[good * 4 + p] | g.GoodsPlayersProd[good * 4 + p]) != 0;
            if (!any) yield break;
        }
        var a = new Auction { Kind = 0, Good = good };
        yield return Status(game, a);
        yield return SelectBuySell(game, a);
        game.Vbi.Handler = null;
        yield return BuySell(game, a);
    }

    // Vertical blanks the drawing of the status screen runs past (measured on the scanline Atari).
    const int StatusDrawFrames = 2;

    /// <summary>animUsageSpoilageProd ($5F07): the good's bars go from what each player held to what is
    /// left after usage and spoilage (food, energy) or the 50-unit cap (smithore, crystite), then up by
    /// the production. For food and energy before the last round, the new requirement is set and the
    /// surpluses and shortages are flashed.</summary>
    public static IEnumerable Status(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        int good = a.Good;
        v.SoundOnPress = 0;
        a.StepTimer = 0;
        for (int x = 3; x >= 0; x--)
        {
            a.Seller[x] = 0;
            g.PlayersGoodShort[x] = 0;
            a.Mask[x] = 0x0F;
            a.Pos[x] = 0xAE;
        }
        a.AtTop = 0xFF;
        SetPriceStartEnd(g, a);
        a.Speed = (byte)(g.Level == 0 && g.NumRound == 1 ? 4 : 2);
        a.Flash = 0;
        v.InitButtons();
        game.Emit(new GoodsStatusEvent(good));
        yield return game.Frames(StatusDrawFrames);

        // The amounts before anything happens.
        for (int p = 3; p >= 0; p--) a.Bid[p] = (ushort)(g.GoodsPlayersNb[good * 4 + p] * 4);
        game.Trace?.Invoke("c1=0"); // the bars start
        v.Handler = () => StatusVbl(g, v, a);
        yield return BarStep(a);
        v.Handler = null;
        yield return game.WaitJiffyLevel();
        v.SoundOnPress++;
        v.InitButtons();
        v.Handler = () => StatusVbl(g, v, a);
        for (int x = 0; x < 4; x++) g.PlayersGoodGap[x] = 0;

        if (good >= 2)
        {
            // Spoilage: anything above 50 units is lost.
            bool capped = false;
            for (int p = 3; p >= 0; p--)
            {
                int i = good * 4 + p;
                a.Bid[p] = (ushort)(g.GoodsPlayersNb[i] * 4);
                if (g.GoodsPlayersNb[i] <= 50) SetW(a.B309, 2 * p, a.Bid[p]);
                else
                {
                    g.GoodsPlayersNb[i] = 50;
                    SetW(a.B309, 2 * p, 200);
                    capped = true;
                }
            }
            if (capped)
            {
                yield return Bars(game, a, BarKind.Spoilage);
                yield return game.WaitJiffyLevel();
            }
        }
        else
        {
            byte used = g.FoodReq;
            for (int pass = 1; pass >= 0; pass--)
            {
                for (int p = 3; p >= 0; p--)
                {
                    int i = good * 4 + p;
                    if (pass == 1) { if (good == Good.Energy) used = Production.CalcEnergyReq(g, p); }
                    else used = (byte)(good == Good.Food ? g.GoodsPlayersNb[i] >> 1 : g.GoodsPlayersNb[i] >> 2);
                    a.Bid[p] = (ushort)(g.GoodsPlayersNb[i] * 4);
                    int left = g.GoodsPlayersNb[i] - used;
                    g.GoodsPlayersNb[i] = (byte)(left < 0 ? 0 : left);
                    SetW(a.B309, 2 * p, (ushort)(g.GoodsPlayersNb[i] * 4));
                }
                yield return Bars(game, a, pass == 1 ? BarKind.Usage : BarKind.Spoilage);
                yield return game.WaitJiffyLevel();
            }
        }

        // Production.
        for (int p = 3; p >= 0; p--)
        {
            int i = good * 4 + p;
            a.Bid[p] = (ushort)(g.GoodsPlayersNb[i] * 4);
            int n = g.GoodsPlayersNb[i] + g.GoodsPlayersProd[i];
            g.GoodsPlayersNb[i] = (byte)(n > 255 ? 255 : n);
            SetW(a.B309, 2 * p, (ushort)(g.GoodsPlayersNb[i] * 4));
        }
        yield return Bars(game, a, BarKind.Production);
        yield return game.WaitJiffyLevel();

        if (g.NumRound < g.NbRounds && good < 2)
        {
            if (good == Good.Food)
            {
                if ((g.NumRound & 3) == 0) g.FoodReq++;
                for (int p = 3; p >= 0; p--) g.PlayersGoodReq[p] = g.FoodReq;
            }
            else
                for (int p = 3; p >= 0; p--) g.PlayersGoodReq[p] = (byte)(Production.CalcEnergyReq(g, p) + 1);
            bool shortage = false;
            for (int p = 3; p >= 0; p--) a.InZone[p] = g.GoodsPlayersNb[good * 4 + p];
            for (int p = 3; p >= 0; p--)
            {
                if (g.PlayersGoodGap[p] != 0) continue; // the player pressed the button: not shown
                int d = g.GoodsPlayersNb[good * 4 + p] - g.PlayersGoodReq[p];
                byte kind = 0;
                if (d < 0) { shortage = true; kind = 12; d = -d; }
                a.Pos[p] = kind;
                a.Mask[p] = kind;
                a.InZone[p] = (byte)d;
            }
            game.Emit(new GoodsRequirementEvent(g.PlayersGoodReq.ToArray(), a.Pos.ToArray()));
            a.Flash = 1;
            yield return FlashSurplusAndShortage(game, a, shortage);
        }

        game.Emit(new MessageEvent("PRESS BUTTON TO CONTINUE"));
        v.Timers[0] = 0x3C;
        while (!AllPressed(g, v) && v.Timers[0] != 0) yield return null;
        yield return game.WaitJiffy(g.Level == 0 ? 80 : 40);
        if ((g.MaxIndexCpu & 0x80) == 0 && good == Good.Energy)
            for (int p = g.MaxIndexCpu; p >= 0; p--) g.PlayersGoodReq[p]++; // CPUs keep one more in reserve
        v.Handler = null;
        for (int p = 3; p >= 0; p--)
        {
            byte n = g.GoodsPlayersNb[good * 4 + p];
            g.PlayersGoodHeld[p] = n;
            if (good > 1) continue;
            int d = n - g.PlayersGoodReq[p];
            if (d < 0) { g.PlayersGoodShort[p]++; d = -d; }
            g.PlayersGoodGap[p] = (byte)d;
        }
        v.SoundOnPress = 0;
    }

    enum BarKind { Usage = 1, Spoilage = 2, Production = 3 }

    /// <summary>The cut-off of the original's button waits: every human has pressed (never with only CPUs).</summary>
    static bool AllPressed(GameState g, Vbi v) => g.MaxIndexCpu != 3 && (byte)(g.MaxIndexCpu + 1 + v.Presses) >= 4;

    /// <summary>v01 ($99F9): the status screen's vertical blank work. A bar step is allowed every
    /// <see cref="Auction.Speed"/> frames. In the tournament game a player's button hides their line.</summary>
    static void StatusVbl(GameState g, Vbi v, Auction a)
    {
        if (a.StepReady == 0 && a.StepTimer == 0)
        {
            a.StepTimer = a.Speed;
            a.StepReady++;
        }
        if (g.Level == 2)
            for (int x = 3; x >= 0; x--)
                if (g.PlayersGoodGap[x] == 0 && v.Button[x] == 0)
                    g.PlayersGoodGap[x] = 1;
        if (a.StepTimer != 0) a.StepTimer--;
    }

    /// <summary>sub_5922: waits for the next bar step.</summary>
    static IEnumerable BarStep(Auction a)
    {
        a.StepReady = 0;
        while (a.StepReady == 0) yield return null;
    }

    /// <summary>animUsageOrSpoilageOrProd ($5892): every bar moves a quarter unit per step toward its
    /// target; a bar is done on the step after it gets there.</summary>
    static IEnumerable Bars(Game game, Auction a, BarKind kind)
    {
        for (int x = 3; x >= 0; x--) a.Seller[x] = 0;
        game.Emit(new GoodsBarsEvent((int)kind, a.Bid.ToArray(), Enumerable.Range(0, 4).Select(p => W(a.B309, 2 * p)).ToArray()));
        bool up = kind == BarKind.Production;
        int done = 0;
        while (done != 4)
        {
            yield return BarStep(a);
            done = 0;
            for (int x = 3; x >= 0; x--)
            {
                if (a.Seller[x] != 0) { done++; continue; }
                ushort dest = W(a.B309, 2 * x);
                byte bh = (byte)(a.Bid[x] >> 8), dh = (byte)(dest >> 8), bl = (byte)a.Bid[x], dl = (byte)dest;
                bool step = bh != dh || (bl != dl && (up ? bl < dl : bl >= dl));
                if (step) a.Bid[x] = (ushort)(up ? a.Bid[x] + 1 : a.Bid[x] - 1);
                else { a.Seller[x]++; done++; }
            }
        }
    }

    /// <summary>sub_59C7: four rounds, surplus then shortage, each flashing its players' lines five
    /// times, two button-cut waits per flash; a round with nobody in it is skipped.</summary>
    static IEnumerable FlashSurplusAndShortage(Game game, Auction a, bool shortage)
    {
        var g = game.State;
        byte kind = 0;
        for (int round = 4; round > 0; round--)
        {
            for (int flash = 4; flash >= 0; flash--)
            {
                if (CountShown(g, a, kind) == 0) break;
                game.Emit(new MessageEvent(kind == 0 ? "SURPLUS" : "SHORTAGE"));
                yield return CutWait(game, 4);
                CountShown(g, a, kind);
                yield return CutWait(game, 4);
            }
            kind ^= 12;
        }
    }

    /// <summary>loc_5A34: players shown in this category (surplus 0, shortage 12).</summary>
    static int CountShown(GameState g, Auction a, byte kind)
    {
        int n = 0;
        for (int x = 3; x >= 0; x--)
            if (g.PlayersGoodGap[x] == 0 && a.Mask[x] == kind) n++;
        return n;
    }

    /// <summary>sub_5A1E: <paramref name="ticks"/> timer ticks, or until every human has pressed.</summary>
    static IEnumerable CutWait(Game game, byte ticks)
    {
        var v = game.Vbi;
        game.Trace?.Invoke("cut wait");
        v.Timers[0] = ticks;
        while (!AllPressed(game.State, v) && v.Timers[0] != 0) yield return null;
    }

    /// <summary>setPriceStartEnd ($327D): the scale for the good. Food and energy open 15 below the
    /// price; smithore and crystite at the low byte of the price rounded down to the step (crystite
    /// steps by 4), at least 4 steps. The scale spans 35 steps.</summary>
    public static void SetPriceStartEnd(GameState g, Auction a)
    {
        int good = a.Good;
        a.Increment = Increments[good];
        g.ProdMax = a.Increment; // the same zero-page byte ($94)
        a.ScaleIndex = (byte)(good * 2);
        if (good >= 2)
        {
            ushort p = RoundDown(a, (byte)g.GoodsPrice[good]);
            if (p < (byte)(a.Increment * 4)) p = (byte)(a.Increment * 4);
            a.PriceStart = p;
            g.LastPriceStart[good] = p;
        }
        else
        {
            a.PriceStart = (ushort)(g.GoodsPrice[good] - 15);
            g.LastPriceStart[good] = a.PriceStart;
        }
        a.PriceEnd = (ushort)(a.PriceStart + 35 * a.Increment);
    }

    static readonly byte[] Increments = { 1, 1, 1, 4, 1, 4 }; // $4274 food, energy, smithore, crystite, mule, land

    /// <summary>sub_9053: a price rounded down to the scale's step.</summary>
    static ushort RoundDown(Auction a, ushort price) => a.Increment == 1 ? price : (ushort)(price & 0xFFFC);

    static ushort W(byte[] t, int i) => (ushort)(t[i] | (t[i + 1] << 8));
    static void SetW(byte[] t, int i, ushort v) { t[i] = (byte)v; t[i + 1] = (byte)(v >> 8); }

    static IEnumerable SelectBuySell(Game game, Auction a) => throw new NotImplementedException();
    static IEnumerable BuySell(Game game, Auction a) => throw new NotImplementedException();
}
