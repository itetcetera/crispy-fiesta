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
    public static IEnumerable Run(Game game, byte good) => Run(game, new Auction { Kind = 0, Good = good });

    public static IEnumerable Run(Game game, Auction a)
    {
        yield return Prepare(game, a);
        if (a.CalledOff) yield break;
        game.Vbi.Handler = null;
        yield return BuySell(game, a);
    }

    /// <summary>Everything before the trading: the status screen and the buy/sell choice. The auction
    /// is called off when there is nothing to trade.</summary>
    public static IEnumerable Prepare(Game game, Auction a)
    {
        var g = game.State;
        int good = a.Good;
        if (good >= 2)
        {
            bool any = g.GoodsStoreNb[good] != 0;
            for (int p = 3; p >= 0 && !any; p--)
                any = (g.GoodsPlayersNb[good * 4 + p] | g.GoodsPlayersProd[good * 4 + p]) != 0;
            if (!any) { a.CalledOff = true; yield break; }
        }
        yield return Status(game, a);
        yield return SelectBuySell(game, a);
    }

    // Vertical blanks the drawing of the status screen runs past (measured on the scanline Atari).
    const int StatusDrawFrames = 3;

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
        yield return game.Frames(game.Think?.Invoke("status draw") ?? StatusDrawFrames);

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
            // The requirement line's height, kept in the same bytes as the CPUs' sides.
            for (int p = 3; p >= 0; p--) a.Role[p] = (byte)(g.PlayersGoodReq[p] << 2);
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
    static bool AllPressed(GameState g, Vbi v) => g.MaxIndexCpu != 3 && (byte)(g.MaxIndexCpu + 1 + v.Presses + v.AwayNotPressed()) >= 4;

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

    // Vertical blanks the drawing of the buy/sell screen runs past (measured on the scanline Atari).
    const int SelectDrawFrames = 2;

    /// <summary>animSelectBuySell ($63F5): with nothing to trade the auction is called off. Otherwise
    /// players start as sellers when they hold units (food and energy: more than they need), CPUs
    /// decide what to trade, and everyone has 31 time-bar steps to push up (sell) or down (buy).</summary>
    public static IEnumerable SelectBuySell(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        int good = a.Good;
        v.InitButtons();
        bool any = g.GoodsStoreNb[good] != 0;
        for (int p = 3; p >= 0 && !any; p--) any = g.GoodsPlayersNb[good * 4 + p] != 0;
        if (!any)
        {
            game.Emit(new MessageEvent("NO COMMODITY"));
            yield return game.Beeps(4);
            yield return game.WaitJiffyLevel();
            a.CalledOff = true;
            yield break;
        }
        for (int x = 3; x >= 0; x--)
        {
            v.Timers[x] = 4;
            byte held = g.GoodsPlayersNb[good * 4 + x];
            bool seller = held != 0 && (good >= 2 || held > g.PlayersGoodReq[x]);
            a.Seller[x] = a.Role[x] = seller ? (byte)1 : (byte)0;
            a.Pos[x] = seller ? (byte)0x52 : (byte)0xAE;
            if (seller) game.Play(0);
        }
        if ((g.MaxIndexCpu & 0x80) == 0) Ai1(g, a);
        LandAuction.Ai2(g, a);
        if ((g.MaxIndexCpu & 0x80) == 0) Ai4(g, game.Rng, a);
        for (int x = 3; x >= 0; x--) a.Mask[x] = 0x0F;
        a.Time = 0x1E;
        a.Tick = 0;
        a.FrameDone = 0;
        game.Emit(new GoodsSelectEvent(good, a.Seller.ToArray()));
        yield return game.Frames(SelectDrawFrames);
        // The drawing sometimes takes one or two jiffies more (docs/PLAN.md, timing effects).
        while (game.Race?.Invoke("select late") == true) yield return null;
        v.Handler = () => SelectVbl(game, a);
        game.Trace?.Invoke("select");
        bool first = true;
        while (true)
        {
            a.StepReady = 0;
            while (a.StepReady == 0) yield return null;
            v.SoundOnPress = 0;
            if (first)
            {
                for (int x = g.MaxIndexCpu; x >= 0 && (g.MaxIndexCpu & 0x80) == 0; x--) v.Stick[x] = (byte)(a.Role[x] + 0x0D);
                first = false;
            }
            if (a.Changed != 0) game.Emit(new GoodsSelectEvent(good, a.Seller.ToArray()));
            if ((a.Time & 0x80) != 0) break;
        }
        v.Handler = null;
        v.InitButtons();
        yield return game.Beeps(1);
        yield return game.WaitJiffy(30);
    }

    /// <summary>v02 ($9A94): the time bar steps every levelAuctionTimeWait/4 frames; a player's stick
    /// (up sell, down buy) switches side, then not again for 7 timer ticks.</summary>
    static void SelectVbl(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        if (a.Tick == 0)
        {
            a.Tick = (byte)(g.LevelAuctionTimeWait >> 2);
            a.Time--;
            if ((a.Time & 0x80) != 0) { a.StepReady = 1; return; }
        }
        a.Changed = 0;
        for (int x = 3; x >= 0; x--)
        {
            if (v.Timers[x] == 0)
            {
                byte s = (byte)(v.Stick[x] | 0x0C);
                int y = s == 0x0D ? 0 : s == 0x0E ? 1 : -1;
                if (y >= 0 && y != a.Seller[x])
                {
                    a.Seller[x] = (byte)y;
                    a.Changed = 1;
                    game.Trace?.Invoke($"switch {x} {y}");
                    game.Play(0);
                    a.Pos[x] = y != 0 ? (byte)0x52 : (byte)0xAE;
                    v.Timers[x] = 7;
                }
            }
            v.ButtonPushed[x] = 0;
        }
        if (a.FrameDone == 0) a.FrameDone++;
        if (a.Tick != 0) a.Tick--;
        a.StepReady = 1;
    }

    /// <summary>ai1 ($AD4B) for goods: each CPU keeps the price of a MULE plus 75; for food, also what
    /// the energy it lacks would cost; for energy with no food left, nothing.</summary>
    static void Ai1(GameState g, Auction a)
    {
        var t = g.PlotsTemp3;
        for (int cpu = g.MaxIndexCpu; cpu >= 0; cpu--)
        {
            int x = 2 * cpu;
            ushort keep = (ushort)(g.MulePrice + 75);
            if (a.Good == Good.Food)
            {
                int d = g.PlayersNbExps[cpu] - g.ExpsPlayersNb[4 + cpu];
                bool borrow = d < 0;
                d = (byte)d - g.GoodsPlayersProd[4 + cpu] - (borrow ? 1 : 0);
                if (d >= 0)
                {
                    int cost = (byte)d * (byte)g.GoodsPrice[Good.Energy];
                    keep = (ushort)(keep + cost);
                }
            }
            else if (a.Good == Good.Energy && g.GoodsPlayersNb[Good.Food * 4 + cpu] == 0) keep = 1;
            LandAuction.SetW(t, Keep + x, keep);
        }
    }

    const int Keep = 8, Willing = 0x11;

    /// <summary>animSelectBuySellAi4 ($AA42): how many units each CPU trades (B487). Nothing in the last
    /// round. Food and energy: the surplus or shortage; in the tournament a seller with a small surplus,
    /// a low price and enough cash sells a random part of it. Smithore and crystite: everything when the
    /// price reaches 50 + level (smithore) or 100 (crystite); else enough to avoid spoilage or reach 1000
    /// in cash; with nothing to sell and a price under 46 (86), buy with what is above 800 (at most 25,
    /// not past 50 held).</summary>
    static void Ai4(GameState g, IRandomSource rng, Auction a)
    {
        int good = a.Good;
        var t = g.PlotsTemp3;
        if (g.NumRound >= g.NbRounds)
        {
            for (int x = g.MaxIndexCpu; x >= 0; x--) a.CpuUnits[x] = 0;
            return;
        }
        if (good < 2)
        {
            for (int x = g.MaxIndexCpu; x >= 0; x--)
            {
                byte n = g.PlayersGoodGap[x];
                if (a.Seller[x] != 0 && g.Level == 2 && n < (good == Good.Food ? 6 : 9) && a.PriceStart < 81)
                {
                    // The cash to keep is read with the player's index, not twice it.
                    ushort keep = (ushort)(t[Keep + x] | (t[Keep + x + 1] << 8));
                    if (keep < g.PlayersCash[x]) n = rng.Below((byte)(n + 1));
                }
                a.CpuUnits[x] = n;
            }
            return;
        }
        int k = good - 2;
        for (int x = g.MaxIndexCpu; x >= 0; x--)
        {
            byte held = g.GoodsPlayersNb[good * 4 + x];
            byte n;
            if (held != 0)
            {
                byte limit = (byte)(MinSell[k] + (k == 0 ? g.Level : 0));
                if ((byte)a.PriceStart >= limit) { a.CpuUnits[x] = held; continue; }
                byte spoil = held >= 51 ? (byte)(held - 50) : (byte)0;
                byte toCash = 0;
                int missing = 1000 - g.PlayersCash[x];
                if (missing >= 0)
                {
                    ushort q = Div16((ushort)missing, (byte)a.PriceStart);
                    toCash = q >= 256 ? (byte)255 : (byte)q;
                }
                if ((toCash | spoil) != 0)
                {
                    a.CpuUnits[x] = spoil >= toCash ? spoil : toCash;
                    continue;
                }
            }
            // Nothing to sell: maybe buy.
            n = 0;
            ushort willing = LandAuction.W(t, Willing + 2 * x);
            if ((byte)a.PriceStart < MinBuy[k] && willing >= 1100)
            {
                ushort q = Div16((ushort)(willing - 800), (byte)a.PriceStart);
                if (q != 0)
                {
                    a.Role[x] = 0;
                    byte want = (byte)q >= 25 ? (byte)25 : (byte)q;
                    byte room = (byte)(50 - held);
                    n = room < want ? room : want;
                }
            }
            a.CpuUnits[x] = n;
        }
    }

    static readonly byte[] MinSell = { 50, 100 }, MinBuy = { 46, 86 }; // $AB75, $AB77

    /// <summary>div16Bits ($11A2): 16 by 8 bits; dividing by 0 gives $FFFF.</summary>
    internal static ushort Div16(ushort n, byte d) => d == 0 ? (ushort)0xFFFF : (ushort)(n / d);

    // Vertical blanks the trading screen's drawing runs past before its handler starts (measured on
    // the scanline Atari).
    const int TradeDrawFrames = 3;

    /// <summary>auctionBuySell ($9D2A) and auctionBuySellGoodAuction ($9F57): sellers start at the top,
    /// buyers at the bottom; when the highest bid meets the lowest ask, units change hands one by one
    /// until either side moves away, runs out of units or cash. The store sells at the top price while
    /// it has units and buys at the opening price. The good's price becomes the average of the trades.</summary>
    public static IEnumerable BuySell(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = g.PlotsTemp3;
        int good = a.Good;
        a.Flag43 = 0;
        if (g.GoodsStoreNb[good] == 0)
        {
            a.Flag43 = 1;
            bool any = false;
            for (int x = 3; x >= 0; x--)
                if (a.Seller[x] != 0 && g.GoodsPlayersNb[good * 4 + x] != 0) any = true;
            if (!any)
            {
                game.Trace?.Invoke("no sellers");
                game.Emit(new MessageEvent("NO SELLERS"));
                yield return game.Beeps(3);
                yield return game.WaitJiffyLevel();
                yield break;
            }
        }
        // $9D92: the screen is built after a VCOUNT sync.
        v.InitButtons();
        for (int x = 3; x >= 0; x--)
        {
            a.Out[x] = 0; a.Track[x] = 0; a.InOffer[x] = 0; g.PlayersGoodHeld[x] = 0; a.InZone[x] = 0;
            t[0x1A + x] = 0; t[0x1E + x] = 0; t[0x22 + x] = 0;
        }
        a.TickDone = 0xFF; a.LogCount = 0xFF; a.SellerPlayer = 0xFF; a.AtTop = 0xFF; a.FrameDone = 0xFF;
        a.MessageHold = 0xFF;
        a.FlashTimer = 0; a.StepTimer = 0; v.Timers[0] = 0; a.MessageShown = 0;
        a.StoreBuys = 0; a.ScaleRaised = 0; a.Offer = 0; a.OfferSetUp = 0; a.Requests = 0; a.RequestWindow = 0;
        a.Time = 0x46;
        a.CpuBound = 0xFA;
        a.Limit = 0xAF;
        a.Top = 0x9A;
        a.Tick = 0; a.Trade = 0; a.PriceCount = 0; a.UnitsTraded = 0; a.PriceSum = 0;
        a.Increment = Increments[good];
        a.ScaleIndex = (byte)(good * 2);
        SetPriceStartEnd(g, a);
        a.Line = 0x54;
        for (int x = 3; x >= 0; x--)
        {
            if (a.Seller[x] == 0) continue;
            a.High[x] = 0x9A;
            byte held = g.GoodsPlayersNb[good * 4 + x];
            if (held == 0 || (g.Level == 0 && good < 2 && held <= g.PlayersGoodReq[x])) a.Out[x] = 1;
        }
        if ((g.MaxIndexCpu & 0x80) == 0)
        {
            int sellers = a.Seller[0] + a.Seller[1] + a.Seller[2] + a.Seller[3];
            a.Mix = sellers == 0 ? (byte)0xFF : sellers == 4 ? (byte)0 : (byte)1;
            if (a.Mix == 0xFF && g.Level == 2) Ai8(g, a);
            Ai5(g, a);
        }
        for (int x = 3; x >= 0; x--)
        {
            if (a.Seller[x] == 0) { a.Pos[x] = 0xAE; a.High[x] = 0xAE; }
            else { a.Pos[x] = 0x52; a.Low[x] = 0x52; }
        }
        game.Emit(new GoodsAuctionStartedEvent(good, a.PriceStart, a.PriceEnd, a.Seller.ToArray(), a.Flag43 == 0));
        yield return game.Frames(game.Think?.Invoke("trade draw") ?? TradeDrawFrames);
        v.Handler = () => Vbl(game, a);
        game.Trace?.Invoke("trade");

        bool skipAi3 = false;
        while (true)
        {
            if (!skipAi3)
            {
                ClearMessage(v, a);
                LandAuction.Ai3(g, a);
                LandAuction.Masks(a);
            }
            skipAi3 = false;
            if ((g.MaxIndexCpu & 0x80) == 0) Ai12(game, a);
            game.Trace?.Invoke("rnd waittick");
            a.TickDone = 0;
            while (a.TickDone == 0) yield return null;
            yield return CollusionStart(game, a);
            LandAuction.RaiseScale(g, a);
            if (a.PriceChanged != 0 && a.ScaleRaised == 0)
            {
                // The store no longer buys once the scale has moved up.
                a.ScaleRaised = 1;
                a.StoreBuys--;
            }
            if (a.PriceChanged != 0) game.Emit(new AuctionPriceEvent(a.PriceStart));
            a.HighBid = 0;
            a.Ask = 0xFF;
            a.LowAsk = (ushort)(a.PriceEnd + 1);
            InZone(a);
            for (int i = 3; i >= 0; i--)
            {
                int x = g.OrdersPlayer[i];
                if (a.Out[x] != 0 && (!g.IsCpu(x) || t[0x1A + x] == 0)) continue;
                if (a.InZone[x] == 0) continue;
                a.Bid[x] = LandAuction.PriceAt(g, a, a.Pos[x]);
                if (a.Seller[x] == 0)
                {
                    if (a.HighBid >= a.Bid[x]) continue;
                    a.HighBid = a.Bid[x];
                    g.CurrentPlayer = (byte)x;
                }
                else
                {
                    if (a.Bid[x] >= a.LowAsk) continue;
                    a.LowAsk = a.Bid[x];
                    a.Ask = (byte)x;
                }
            }
            if (a.HighBid < a.PriceStart)
            {
                a.HighBid = a.PriceStart;
                g.CurrentPlayer = 0xFF;
            }
            if (a.Flag43 == 0 && a.PriceEnd < a.LowAsk)
            {
                a.LowAsk = a.PriceEnd;
                a.Ask = 0xFF;
            }
            byte cp = g.CurrentPlayer;
            if (a.Flag43 != 0 && g.Level != 0 && (cp & 0x80) == 0 && a.Pos[cp] == 0x54 && v.Stick[cp] != 0x0E)
            {
                // Another buyer pushing up on the top line takes over.
                LandAuction.MasksAtTop(g, a);
                for (int i = 3; i >= 0; i--)
                {
                    int x = g.OrdersPlayer[i];
                    if (x != cp && a.Pos[x] < 0x55 && (v.Stick[x] | a.Mask[x]) == 0x0E)
                    {
                        g.CurrentPlayer = (byte)x;
                        break;
                    }
                }
                cp = g.CurrentPlayer;
            }
            game.Trace?.Invoke(StepText(g, a));
            a.FrameDone = 0;
            while (a.FrameDone == 0) yield return null;
            game.Emit(new GoodsAuctionStateEvent(a.Pos.ToArray(), cp, a.Ask, a.HighBid, a.LowAsk, a.Time));

            a.Trade = 0;
            if (((cp | a.Ask) & 0x80) == 0 && a.HighBid >= a.LowAsk)
            {
                if (a.HighBid != a.LowAsk)
                {
                    // sub_978D: the buyer comes down to the seller's price.
                    a.Pos[cp] = a.Pos[a.Ask];
                    a.Bid[cp] = a.Bid[a.Ask];
                    a.FrameDone = 0;
                    while (a.FrameDone == 0) yield return null;
                }
                // Buyers above the seller and sellers below come to the seller's price.
                byte ask = a.Ask;
                for (int x = 3; x >= 0; x--)
                {
                    if (a.Out[x] != 0) continue;
                    bool move = a.Seller[x] == 0 ? a.Pos[ask] >= a.Pos[x] : a.Pos[ask] < a.Pos[x];
                    if (!move) continue;
                    a.Pos[x] = a.Pos[ask];
                    a.Bid[x] = a.Bid[ask];
                }
                yield return LandAuction.Freeze(a);
                a.Trade = 1;
            }
            else if (a.Flag43 == 0 && a.HighBid >= a.PriceEnd) a.Trade = 0xFF;
            else if ((a.StoreBuys & 0x80) == 0 && a.PriceStart >= a.LowAsk) a.Trade = 0x80;
            if (a.Trade != 0)
            {
                a.TradeDelay = 0x0C;
                v.Timers[2] = 0x0C;
                v.Timers[1] = 5;
                yield return Trade(game, a);
                LandAuction.Ai2(g, a);
                LandAuction.Ai3(g, a);
                if ((g.MaxIndexCpu & 0x80) == 0 && good < 2)
                {
                    Ai6(g, a);
                    byte x = a.Ask;
                    if ((x & 0x80) == 0 && g.IsCpu(x) && a.CpuUnits[x] != 0 && t[0x22 + x] == 0)
                    {
                        // A CPU seller at a low price may stop selling.
                        a.HighBid >>= 1;
                        int d = 0x41 - a.HighBid;
                        if (d >= 0 && game.Rng.Below(100) < (byte)d) t[0x22 + x]++;
                    }
                }
                if (a.Offer == 0) BackToStart(g, a);
            }
            // $A25E
            if ((a.Time & 0x80) != 0)
            {
                if (a.Offer != 0)
                {
                    yield return Over(game, a);
                    yield return CollusionEnd(game, a);
                    BackToStart(g, a);
                    continue;
                }
                break;
            }
            cp = g.CurrentPlayer;
            if (a.Offer == 0 && a.Flag43 != 0 && g.Level != 0 && (cp & 0x80) == 0 && a.Pos[cp] == 0x54)
            {
                a.AtTop = cp;
                LandAuction.MasksAtTop(g, a);
                ClearMessage(v, a);
                skipAi3 = true;
            }
        }
        if (a.PriceCount != 0) g.GoodsPrice[good] = Div16(a.PriceSum, a.PriceCount);
        yield return Over(game, a);
        v.Handler = null;
        game.Emit(new GoodsAuctionEndedEvent(good, g.GoodsPrice[good]));
    }

    internal static string StepText(GameState g, Auction a) =>
        $"step {g.CurrentPlayer:X2} {a.Ask:X2} {a.HighBid} {a.LowAsk} {a.Time:X2} {string.Join(",", a.Pos.Select(p => p.ToString("X2")))}";

    /// <summary>sub_97FB: buyers below the store's buying line and sellers above its selling line are
    /// in the trading zone.</summary>
    static void InZone(Auction a)
    {
        for (int x = 3; x >= 0; x--)
            if (a.Seller[x] == 0) a.InZone[x] = a.Pos[x] < 0x9B ? (byte)1 : (byte)0;
        for (int x = 3; x >= 0; x--)
            if (a.Seller[x] != 0) a.InZone[x] = a.Pos[x] >= 0x54 ? (byte)1 : (byte)0;
    }

    /// <summary>sub_990F: the notice line is cleared once its timer has run out.</summary>
    static void ClearMessage(Vbi v, Auction a)
    {
        if ((v.Timers[0] | a.MessageShown) != 0) return;
        a.MessageShown--;
    }

    /// <summary>sub_933B: humans who are out go back to their side's start.</summary>
    static void BackToStart(GameState g, Auction a)
    {
        for (int x = 3; x >= 0; x--)
            if (a.Out[x] != 0 && !g.IsCpu(x)) a.Pos[x] = a.Seller[x] != 0 ? (byte)0x52 : (byte)0xAE;
    }

    /// <summary>v04 ($9B47): the goods auction's vertical blank work: the movement tick every 8 frames,
    /// the time bar (held during a trade, 4 times faster when nobody in the zone moves), the trading
    /// sides' sticks, collusion requests and the trade speed-up.</summary>
    static void Vbl(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        game.Trace?.Invoke($"rnd vbl {a.Tick:X2} {a.FrameDone:X2} {a.TickDone:X2} {a.MessageHold:X2} {a.FlashTimer:X2} {v.Timers[2]:X2} {v.RtClock:X2}");
        if (a.FrameDone == 0) a.FrameDone++;
        else if (a.MessageHold == 0 && a.FlashTimer == 0)
        {
            a.FlashTimer = 0x15;
            goto tail;
        }
        else if (a.TickDone == 0 && (a.Tick & 0x80) != 0)
        {
            a.Tick += 8;
            game.Trace?.Invoke("rnd move");
            LandAuction.Move(g, v, a);
            a.TickDone = 1;
        }
        if (a.Trade == 0 && a.StepTimer == 0)
        {
            a.StepTimer = g.GoodsTimeReload;
            if ((a.Time & 0x80) != 0) return;
            a.Time--;
            game.Trace?.Invoke("rnd time");
            if (a.Time == 0)
            {
                a.Time--;
                return;
            }
        }
    tail:
        a.Tick--;
        if (a.FlashTimer != 0) a.FlashTimer--;
        if (a.StepTimer != 0) a.StepTimer--;
        byte cp = g.CurrentPlayer;
        if ((cp & 0x80) == 0)
        {
            byte s = (byte)(v.Stick[cp] | a.Mask[cp]);
            if (s != 0x0F) a.StickBuyer = s;
        }
        if ((a.Ask & 0x80) == 0)
        {
            byte s = (byte)(v.Stick[a.Ask] | a.Mask[a.Ask]);
            if (s != 0x0F) a.StickSeller = s;
        }
        byte all = 0x0F;
        for (int x = 3; x >= 0; x--)
            if (a.InZone[x] != 0) all &= v.Stick[x];
        byte reload = g.LevelAuctionTimeWait;
        if ((all | 0x0C) == 0x0F) reload >>= 2;
        g.GoodsTimeReload = reload;
        if (a.Offer == 0) CollusionRequests(g, v, a);
        if (a.Trade == 0) return;
        // Pushing towards the other side keeps the trade speeding up.
        if ((a.Ask & 0x80) == 0) { if ((v.Stick[a.Ask] | 0x0C) == 0x0D) return; }
        else if ((v.Stick[cp] | 0x0C) == 0x0E) return;
        v.Timers[1] = 5;
    }

    /// <summary>sub_9926 (tournament): a button press asks for a collusion; when, within 10 frames,
    /// at least two players ask and there are a buyer and a seller among them, it starts.</summary>
    static void CollusionRequests(GameState g, Vbi v, Auction a)
    {
        if (g.Level != 2) return;
        for (int x = 3; x >= 0; x--)
        {
            byte e = a.Track[x];
            if ((e & 0x80) == 0 && e != 0 && v.Button[x] == 0)
            {
                a.RequestWindow = 10;
                a.Requests++;
                a.InOffer[x] = 1;
                a.Track[x] = 0x9C;
            }
            a.Track[x]++;
            if ((a.Track[x] & 0x80) == 0) a.Track[x] = v.Button[x];
        }
        if (a.RequestWindow == 0)
        {
            a.Requests = 0;
            for (int x = 0; x < 4; x++) a.InOffer[x] = 0;
            return;
        }
        a.RequestWindow--;
        if (a.Requests < 2) return;
        bool buyer = false, seller = false;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] != 0) { if (a.Seller[x] == 0) buyer = true; else seller = true; }
        if (buyer && seller) a.Offer++;
    }

    /// <summary>sub_91EA for goods: a collusion begins. Only the players who asked stay in, the store
    /// leaves, and the time bar is set to 50 steps.</summary>
    static IEnumerable CollusionStart(Game game, Auction a)
    {
        if (a.Offer == 0 || a.OfferSetUp != 0) yield break;
        a.OfferSetUp++;
        game.Trace?.Invoke("collusion");
        yield return LandAuction.Freeze(a);
        game.Vbi.Handler = null;
        for (int x = 3; x >= 0; x--) a.SavedOut[x] = a.Out[x];
        a.SavedStoreBuys = a.StoreBuys;
        a.SavedFlag43 = a.Flag43;
        a.SavedTime = a.Time;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] == 0) { a.Out[x] = 1; a.Mask[x] = 0x0F; }
        a.Flag43 = 1;
        a.StoreBuys = 0xFF;
        game.Emit(new MessageEvent("COLLUSION"));
        yield return game.Beeps(2);
        for (int i = 0; i < 3; i++)
        {
            yield return game.WaitJiffy(20);
            yield return game.WaitJiffy(20);
        }
        a.Time = 0x32;
        game.Vbi.Handler = () => Vbl(game, a);
    }

    /// <summary>sub_92B9 for goods: the collusion is over; everyone and the store come back.</summary>
    static IEnumerable CollusionEnd(Game game, Auction a)
    {
        game.Vbi.Handler = null;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] == 0) a.Out[x] = a.SavedOut[x];
        a.StoreBuys = a.SavedStoreBuys;
        a.Flag43 = a.SavedFlag43;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] != 0)
            {
                a.Pos[x] = a.Seller[x] != 0 ? (byte)0x52 : (byte)0xAE;
            }
        a.Offer = 0; a.OfferSetUp = 0; a.Requests = 0; a.RequestWindow = 0;
        for (int x = 3; x >= 0; x--) { a.InOffer[x] = 0; a.Track[x] = 0; }
        a.Time = a.SavedTime;
        a.Tick = 7;
        game.Vbi.Handler = () => Vbl(game, a);
        yield break;
    }

    /// <summary>animAuctionOrCollusionOver ($952A).</summary>
    static IEnumerable Over(Game game, Auction a)
    {
        game.Trace?.Invoke("over");
        yield return LandAuction.Freeze(a);
        if (a.Offer != 0)
        {
            game.Emit(new MessageEvent("COLLUSION IS OVER"));
            yield return game.Beeps(2);
        }
        else
        {
            game.Emit(new MessageEvent("AUCTION IS OVER"));
            yield return game.Beeps(4);
            yield return game.WaitJiffy(0x32);
        }
        yield return game.WaitJiffy(0x32);
    }

    /// <summary>The text buffer byte that $915E reads as "CPU buyer strapped" when the store is the
    /// buyer (plotsTemp3+$1A indexed by $FF lands in the notice line at $B546).</summary>
    const byte TextB546 = 0;

    const int UnitDrawFrames = 1;

    /// <summary>auctionBuySellAi9 ($9071): units change hands, one per timer period, faster while
    /// both sides keep pushing, until a side moves away or a notice stops the trade.</summary>
    static IEnumerable Trade(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = g.PlotsTemp3;
        game.Trace?.Invoke("rnd ai9");
        yield return LandAuction.Freeze(a);
        a.UnitsTraded = 0;
        byte cp = g.CurrentPlayer;
        byte buyerPos = 0, sellerPos = 0;
        if (a.Trade != 0x80)
        {
            a.Mask[cp] = 0x0D;
            buyerPos = a.Pos[cp];
        }
        if (a.Trade != 0xFF)
        {
            a.Mask[a.Ask] = 0x0E;
            sellerPos = a.Pos[a.Ask];
        }
        a.TradeLine = (byte)(buyerPos | sellerPos);
        a.MessageHold = 0; a.StickBuyer = 0; a.StickSeller = 0;
        bool tick = true;
        while (true)
        {
            if (tick && (a.Tick & 0x80) != 0)
            {
                TradeMasks(g, a);
                if ((g.MaxIndexCpu & 0x80) == 0) Ai12(game, a);
                a.TickDone = 0;
                while (a.TickDone == 0) yield return null;
                InZone(a);
                for (int x = 3; x >= 0; x--)
                    if (a.Out[x] == 0 && a.InZone[x] != 0) a.Bid[x] = LandAuction.PriceAt(g, a, a.Pos[x]);
                // Drawing the prices now and then runs past the next vertical blank.
                if (game.Race?.Invoke("slow tick") == true) yield return null;
            }
            bool ticked = tick && (a.Tick & 0x80) != 0;
            tick = true;
            // A stick pulled back in the vertical blank that also makes the tick due: now and then the
            // original sees the tick first.
            bool quit = (a.Trade != 0x80 && a.StickBuyer == 0x0D) || (a.Trade != 0xFF && a.StickSeller == 0x0E);
            if (quit && !ticked && (a.Tick & 0x80) != 0 && game.Race?.Invoke("quit late") == true) continue;
            // Or, when the trade timer runs out in it too, it trades a unit first.
            if (quit && v.Timers[2] == 0 && game.Race?.Invoke("unit first") == true) quit = false;
            if (quit && a.Trade != 0x80 && a.StickBuyer == 0x0D)
            {
                // sub_9758: the buyer steps away.
                game.Trace?.Invoke("rnd quitb");
                a.Pos[cp] += 2;
                a.Bid[cp] = LandAuction.PriceAt(g, a, a.Pos[cp]);
                yield return Notice(v, a);
                break;
            }
            if (quit && a.Trade != 0xFF && a.StickSeller == 0x0E)
            {
                // sub_977E: the seller steps away.
                game.Trace?.Invoke("rnd quits");
                a.Pos[a.Ask] -= 2;
                a.Bid[a.Ask] = LandAuction.PriceAt(g, a, a.Pos[a.Ask]);
                yield return Notice(v, a);
                break;
            }
            if (v.Timers[2] != 0)
            {
                // The original polls the movement tick, the sticks and the timer in turn. The vertical
                // blank nearly always lands between the tick check and the timer check, so after it the
                // sticks and the timer are seen first.
                if ((a.Tick & 0x80) != 0) continue;
                yield return null;
                tick = false;
                continue;
            }
            // Both the timer and the movement tick are due: the original nearly always sees the
            // timer first.
            if ((a.Tick & 0x80) != 0 && game.Race?.Invoke("tick first") == true)
            {
                tick = true;
                continue;
            }
            Ai9a(game, a);
            Ai9b(game, a);
            // Drawing the new amounts runs past a vertical blank.
            yield return game.Frames(UnitDrawFrames);
            // Now and then the drawing runs past a second one.
            if (game.Race?.Invoke("slow unit") == true) yield return null;
            if ((a.LogCount & 0x80) != 0)
            {
                v.Timers[2] = a.TradeDelay < 2 ? (byte)2 : a.TradeDelay;
                if (v.Timers[1] == 0 && a.TradeDelay != 0) a.TradeDelay -= 2;
                yield return game.WaitJiffy(1);
                continue;
            }
            game.Trace?.Invoke("rnd notices");
            yield return Notices(game, a);
            byte strapped = (cp & 0x80) == 0 ? t[0x1A + cp] : TextB546;
            if (strapped == 0)
            {
                var handler = v.Handler;
                v.Handler = null;
                v.SoundLeft = 0;
                game.Play(5);
                yield return game.WaitJiffy(0x14);
                v.Timers[0] = 0x1E;
                v.Handler = handler;
            }
            for (int y = a.LogCount; y >= 0; y--)
            {
                byte code = a.Log[y];
                if (code == 0xFF) continue;
                if (code == 0x80)
                {
                    if (g.IsCpu(cp)) continue;
                    a.Pos[cp] = a.High[cp];
                    a.Bid[cp] = LandAuction.PriceAt(g, a, a.Pos[cp]);
                }
                else
                {
                    a.Pos[a.Ask] = a.Low[a.Ask];
                    a.Bid[a.Ask] = LandAuction.PriceAt(g, a, a.Pos[a.Ask]);
                }
            }
            break;
        }
        game.Trace?.Invoke("rnd tradeend");
        a.LogCount = 0xFF;
        a.Trade = 0;
        a.TickDone = 1;
    }

    /// <summary>sub_9993: stick masks while a trade is under way, for everyone but the two traders.</summary>
    static void TradeMasks(GameState g, Auction a)
    {
        for (int x = 3; x >= 0; x--)
        {
            if (x == g.CurrentPlayer || x == a.Ask) continue;
            byte y;
            if (a.Out[x] != 0) y = 0x0F;
            else if (a.Seller[x] != 0)
                y = a.Pos[x] <= 0x52 ? (byte)0x0D : a.Pos[x] < a.TradeLine ? (byte)0x0C : (byte)0x0E;
            else if (a.Pos[x] <= a.TradeLine) y = 0x0D;
            else if (LandAuction.W(g.PlotsTemp3, Willing + 2 * x) < (ushort)(a.Bid[x] + a.Increment)) y = 0x0D;
            else y = a.Pos[x] < 0xAE ? (byte)0x0C : (byte)0x0E;
            a.Mask[x] = y;
        }
    }

    /// <summary>auctionBuySellAi9a ($8EAA): one unit moves from the seller (or the store) to the buyer
    /// (or the store). A seller with none left is out; a seller down to the requirement is noted.</summary>
    static void Ai9a(Game game, Auction a)
    {
        var g = game.State;
        int good = a.Good;
        int gi = good * 4;
        byte cp = g.CurrentPlayer;
        game.Trace?.Invoke($"unit {cp:X2} {a.Ask:X2} {a.Trade:X2}");
        if (a.Trade != 0x80)
        {
            g.GoodsPlayersNb[gi + cp]++;
            g.PlayersGoodHeld[cp]++;
            if (a.Needed != 0) a.Needed--;
            if (g.IsCpu(cp)) a.CpuUnits[cp]--;
            if (a.Trade == 0xFF)
            {
                if (--g.GoodsStoreNb[good] == 0)
                {
                    a.Flag43++;
                    AddLog(a, 0xFF);
                }
                a.UnitsTraded++;
                return;
            }
        }
        if ((a.Trade & 0x80) != 0 && a.Kind == 0 && g.GoodsStoreNb[good] != 0xFF) g.GoodsStoreNb[good]++;
        byte ask = a.Ask;
        byte left = --g.GoodsPlayersNb[gi + ask];
        g.PlayersGoodHeld[ask]++;
        if (a.Available != 0) a.Available--;
        if (g.IsCpu(ask)) a.CpuUnits[ask]--;
        if (left == 0)
        {
            a.Out[ask] = 1;
            AddLog(a, 1);
        }
        else if (good < 2 && left == g.PlayersGoodReq[ask]) AddLog(a, 2);
        a.UnitsTraded++;
    }

    static void AddLog(Auction a, byte code) => a.Log[++a.LogCount] = code;

    /// <summary>auctionBuySellAi9b ($8FD2): the cash, and the price for the good's new average. A buyer
    /// who cannot pay for one more is noted.</summary>
    static void Ai9b(Game game, Auction a)
    {
        var g = game.State;
        byte cp = g.CurrentPlayer;
        if (a.Trade != 0x80)
        {
            g.PlayersCash[cp] -= a.Bid[cp];
            LandAuction.Ai2(g, a);
            if (LandAuction.W(g.PlotsTemp3, Willing + 2 * cp) < a.Bid[cp]) AddLog(a, 0x80);
        }
        if (a.Trade != 0xFF) g.PlayersCash[a.Ask] += a.Bid[a.Ask];
        byte y = a.Trade == 0x80 ? a.Ask : cp;
        game.Emit(new GoodsTradedEvent(a.Trade == 0x80 ? (byte)0xFF : cp, a.Ask, a.Bid[y]));
        if (a.PriceCount == 0xFF) return;
        a.PriceCount++;
        a.PriceSum += a.Bid[y];
    }

    /// <summary>sub_93A9: the notices of a trade, one movement tick each. A CPU buyer out of cash stops
    /// buying; a seller down to the requirement is out in the beginner game and for CPUs.</summary>
    static IEnumerable Notices(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        yield return LandAuction.Freeze(a);
        for (int i = a.LogCount; i >= 0; i--)
        {
            a.MessageHold = 0x14;
            byte code = a.Log[i];
            if (code == 0xFF) game.Emit(new MessageEvent("STORE HAS RUN DRY!"));
            else if (code == 0x80)
            {
                byte cp = g.CurrentPlayer;
                if (g.IsCpu(cp))
                {
                    a.CpuUnits[cp] = 0;
                    g.PlotsTemp3[0x1A + cp] = 1;
                }
                else game.Emit(new MessageEvent("BUYER'S STRAPPED"));
                MessageOn(v, a);
            }
            else if (code == 2)
            {
                game.Emit(new MessageEvent("SELLER AT CRITICAL LEVEL"));
                if (g.Level == 0 || ((g.MaxIndexCpu & 0x80) == 0 && a.Ask <= g.MaxIndexCpu)) a.Out[a.Ask] = 1;
            }
            else game.Emit(new MessageEvent("SELLER'S RUN DRY!"));
            yield return Notice(v, a);
            v.Timers[0] = 0x28;
        }
    }

    /// <summary>sub_937B: a movement tick with everyone held, then the notice stays up.</summary>
    static IEnumerable Notice(Vbi v, Auction a)
    {
        yield return LandAuction.Freeze(a);
        a.MessageHold = 0x18;
        MessageOn(v, a);
    }

    /// <summary>sub_9387.</summary>
    static void MessageOn(Vbi v, Auction a)
    {
        a.MessageShown = 0;
        v.Timers[0] = 0x14;
    }

    /// <summary>auctionBuySellAi8 ($AF00, tournament, everyone buying): before the last round a CPU buys
    /// the whole store (20 units or fewer) if it can pay the top price for all of it.</summary>
    static void Ai8(GameState g, Auction a)
    {
        int good = a.Good;
        if (good >= 2 || g.NumRound == g.NbRounds) return;
        byte store = g.GoodsStoreNb[good];
        if (store >= 21) return;
        for (int x = g.MaxIndexCpu; x >= 0; x--)
        {
            if (store <= a.CpuUnits[x]) continue;
            ushort cost = (ushort)(a.PriceEnd * store);
            if (cost < LandAuction.W(g.PlotsTemp3, Willing + 2 * x)) a.CpuUnits[x] = store;
        }
    }

    /// <summary>auctionBuySellAi5 ($A94F, food and energy): the units the buyers lack and the units for
    /// sale (the store's and the sellers' surpluses, at most 255), then Ai6.</summary>
    static void Ai5(GameState g, Auction a)
    {
        int good = a.Good;
        if (good >= 2) return;
        a.Needed = 0;
        a.Available = g.GoodsStoreNb[good];
        for (int x = 3; x >= 0; x--)
        {
            if (a.Seller[x] == 0)
            {
                if (g.PlayersGoodShort[x] != 0) a.Needed += g.PlayersGoodGap[x];
            }
            else if (g.PlayersGoodShort[x] == 0)
            {
                int n = g.PlayersGoodGap[x] + a.Available;
                a.Available = n > 255 ? (byte)255 : (byte)n;
            }
        }
        Ai6(g, a);
    }

    /// <summary>auctionBuySellAi6 ($A999): each CPU's eagerness. Buyers: their requirement over what
    /// they hold, scaled by demand over supply; sellers: what they hold over their requirement, scaled
    /// by supply over demand, halved when there are a few CPU sellers with small surpluses and supply
    /// is short. 255 when a divisor is 0 or the result is above 255.</summary>
    static void Ai6(GameState g, Auction a)
    {
        int gi = a.Good * 4;
        byte few = 0;
        bool check = a.Flag43 != 0 ? a.Needed != 0 : (byte)(a.Needed >> 1) >= g.GoodsStoreNb[a.Good];
        if (check)
        {
            int sellers = 0;
            bool small = true;
            for (int y = g.MaxIndexCpu; y >= 0; y--)
            {
                if (a.Seller[y] == 0) continue;
                sellers++;
                if (g.PlayersGoodGap[y] >= 6) { small = false; break; }
            }
            if (small && sellers != 0) few = 1;
        }
        a.FewSellers = few;
        for (int x = g.MaxIndexCpu; x >= 0; x--)
        {
            int r;
            if (a.Seller[x] == 0)
            {
                byte held = g.GoodsPlayersNb[gi + x];
                if (held == 0 || a.Available == 0) r = 255;
                else
                {
                    ushort n = (ushort)(Div16((ushort)(g.PlayersGoodReq[x] * 125), held) * a.Needed);
                    r = Div16(n, a.Available);
                }
            }
            else
            {
                byte req = g.PlayersGoodReq[x];
                if (req == 0 || a.Needed == 0) r = 255;
                else
                {
                    ushort n = (ushort)(Div16((ushort)(g.GoodsPlayersNb[gi + x] * 15), req) * a.Available);
                    r = Div16(n, a.Needed);
                }
            }
            if (r > 255) r = 255;
            a.Share[x] = (byte)r;
            if (a.Seller[x] != 0 && a.FewSellers != 0) a.Share[x] >>= 1;
        }
    }

    /// <summary>auctionBuySellAi12 ($AB79): the CPUs' sticks. During a collusion they stand still.
    /// When everyone buys, a CPU that still wants units climbs to the store's line; when everyone
    /// sells, a CPU with units to sell comes down. Otherwise buyers climb, hold or step back at random
    /// against their eagerness, and sellers come down the same way, selling more of their surplus
    /// while the price is 200 or more.</summary>
    static void Ai12(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = g.PlotsTemp3;
        var rng = game.Rng;
        int good = a.Good;
        if (a.Offer != 0)
        {
            for (int x = g.MaxIndexCpu; x >= 0; x--) v.Stick[x] = 0x0F;
            return;
        }
        if (a.Trade == 0)
        {
            byte f3 = a.CpuBound;
            int r = (f3 & 0x80) != 0 ? f3 - 2 : f3 - 3;
            a.CpuBound = r < 0 ? (byte)5 : (byte)r;
        }
        if (a.Mix == 0xFF)
        {
            for (int x = g.MaxIndexCpu; x >= 0; x--)
            {
                byte y = 0x0F;
                if (good < 2)
                {
                    if (a.Out[x] != 0) a.Mask[x] = 0x0D;
                    if (a.CpuUnits[x] == 0) { if (a.Pos[x] < 0x56) y = 0x0D; }
                    else if (a.Pos[x] != 0x54) y = 0x0E;
                }
                v.Stick[x] = (byte)(y | a.Mask[x]);
            }
            return;
        }
        if (a.Mix == 0)
        {
            for (int x = g.MaxIndexCpu; x >= 0; x--)
            {
                byte y = 0x0F;
                if (a.Out[x] == 0)
                {
                    if (a.CpuUnits[x] != 0) y = 0x0D;
                    else if (a.Pos[x] >= 0x9A) y = 0x0E;
                }
                v.Stick[x] = y;
            }
            return;
        }
        for (int x = g.MaxIndexCpu; x >= 0; x--)
        {
            byte? stick = a.Seller[x] == 0 ? BuyerStick(x) : SellerStick(x);
            v.Stick[x] = stick ?? (byte)(0x0F | a.Mask[x]);
        }

        // Each returns the stick as the code leaves it: a value ORed with the mask, or a bare value.
        byte? BuyerStick(int x)
        {
            if (a.Out[x] != 0) a.Mask[x] = 0x0D;
            byte y = 0x0F;
            if (good >= 2)
            {
                if (a.CpuUnits[x] == 0) { if (a.Pos[x] != 0xAE) y = 0x0D; }
                else if (a.Pos[x] != 0x9A) y = 0x0E;
                return (byte)(y | a.Mask[x]);
            }
            if (a.Flag43 != 0)
            {
                byte all = 1;
                for (int s = 3; s >= 0; s--)
                    if (a.Seller[s] != 0) all &= a.Out[s];
                if (all != 0) return (byte)(0x0F | a.Mask[x]);
            }
            if (a.CpuUnits[x] == 0) return (byte)((a.Pos[x] != 0xAE ? 0x0D : 0x0F) | a.Mask[x]);
            if (t[0x22 + x] != 0) return (byte)(0x0F | a.Mask[x]);
            if (a.StoreBuys != 0 && rng.NextByte() < 5)
            {
                t[0x22 + x]++;
                return (byte)(0x0F | a.Mask[x]);
            }
            if (a.Pos[x] >= 0x9B) return (byte)(0x0E | a.Mask[x]);
            return (byte)((rng.Below(a.CpuBound) >= a.Share[x] ? 0x0F : 0x0E) | a.Mask[x]);
        }

        byte? SellerStick(int x)
        {
            bool ask = false; // at $AC85
            if (a.Out[x] != 0) ask = true;
            else if (good >= 2)
            {
                if (a.CpuUnits[x] != 0) return 0x0D;
                ask = true;
            }
            while (true)
            {
                if (ask)
                {
                    if (a.Pos[x] != 0x52) return (byte)(0x0E | a.Mask[x]);
                    if (good >= 2 || g.NumRound == g.NbRounds || g.PlayersGoodGap[x] == g.PlayersGoodHeld[x] || a.HighBid < 200)
                    {
                        a.Out[x] = 1;
                        return null;
                    }
                    a.CpuUnits[x] = (byte)(g.PlayersGoodGap[x] - g.PlayersGoodHeld[x]);
                }
                // $ACBF
                if (a.CpuUnits[x] == 0) { ask = true; continue; }
                if (t[0x22 + x] != 0) return null;
                if (a.FewSellers == 0)
                {
                    if (a.Pos[x] < 0x54) return 0x0D;
                }
                else if ((a.CpuBound & 0x80) != 0)
                {
                    if (a.Time >= 0x10) return null;
                    if ((rng.NextByte() & 0x80) != 0) return null;
                    a.CpuBound = 0x32;
                    return null;
                }
                return rng.Below(a.CpuBound) < a.Share[x] ? (byte)0x0D : null;
            }
        }
    }
}
