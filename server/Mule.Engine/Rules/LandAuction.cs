using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>
/// The working variables of one auction screen. Names follow the original zero page and $B3xx
/// variables they stand for (address in each comment).
/// </summary>
public sealed class Auction
{
    public byte Kind;                                   // $BC: 0 goods, 5 land
    public readonly byte[] Pos = new byte[4];           // $A3 player heights (smaller = higher)
    public readonly byte[] Seller = new byte[4];        // $B1B5 0 buyer, 1 seller
    /// <summary>$B309-$B310: <see cref="Out"/> and <see cref="Low"/>, also used as four words by the
    /// goods status bars (their target amounts), which leave values behind for the auction.</summary>
    public readonly byte[] B309 = new byte[8];
    public ByteView Out => new(B309, 0);                // $B309 1 = cannot bid
    public ByteView Low => new(B309, 4);                // $B30D highest point the player can afford
    public readonly byte[] High = new byte[4];          // $B311 start height
    public readonly byte[] Mask = new byte[4];          // $E3 stick mask (CPUs: also their stick)
    public readonly byte[] InZone = new byte[4];        // $9F
    public readonly byte[] InOffer = new byte[4];       // $97 private offer: the seller and the buyer offered to
    public readonly ushort[] Bid = new ushort[4];       // $B301
    public ushort PriceStart, PriceEnd;                 // $B31D, $B31F
    public ushort HighBid;                              // $B321
    public byte Limit = 0xAF;                           // $960C lowest height + 1
    public byte Top = 0x9A;                             // $8E0A/$8E43 height of the scale's base price
    public byte Increment;                              // $94
    public byte ScaleIndex;                             // $88 (word index * 2 into lastPriceStart)
    public byte Line = 0x54;                            // $44
    public byte PrevLeader;                             // $AD
    public byte SellerPlayer = 0xFF;                    // $F0 player selling the plot (private sale), $FF none
    public byte AtTop = 0xFF;                           // $49 leader holding the top line (shared with the sales counter)
    public byte TopPush;                                // $D8 the leader is pushing past the top this tick
    public byte SellerMove;                             // $EE seller asking to raise (1) or lower ($FF) the price
    public byte Time;                                   // $D3 time bar; negative = over
    public byte TimeLeft;                               // $DA frames to the next time bar step
    public byte TimeStop;                               // $DB nonzero = time bar held
    public byte Tick;                                   // $93 frames to the next movement tick
    public byte TickDone;                               // $AE set by the vertical blank on a movement tick
    public byte FrameDone;                              // $A9 set by the vertical blank every frame
    public byte FirstLoop;                              // $8D
    public byte Offer;                                  // $E1 private offer under way
    public byte OfferSetUp;                             // $E2
    public byte OfferBuyer;                             // $EF
    public byte OffersDone;                             // $B1B1
    public readonly byte[] SavedOut = new byte[4];      // $B217
    public byte SavedTime;                              // $B21D
    public byte Flag43;                                 // $43
    public byte PriceChanged;                           // $80 after the seller/price logic

    // Goods
    public byte Good;                                   // $91
    public byte Speed;                                  // $9A05 frames between status bar steps
    public byte StepTimer;                              // $AF
    public byte StepReady;                              // $D9
    public byte Flash;                                  // $DA status: surplus and shortage shown
    public readonly byte[] Role = new byte[4];          // $B1A9 side a CPU takes (1 seller)
    public readonly byte[] CpuUnits = new byte[4];      // $B487 units a CPU means to trade
    public byte Changed;                                // $AF a player switched side
    public bool CalledOff;                              // nothing to trade
}

/// <summary>A view of part of a byte array, for original variables that share memory.</summary>
public readonly struct ByteView
{
    readonly byte[] _a;
    readonly int _o;
    public ByteView(byte[] a, int offset) { _a = a; _o = offset; }
    public ref byte this[int i] => ref _a[_o + i];
}

/// <summary>Land auctions ($A2DA-$A893, $A6DA, $A727) and the CPU land bidding ($AD03-$AEFF).</summary>
public static class LandAuction
{
    static readonly byte[] Delta = { 2, 0xFE, 0, 0 }; // $427F (index $FF reads $437E = 0)

    // Vertical blanks the original's drawing runs past (measured on the scanline Atari; drawMap and the
    // auction screen both start with a VCOUNT sync, so the count is the same every time).
    const int DrawMapFrames = 2;

    /// <summary>landsAuction ($A6DA): up to <paramref name="count"/> colony plots, one after another,
    /// until one does not sell.</summary>
    public static IEnumerable LandsAuction(Game game, int count)
    {
        var g = game.State;
        ushort total = 0; // word_72
        var a = new Auction();
        a.AtTop = 0;      // byte_49 starts at 0 here; the auction reuses it
        while (true)
        {
            game.Vbi.Handler = null;
            byte plot = Prices.RandomFreePlot(g, game.Rng, out int free);
            if (free == 0) break;
            game.Emit(new MapShownEvent());
            yield return game.Frames(DrawMapFrames);
            yield return game.WaitJiffy(6);
            yield return PressButtonsToStart(game, plot);
            CalcPlotQualitiesForLandAuctionAi(g, game.Rng, plot);
            a = new Auction { Kind = 5, AtTop = a.AtTop };
            g.NbLandsToSellByPlayers = 0;
            yield return BuySell(game, a, plot, total, t => total = t);
            if ((g.CurrentPlayer & 0x80) != 0) break;
            g.PlotsPlayer[plot] = g.CurrentPlayer;
            game.Emit(new PlotGrantedEvent(plot, g.CurrentPlayer));
            if (--count <= 0) break;
        }
        LastBidFromAverage(g, total, a.AtTop);
        yield return AuctionIsOver(game);
        game.Vbi.Handler = null;
    }

    /// <summary>landsAuctionByPlayers ($A727): plots players put up for sale during their turns.</summary>
    public static IEnumerable LandsAuctionByPlayers(Game game)
    {
        var g = game.State;
        if (g.NbLandsToSellByPlayers == 0) yield break;
        ushort total = 0;
        var a = new Auction { AtTop = 0 };
        for (int i = 0; i < g.NbLandsToSellByPlayers; i++)
        {
            game.Vbi.Handler = null;
            game.Emit(new MapShownEvent());
            yield return game.Frames(DrawMapFrames);
            byte plot = g.LandsToSell[i];
            yield return PressButtonsToStart(game, plot);
            CalcPlotQualitiesForLandAuctionAi(g, game.Rng, plot);
            a = new Auction { Kind = 5, AtTop = a.AtTop };
            yield return BuySell(game, a, plot, total, t => total = t, i);
            if ((g.CurrentPlayer & 0x80) == 0)
            {
                g.PlotsPlayer[g.LandsToSell[i]] = g.CurrentPlayer;
                game.Emit(new PlotGrantedEvent(g.LandsToSell[i], g.CurrentPlayer));
            }
        }
        LastBidFromAverage(g, total, a.AtTop);
        game.Vbi.Handler = null;
        game.Emit(new MapShownEvent());
        yield return game.Frames(DrawMapFrames);
    }

    /// <summary>B2FB_72div49: the next opening bid = total of the sales / their count (when both are
    /// nonzero; the count is reset by every auction, so in practice the last sale's price stands).</summary>
    static void LastBidFromAverage(GameState g, ushort total, byte count)
    {
        if (total == 0 || count == 0) return;
        g.LastBidStartLa = (ushort)(total / count);
    }

    /// <summary>printAndWaitPlayersButtons2 ($4C01): the plot cursor flashes until every human has
    /// pressed, or the time runs out ($FF ticks, $40 with only CPUs).</summary>
    public static IEnumerable PressButtonsToStart(Game game, byte plot)
    {
        var g = game.State;
        var v = game.Vbi;
        game.Emit(new TitleEvent("YOUR STICK BUTTON TO START"));
        game.Emit(new MessageEvent("PRESS ALL PLAYER BUTTONS TO GO ON."));
        yield return game.WaitJiffy(6);
        v.InitButtons();
        v.SoundOnPress = 1;
        v.Timers[1] = g.MaxIndexCpu == 3 ? (byte)0x40 : (byte)0xFF;
        game.Emit(new GrantCursorEvent(plot));
        while (true)
        {
            yield return WaitButtonsTick(game, 5);
            if (v.Presses >= 4) break;
            v.SoundOnPress++;
            yield return WaitButtonsTick(game, 5);
            v.SoundOnPress++;
            if (v.Timers[1] == 0 || v.Presses >= 4) break;
        }
        v.SoundOnPress = 0;
        game.Emit(new GrantCursorHiddenEvent());
    }

    /// <summary>loc_30A2: waits <paramref name="ticks"/> timer ticks, cut short once every human has pressed.</summary>
    static IEnumerable WaitButtonsTick(Game game, byte ticks)
    {
        var g = game.State;
        var v = game.Vbi;
        v.Timers[0] = ticks;
        while (true)
        {
            if (v.Timers[0] == 0) { v.SoundOnPress = 0; yield break; }
            if (g.MaxIndexCpu != 3 && (byte)(g.MaxIndexCpu + 1 + v.Presses) >= 4)
            {
                v.Presses = 4;
                v.Timers[0] = 1;
            }
            yield return null;
        }
    }

    /// <summary>calcPlotQualitiesForLandAuctionAi ($AD03): each CPU's value for the plot, as round-1
    /// rating plus 0-3, times 8, times the rounds left. The loop runs once even with no CPUs (index $FF):
    /// it then rates the plot for "player $FF", using a random byte, and stores the value at $B52B,
    /// past plotsTemp3, where nothing reads it.</summary>
    public static void CalcPlotQualitiesForLandAuctionAi(GameState g, IRandomSource rng, byte plot)
    {
        Production.InitVarsFromMaps(g);
        byte cpu = g.MaxIndexCpu;
        do
        {
            g.AiMode = 2;
            if ((cpu & 0x80) != 0)
            {
                SetGoodsQualityForLandAuction(g);
                rng.NextByte();
            }
            else
            {
                byte q = CalcPlotQualityForLandAuctionAi(g, rng, cpu, plot);
                ushort v = (ushort)((ushort)(q * 8) * (byte)(g.NbRounds - g.NumRound));
                SetW(g.PlotsTemp3, 2 * cpu, v);
            }
            cpu--;
        } while ((cpu & 0x80) == 0);
    }

    static void SetGoodsQualityForLandAuction(GameState g)
    {
        g.GoodsQuality[0] = 3;
        g.GoodsQuality[1] = 2;
        g.GoodsQuality[2] = 6;
    }

    /// <summary>calcPlotQualityForLandAuctionAi ($3949): goods rated 3/2/6 (crystite as last computed),
    /// as in round 1, plus 0-3.</summary>
    public static byte CalcPlotQualityForLandAuctionAi(GameState g, IRandomSource rng, byte cpu, byte plot)
    {
        SetGoodsQualityForLandAuction(g);
        byte round = g.NumRound;
        g.NumRound = 1;
        byte q = Ai.CalcPlotExpAndQualityAi(g, cpu, plot, out _);
        g.NumRound = round;
        return (byte)(q + (rng.NextByte() & 3));
    }

    // plotsTemp3 layout used by the auction AI: +0 value (word per player), +8 cash to keep,
    // +$11 most the player will pay, +$1A, +$1E, +$22 per-player flags.
    const int Value = 0, Keep = 8, Willing = 0x11, Quit = 0x1E;

    internal static ushort W(byte[] t, int i) => (ushort)(t[i] | (t[i + 1] << 8));
    internal static void SetW(byte[] t, int i, ushort v) { t[i] = (byte)v; t[i + 1] = (byte)(v >> 8); }

    /// <summary>ai1 ($AD4B) for a land auction: each CPU keeps 400-655 in hand.</summary>
    static void Ai1Land(GameState g, IRandomSource rng)
    {
        for (int cpu = g.MaxIndexCpu; cpu >= 0; cpu--)
        {
            int keep = rng.NextByte() + 144 + 256;
            SetW(g.PlotsTemp3, Keep + 2 * cpu, (ushort)keep);
        }
    }

    /// <summary>ai2 ($AE6B): the most each player will pay. Humans: their cash. CPUs: cash minus what they
    /// keep (or the opening price when 4 times it is in hand, else 1), at most their value of the plot.</summary>
    internal static void Ai2(GameState g, Auction a)
    {
        var t = g.PlotsTemp3;
        for (int p = 3; p >= 0; p--)
        {
            int x = 2 * p;
            if (!g.IsCpu(p)) { SetW(t, Willing + x, g.PlayersCash[p]); continue; }
            ushort cash = g.PlayersCash[p];
            ushort keep = W(t, Keep + x);
            SetW(t, Willing + x, (ushort)(cash - keep));
            if (cash < keep)
            {
                if (a.Kind != 0 && (a.PriceStart >> 8) == 0 && (cash >> 2) >= a.PriceStart)
                    SetW(t, Willing + x, (byte)a.PriceStart);
                else
                    SetW(t, Willing + x, 1);
            }
            if (a.Kind != 0 && W(t, Value + x) < W(t, Willing + x))
                SetW(t, Willing + x, W(t, Value + x));
        }
    }

    /// <summary>sub_8E07: the price at a height.</summary>
    internal static ushort PriceAt(GameState g, Auction a, byte pos)
    {
        byte d = (byte)((byte)(a.Top - pos) >> 1);
        if (a.Increment != 1) d = (byte)(d << 2);
        return (ushort)(g.LastPriceStart[a.ScaleIndex >> 1] + d);
    }

    /// <summary>sub_8E27: the height of a price.</summary>
    internal static byte PosOf(GameState g, Auction a, ushort price)
    {
        ushort d = (ushort)(price - g.LastPriceStart[a.ScaleIndex >> 1]);
        if (a.Increment != 1) d >>= 2;
        return (byte)(a.Top - (byte)(d << 1));
    }

    /// <summary>auctionBuySellAi3 ($8F77): for buyers, whether they can bid and how high they can go.</summary>
    internal static void Ai3(GameState g, Auction a)
    {
        var t = g.PlotsTemp3;
        for (int p = 3; p >= 0; p--)
        {
            if (a.Seller[p] != 0) continue;
            ushort willing = W(t, Willing + 2 * p);
            if (willing >= a.PriceEnd && a.Line != 0)
                a.Low[p] = a.Line;
            else
            {
                ushort r = a.Increment == 1 ? willing : (ushort)(willing & 0xFFFC);
                if (r < a.PriceStart)
                {
                    a.Out[p] = 1;
                    continue;
                }
                a.Low[p] = PosOf(g, a, r);
            }
            if (a.Kind != 0) a.Out[p] = 0;
        }
    }

    /// <summary>sub_8E4B: stick masks from what each player can afford.</summary>
    internal static void Masks(Auction a)
    {
        for (int x = 3; x >= 0; x--)
        {
            byte y;
            if (a.Out[x] != 0) y = 0x0F;
            else if (a.Pos[x] <= a.Low[x]) y = 0x0D;
            else if (a.Pos[x] < a.High[x]) y = 0x0C;
            else y = 0x0E;
            a.Mask[x] = y;
        }
    }

    /// <summary>sub_9845: stick masks while the leader holds the top line: a buyer who cannot pay one
    /// more step can only go down.</summary>
    internal static void MasksAtTop(GameState g, Auction a)
    {
        for (int x = 3; x >= 0; x--)
        {
            byte y;
            if (a.Out[x] != 0) y = 0x0F;
            else if (a.Seller[x] != 0)
                y = a.Pos[x] <= a.Low[x] ? (byte)0x0D : a.Pos[x] < a.High[x] ? (byte)0x0C : (byte)0x0E;
            else if (W(g.PlotsTemp3, Willing + 2 * x) < (ushort)(a.Bid[x] + a.Increment)) y = 0x0D;
            else y = a.Pos[x] < a.High[x] ? (byte)0x0C : (byte)0x0E;
            a.Mask[x] = y;
        }
    }

    /// <summary>auctionBuySellLandAuctionAi8 ($ADC4): each CPU's stick. A CPU that is not leading pushes
    /// up until it quits. The leader steps down to the opening price when nobody is near, holds or pushes
    /// up against a close rival, and late in a dear auction may quit against a rival who keeps pushing.</summary>
    static void Ai8(GameState g, IRandomSource rng, Game game, Auction a)
    {
        var t = g.PlotsTemp3;
        var v = game.Vbi;
        byte dear = a.HighBid >= 1000 ? (byte)1 : (byte)0;
        for (int me = g.MaxIndexCpu; me >= 0; me--)
        {
            byte y = 0x0E;
            if (me != g.CurrentPlayer)
            {
                if (t[Quit + me] != 0) y = 0x0D;
            }
            else if (t[Quit + me] != 0) y = 0x0D;
            else
            {
                byte myPos = a.Pos[me];
                byte closest = 0xFF;
                int rival = -1;
                bool decided = false;
                for (int x = 3; x >= 0 && !decided; x--)
                {
                    if (x == me) continue;
                    byte dist = (byte)(a.Pos[x] - myPos);
                    if (dist >= 5) continue;
                    if (dist < closest) { closest = dist; rival = x; }
                    if ((v.Stick[x] | a.Mask[x]) != 0x0E) continue;
                    // A rival is pushing up right behind.
                    decided = true;
                    if (dear == 0 || a.Time >= 4 || (rng.NextByte() & 0x80) != 0) y = 0x0E;
                    else { t[Quit + me]++; y = 0x0D; }
                }
                if (!decided)
                {
                    if (rival < 0) y = myPos == 0x90 ? (byte)0x0F : (byte)0x0D;
                    else
                    {
                        bool quit = !g.IsCpu(rival) && dear != 0 && a.Time < 4 && (rng.NextByte() & 0x80) == 0;
                        if (quit) { t[Quit + me]++; y = 0x0D; }
                        else y = closest < 3 ? (byte)0x0E : (byte)0x0F;
                    }
                }
            }
            v.Stick[me] = (byte)(y | a.Mask[me]);
        }
    }

    /// <summary>v08 ($9C91): the land auction's vertical blank work.</summary>
    static void Vbl(GameState g, Game game, Auction a)
    {
        var v = game.Vbi;
        if (a.FrameDone == 0) a.FrameDone++;
        if (a.TickDone == 0 && (a.Tick & 0x80) != 0)
        {
            a.Tick += 8;
            Move(g, v, a);
            a.TickDone = 1;
        }
        if (a.TimeStop == 0 && a.TimeLeft == 0)
        {
            a.TimeLeft = g.AuctionTimeReload;
            a.Time--;
            if (a.Time == 0)
            {
                a.Time--;
                a.TimeStop++;
            }
        }
        a.Tick--;
        if (a.TimeLeft != 0) a.TimeLeft--;
        byte s = 0x0F;
        for (int x = 3; x >= 0; x--)
            if (a.InZone[x] != 0) s &= v.Stick[x];
        byte reload = g.LevelAuctionTimeWait;
        if ((s | 0x0C) == 0x0F && (a.SellerPlayer & 0x80) != 0) reload >>= 2;
        g.AuctionTimeReload = reload;
    }

    /// <summary>sub_9595: one movement tick (every 8 frames), 2 height units per tick.</summary>
    internal static void Move(GameState g, Vbi v, Auction a)
    {
        a.TopPush = 0;
        a.SellerMove = 0;
        if ((a.SellerPlayer & 0x80) == 0 && (v.Stick[a.SellerPlayer] | a.Mask[a.SellerPlayer]) != 0x0F)
            for (int x = 3; x >= 0; x--)
                if (x != a.SellerPlayer) a.Mask[x] = 0x0F;
        if ((a.AtTop & 0x80) == 0 && (v.Stick[a.AtTop] | a.Mask[a.AtTop]) == 0x0E) a.TopPush++;
        for (int x = 3; x >= 0; x--)
        {
            if (a.SellerMove != 0) continue;
            byte s = (byte)(v.Stick[x] | a.Mask[x]);
            if (a.Kind != 0 && x == a.SellerPlayer)
            {
                if (s == 0x0E) a.SellerMove++;
                else if (s == 0x0D) a.SellerMove--;
                continue;
            }
            int y;
            if (a.TopPush != 0)
            {
                if (s == 0x0E) continue;
                if (a.Seller[x] == 0 && a.Pos[x] != 0) { Step(a, x, 0); continue; }
            }
            if (s == 0x0F) continue;
            y = (s & 3) - 1;
            Step(a, x, y);
        }
    }

    internal static void Step(Auction a, int x, int y)
    {
        byte d = y < 0 ? (byte)0 : Delta[y];
        byte n = (byte)(a.Pos[x] + d);
        if (n >= a.Limit) return;
        if (a.Seller[x] == 0 && n < 0x54) return;
        a.Pos[x] = n;
    }

    /// <summary>sub_96B9: everyone's stick held still, then one movement tick.</summary>
    internal static IEnumerable Freeze(Auction a)
    {
        for (int x = 0; x < 4; x++) a.Mask[x] = 0x0F;
        a.Tick = 0xFF;
        a.TickDone = 0;
        while (a.TickDone == 0) yield return null;
    }

    /// <summary>sub_9899: raises the whole price scale by one increment when the leader pushes at the top.</summary>
    internal static void RaiseScale(GameState g, Auction a)
    {
        a.AtTop = 0xFF;
        a.PriceChanged = 0;
        if (a.TopPush == 0) return;
        g.LastPriceStart[a.ScaleIndex >> 1] += a.Increment;
        a.PriceStart += a.Increment;
        a.PriceEnd += a.Increment;
        a.TopPush = 0;
        a.PriceChanged++;
    }

    /// <summary>auctionBuySell ($9D2A) for land, then auctionBuySellLandAuction ($A2DA).</summary>
    static IEnumerable BuySell(Game game, Auction a, byte plot, ushort total, Action<ushort> setTotal, int sale = -1)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = g.PlotsTemp3;
        // $9D2A-$9F54, the parts that matter for land.
        for (int x = 0; x < 4; x++) a.Seller[x] = 0;
        // The screen is built after a VCOUNT sync (sub_5E1B at $9D92, just before a vertical blank)
        // and runs past three vertical blanks: before initPlayersButtons, before $A2DA's land setup
        // ends, and before the closing initPlayersButtons.
        yield return null;
        v.InitButtons();
        for (int x = 3; x >= 0; x--)
        {
            a.Out[x] = 0; a.InOffer[x] = 0; a.InZone[x] = 0;
            t[0x1A + x] = 0; t[0x1E + x] = 0; t[0x22 + x] = 0;
        }
        a.TickDone = 0xFF; a.SellerPlayer = 0xFF; a.AtTop = 0xFF; a.FrameDone = 0xFF;
        v.Timers[0] = 1;
        a.Time = 0x46;
        a.Limit = 0xAF; a.Top = 0x9A;
        a.Tick = 0; a.Offer = 0; a.OfferSetUp = 0; a.Flag43 = 0;
        if ((g.MaxIndexCpu & 0x80) == 0) Ai1Land(g, game.Rng);
        a.Increment = 4;     // $4274[5]
        a.ScaleIndex = 10;
        game.Emit(new TitleEvent("LAND AUCTION"));
        yield return null;

        // $A2DA
        if (g.LastBidStartLa == 0)
        {
            g.LastPriceStart[5] = 112;
            g.LastBidStartLa = 160;
        }
        a.PriceStart = g.LastBidStartLa;
        g.LastPriceStart[5] = (ushort)(a.PriceStart - 48);
        Ai2(g, a);
        a.Limit = 0xA9;
        a.Top = 0xA8;
        for (int x = 3; x >= 0; x--) { a.High[x] = 0xA8; a.Pos[x] = 0xA8; }
        g.CurrentPlayer = 0xFF;
        a.AtTop = 0xFF;
        a.SellerPlayer = 0xFF;
        a.FirstLoop = 0; a.SellerMove = 0; a.TimeLeft = 0; a.TimeStop = 0;
        a.Line = 0x54;
        a.PriceEnd = PriceAt(g, a, 0x54);
        if (g.NbLandsToSellByPlayers != 0)
        {
            byte seller = g.PlotsPlayer[g.LandsToSell[sale]];
            a.Seller[seller] = 1;
            a.Out[seller] = 1;
            a.SellerPlayer = seller;
            a.Pos[seller] = 76;
            ResetOffer(a);
        }
        yield return null;
        v.InitButtons();
        game.Emit(new AuctionStartedEvent(plot, a.PriceStart));
        v.Handler = () => Vbl(g, game, a);
        v.Timers[3] = 10;

        bool skipToMasks = false;
    loop:
        while (true)
        {
            if (!skipToMasks)
            {
                Ai3(g, a);
                if (a.Offer != 0)
                    for (int x = 3; x >= 0; x--)
                        a.Out[x] = a.InOffer[x] != 0 && x != a.SellerPlayer ? (byte)0 : (byte)1;
                Masks(a);
            }
            if (skipToMasks || a.Offer == 0)
                if ((a.SellerPlayer & 0x80) == 0) a.Mask[a.SellerPlayer] = 0x0C;
            skipToMasks = false;
            if ((g.MaxIndexCpu & 0x80) == 0) Ai8(g, game.Rng, game, a);
            a.TickDone = 0;
            while (a.TickDone == 0) yield return null;

            yield return SellerAndPrice(game, a);
            if (a.PriceChanged != 0) game.Emit(new AuctionPriceEvent(a.PriceStart));
            // sub_97FB
            for (int x = 3; x >= 0; x--)
                a.InZone[x] = a.Seller[x] == 0 ? (a.Pos[x] < 0x9B ? (byte)1 : (byte)0) : (a.Pos[x] >= 0x54 ? (byte)1 : (byte)0);
            for (int x = 3; x >= 0; x--)
                if (a.Out[x] == 0 && a.InZone[x] != 0) a.Bid[x] = PriceAt(g, a, a.Pos[x]);
            a.HighBid = 0;
            if (g.NbLandsToSellByPlayers != 0) a.Bid[a.SellerPlayer] = 0;
            int start = (g.CurrentPlayer & 0x80) == 0 ? g.CurrentPlayer : game.Rng.NextByte() & 3;
            int i = start;
            do
            {
                if (a.Out[i] == 0 && !(g.NbLandsToSellByPlayers != 0 && i == a.SellerPlayer) && a.Pos[i] < 0x91
                    && a.Bid[i] > a.HighBid)
                {
                    a.HighBid = a.Bid[i];
                    g.CurrentPlayer = (byte)i;
                }
                i = (i + 1) & 3;
            } while (i != start);
            if (a.HighBid < a.PriceStart) g.CurrentPlayer = 0xFF;
            a.FrameDone = 0;
            while (a.FrameDone == 0) yield return null;

            if (a.FirstLoop == 0)
            {
                a.PrevLeader = g.CurrentPlayer;
                a.FirstLoop++;
            }
            else
            {
                byte cp = g.CurrentPlayer;
                if ((cp & 0x80) == 0 && a.Pos[cp] == 0x54 && v.Stick[cp] != 0x0E)
                {
                    MasksAtTop(g, a);
                    int s0 = game.Rng.NextByte() & 3;
                    int x = s0;
                    do
                    {
                        if (x != cp && a.Out[x] == 0 && a.Pos[x] < 0x55 && (v.Stick[x] | a.Mask[x]) == 0x0E)
                        {
                            g.CurrentPlayer = (byte)x;
                            break;
                        }
                        x = (x + 1) & 3;
                    } while (x != s0);
                }
                a.PrevLeader = g.CurrentPlayer;
            }
            game.Emit(new AuctionStateEvent(a.Pos.ToArray(), g.CurrentPlayer, a.HighBid, a.Time));

            if ((a.Time & 0x80) != 0) break;
            bool end = false;
            yield return PrivateOffer(game, a, () => end = true);
            if (end) break;
            if (a.Offer == 0 && (g.CurrentPlayer & 0x80) == 0 && a.Pos[g.CurrentPlayer] == 0x54)
            {
                a.AtTop = g.CurrentPlayer;
                MasksAtTop(g, a);
                skipToMasks = true;
            }
            continue;
        }

        // $A5A6: the auction is over
        yield return Freeze(a);
        if (a.Offer != 0)
        {
            yield return Freeze(a);
            yield return game.Beeps(2);
            yield return game.WaitJiffy(50);
            yield return EndOffer(game, a);
            yield return Freeze(a);
            if (a.OffersDone != 0)
            {
                // Every buyer passed on the offer: back into the auction ($A5BB).
                ResetOffer(a);
                a.TimeStop = 0;
                game.Emit(new AuctionPriceEvent(a.PriceStart));
                skipToMasks = false;
                goto loop;
            }
        }
        yield return Finish(game, a, total, setTotal);
    }

    static IEnumerable Finish(Game game, Auction a, ushort total, Action<ushort> setTotal)
    {
        var g = game.State;
        a.TimeStop++;
        game.Emit(new AuctionEndedEvent(g.CurrentPlayer, (g.CurrentPlayer & 0x80) == 0 ? a.Bid[g.CurrentPlayer] : (ushort)0));
        yield return game.WaitJiffy(0x78);
        if ((a.SellerPlayer & 0x80) != 0)
        {
            if ((g.CurrentPlayer & 0x80) != 0)
            {
                a.PriceStart = (ushort)((a.PriceStart >> 1) + 52);
                g.LastBidStartLa = a.PriceStart;
            }
            else g.LastBidStartLa = a.Bid[g.CurrentPlayer];
            total += g.LastBidStartLa;
            setTotal(total);
            a.AtTop++;
            NextOpeningBid(g, a);
        }
        if ((g.CurrentPlayer & 0x80) == 0)
        {
            byte cp = g.CurrentPlayer;
            g.PlayersCash[cp] -= a.Bid[cp];
            if (g.NbLandsToSellByPlayers != 0)
            {
                // The plot still belongs to the seller here.
                byte seller = a.SellerPlayer;
                g.PlayersCash[seller] += a.Bid[cp];
            }
        }
        yield return game.WaitJiffyLevel();
    }

    /// <summary>B2FB_94: scale base = last bid - 27 steps (at least one step), opening bid = base + 12 steps.</summary>
    static void NextOpeningBid(GameState g, Auction a)
    {
        int inc = a.Increment;
        int lps = g.LastBidStartLa - 27 * inc;
        if (lps < 0 || lps < inc) lps = inc;
        g.LastPriceStart[5] = (ushort)lps;
        g.LastBidStartLa = (ushort)(lps + 12 * inc);
    }

    static void ResetOffer(Auction a)
    {
        a.OffersDone = 0;
        a.OfferBuyer = a.SellerPlayer == 0 ? (byte)1 : (byte)0;
    }

    /// <summary>auctionBuySellLandAuctionAi7 ($A770): the seller's button opens a private offer; the
    /// seller's stick moves the asking price.</summary>
    static IEnumerable SellerAndPrice(Game game, Auction a)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = g.PlotsTemp3;
        byte f0 = a.SellerPlayer;
        if ((f0 & 0x80) == 0 && a.Offer == 0 && v.Timers[3] == 0)
        {
            v.Timers[3] = 5;
            if (v.Button[f0] == 0)
            {
                v.Sound = 0;
                bool any = false;
                for (int y = 3; y >= 0; y--)
                    if (y != f0 && W(t, Willing + 2 * y) >= a.PriceStart) { any = true; break; }
                if (any)
                {
                    a.InOffer[f0] = 1;
                    a.Offer++;
                    a.TimeStop = 1;
                    yield return StartOffer(game, a);
                    for (int x = 3; x >= 0; x--)
                        if (a.Seller[x] == 0 && a.Pos[x] != 0xA8) a.Pos[x] = 0xA8;
                    while (true)
                    {
                        if (W(t, Willing + 2 * a.OfferBuyer) >= a.PriceStart)
                        {
                            byte b = a.OfferBuyer;
                            a.Out[b] = 0;
                            a.InOffer[b] = 1;
                            a.Pos[b] = 0x90;
                            a.TimeStop--;
                            v.Timers[3] = 5;
                            break;
                        }
                        if (++a.OfferBuyer == 4) break;
                    }
                }
            }
        }
        // $A803
        a.PriceChanged = 0;
        a.AtTop = 0xFF;
        if (a.Offer != 0) yield break;
        if (a.TopPush != 0) { RaiseScale(g, a); yield break; }
        if (a.SellerMove == 0) yield break;
        for (int x = 3; x >= 0; x--)
            if (a.Seller[x] == 0 && a.Pos[x] != 0xA8) a.Pos[x] = 0xA8;
        if ((a.SellerMove & 0x80) == 0)
        {
            a.TopPush++;
            a.Increment = 0x10;
            RaiseScale(g, a);
            a.Increment = 4;
            yield break;
        }
        int lps = g.LastPriceStart[5] - 16;
        if (lps < 0 || (lps >> 8 == 0 && (byte)lps < 4)) lps = 4;
        g.LastPriceStart[5] = (ushort)lps;
        a.PriceStart = (ushort)(lps + 48);
        a.PriceEnd = (ushort)(lps + 168);
        a.PriceChanged++;
    }

    /// <summary>sub_91EA: a private offer begins (screen and sound, everyone else held still).</summary>
    internal static IEnumerable StartOffer(Game game, Auction a)
    {
        if (a.Offer == 0 || a.OfferSetUp != 0) yield break;
        a.OfferSetUp++;
        yield return Freeze(a);
        var handler = game.Vbi.Handler;
        game.Vbi.Handler = null;
        for (int x = 3; x >= 0; x--) a.SavedOut[x] = a.Out[x];
        a.SavedTime = a.Time;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] == 0) { a.Out[x] = 1; a.Mask[x] = 0x0F; }
        a.Flag43 = 1;
        game.Emit(new MessageEvent("COLLUSION"));
        yield return game.Beeps(2);
        for (int i = 0; i < 3; i++)
        {
            yield return game.WaitJiffy(20);
            yield return game.WaitJiffy(20);
        }
        a.Time = 0x32;
        game.Vbi.Handler = handler;
    }

    /// <summary>sub_92B9: the private offer is over; everyone back.</summary>
    internal static IEnumerable EndOffer(Game game, Auction a)
    {
        var handler = game.Vbi.Handler;
        game.Vbi.Handler = null;
        for (int x = 3; x >= 0; x--)
            if (a.InOffer[x] == 0) a.Out[x] = a.SavedOut[x];
        a.Offer = 0; a.OfferSetUp = 0;
        for (int x = 3; x >= 0; x--) a.InOffer[x] = 0;
        a.Time = a.SavedTime;
        a.Tick = 7;
        game.Vbi.Handler = handler;
        yield break;
    }

    /// <summary>sub_A893: during a private offer the seller's button passes the offer to the next buyer
    /// who can pay; after the last one the auction ends.</summary>
    static IEnumerable PrivateOffer(Game game, Auction a, Action end)
    {
        var g = game.State;
        var v = game.Vbi;
        if (a.Offer == 0 || v.Timers[3] != 0) yield break;
        v.Timers[3] = 5;
        if (v.Button[a.SellerPlayer] != 0) yield break;
        v.Sound = 0;
        byte b = a.OfferBuyer;
        a.Pos[b] = 0xA8;
        g.CurrentPlayer = 0xFF;
        a.InOffer[b]--;
        a.Out[b]++;
        while (true)
        {
            a.OfferBuyer++;
            if (a.OfferBuyer == a.SellerPlayer) a.OfferBuyer++;
            if (a.OfferBuyer == 4)
            {
                a.OffersDone++;
                end();
                yield break;
            }
            if (W(g.PlotsTemp3, Willing + 2 * a.OfferBuyer) < a.PriceStart) continue;
            byte n = a.OfferBuyer;
            a.Pos[n] = 0x90;
            a.InOffer[n]++;
            a.Out[n]--;
            yield break;
        }
    }

    /// <summary>animAuctionOrCollusionOver2 ($952D) after the colony's land auctions.</summary>
    static IEnumerable AuctionIsOver(Game game)
    {
        game.Emit(new MessageEvent("AUCTION IS OVER"));
        yield return game.Beeps(4);
        yield return game.WaitJiffy(0x32);
        yield return game.WaitJiffy(0x32);
    }
}
