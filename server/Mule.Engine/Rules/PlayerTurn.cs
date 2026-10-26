using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>The development turns' working state (zero page and a few $B3xx bytes in the original).</summary>
public sealed class Turn
{
    public byte X = 0x7B, Y = 0x74;   // $9F, $A3 walker (player 0)
    public byte MuleX, MuleY;         // $A0, $A4 mule (player 1)
    public byte Map;                  // $AD: 0 town, 1 map
    public byte HasMule;              // $E0
    public byte Outfit;               // $BC: carried outfit / errand (1-4 job, 5 land office, 6 assay)
    public byte Building;             // $AF
    public byte Msg;                  // $C2 message to show
    public byte Mask = 0x0F;          // $E3 ORed into the stick
    public byte Phase;                // $A9 movement phase
    public byte Anim;                 // $AA
    public byte Toggle;               // $AB
    public byte Dx, Dy;               // $A7, $A8
    public byte NewX, NewY;           // $D9, $DA
    public byte Dir;                  // $DC
    public byte Time;                 // $D3 time bar pixels
    public byte Marked = 0xFF;        // $CB plot marked for the land office or assay
    public byte Exits;                // $91 times the store was left
    public byte EventShown;           // $B33D player event text: 1 scrolling, 2+ shown once
    public byte Ctrl;                 // $E7 controller index
    public byte TimeWait;             // $5642 playerTurnTimeWait
    public byte WarnTime = 0x1E;      // $B33A low-time warning threshold
    public byte Warn;                 // $B33C warning sound on
    public byte WampusTime;           // $8B8B
    public byte WampusX, WampusX2, WampusY, WampusVisible;
    public byte Flash, Flashes;       // $80, $88
    public bool Running;              // handler 4 ($C1 = 4)
    public bool MuleWalkOnly;         // handler $D ($5625 without the time bar and wampus)
    /// <summary>Player 0's sprite memory ($BC00) and position register: the map collisions come
    /// from them. Only the map drawing is kept: going to the map clears the page first.</summary>
    public readonly byte[] Pm0 = new byte[256];
    public byte Hpos0;
    /// <summary>P0PL/P0PF as the GTIA latched them over the frame being shown.</summary>
    public (byte Players, bool Playfield) Latch;
    public (byte Players, bool Playfield) Seen;
}

/// <summary>playersTurn ($6628): every player's development turn, in ranking order (reversed when the
/// store has fewer than 8 mules).</summary>
public static class PlayerTurn
{
    // Frames of drawing the original spends between its waits (measured; see the turn tests).
    const int TitleDrawFrames = 1;

    static readonly byte[] TopDoors = new byte[40];    // unk_1E00
    static readonly byte[] BottomDoors = new byte[40]; // unk_1E28
    static readonly byte[] DyTable = { 2, 0xFE, 0 };   // unk_56D9
    static readonly byte[] DxTable = { 2, 0xFE, 0 };   // $56D6
    static readonly byte[] VertAnim = { 1, 2, 0 };     // $54AF
    static readonly byte[] HorzAnim = { 0, 3, 6 };     // $54B2
    static readonly byte[] TownAnim = { 0, 0, 0, 0x30, 0x30, 0x30, 0x60, 0x90, 0xC0 }; // $54B5
    static readonly byte[] MapAnim = { 0, 0, 0, 0x10, 0x10, 0x10, 0x20, 0x30, 0x40 };   // $56DC
    static readonly byte[] GamblingBonus = { 50, 100, 150, 200 };
    static readonly ushort[] WampusBonus = { 100, 200, 300, 400 };

    static PlayerTurn()
    {
        TopDoors[10] = 4; TopDoors[16] = 3; TopDoors[22] = 2; TopDoors[28] = 1;
        BottomDoors[10] = 6; BottomDoors[15] = 5; BottomDoors[20] = 7;
        for (int i = 25; i < 30; i++) BottomDoors[i] = 8;
    }

    static ushort Price(GameState g, int outfit) => outfit == 8 ? g.MulePrice : outfit < 5 ? g.OutfitsPrice[outfit] : (ushort)0;

    public static IEnumerable Run(Game game)
    {
        var g = game.State;
        var v = game.Vbi;
        var t = new Turn();
        game.Turn = t;
        g.NbLandsToSellByPlayers = 0;
        int order = 0, step = 1;
        if (g.GoodsStoreNb[4] < 8) { order = 3; step = -1; }
        while (true)
        {
            byte cp = g.OrdersPlayer[order];
            g.CurrentPlayer = cp;
            game.Trace?.Invoke($"player {cp}");
            // Drawing the money can run past the vertical blank before the walker is placed.
            if (game.Race?.Invoke("player late") == true) yield return null;
            t.X = 0x7B;
            t.Exits = 0; t.HasMule = 0; t.Phase = 0; v.Timers[1] = 0; t.Time = 0; t.EventShown = 0;
            t.Mask = 0x0F;
            MapMode(game, t);
            game.Emit(new TurnStartedEvent(cp));
            bool cpu = (g.MaxIndexCpu & 0x80) == 0 && cp <= g.MaxIndexCpu;
            byte ctrl;
            if (cpu)
            {
                game.Emit(new MessageEvent("THE COMPUTER IS TAKING HIS TURN"));
                v.Button[cp] = 1;
                ctrl = cp;
            }
            else
            {
                game.Emit(new MessageEvent(g.NbPaddles == 0 ? "PRESS YOUR STICK BUTTON TO START" : "PRESS ANY STICK BUTTON TO START"));
                ctrl = g.NbPaddles == 0 ? cp : (byte)3;
            }
            t.Ctrl = ctrl;
            // The title and money drawing now and then ends before the next vertical blank.
            if (game.Race?.Invoke("title quick") != true) yield return game.Frames(TitleDrawFrames);
            yield return game.WaitJiffy(6);
            game.Trace?.Invoke("event");
            yield return PlayerEvent(game, t);
            t.TimeWait = g.PlayersTurnTimeWait[cp];
            t.Flash = 12; v.Timers[0] = 12; t.Flashes = 6; v.Timers[3] = 0xFF;
            yield return WaitStart(game, t, cpu);
            game.Trace?.Invoke("pressed");
            v.Sound = cp;
            t.Ctrl = ctrl;
            t.X = 0x7C; t.Y = 0x58;
            t.Running = false;
            if (t.EventShown != 0) v.Timers[3] = v.Timers[3] + 100 > 0xFF ? (byte)0xFF : (byte)(v.Timers[3] + 100);
            t.Phase = 7;
            v.Timers[1] = 0xFF;
            t.WampusTime = (byte)((0xFF - v.Timers[3]) >> 2);
            t.Outfit = 0;
            t.Time = 91;
            if (g.GoodsPlayersNb[cp] < g.FoodReq)
            {
                t.Time = (byte)(t.Time * g.GoodsPlayersNb[cp] / g.FoodReq);
                yield return MessageWithBeep(game, t, 22);
                Second(game, 23, "DECREASES TIME.");
                yield return game.Beeps(2);
                v.Timers[0] = 50;
                yield return game.WaitJiffyLevel();
            }
            t.Time += 10;
            game.Trace?.Invoke("food");
            if (cpu) throw new NotImplementedException("playerTurnAi");
            yield return Human(game, t);
            game.Trace?.Invoke("end");
            game.Emit(new TurnEndedEvent(cp));
            order += step;
            if (order < 0 || order >= 4) break;
        }
    }

    /// <summary>playerEvent ($8131) as the turn shows it: the wampus moves, then maybe an event with
    /// its scrolling text.</summary>
    static IEnumerable PlayerEvent(Game game, Turn t)
    {
        var g = game.State;
        MoveWampus(game, t);
        var r = PlayerEvents.Run(g, game.Rng, g.CurrentPlayer, ref t.Outfit);
        if (r == null) yield break;
        game.Emit(new PlayerEventShownEvent(r));
        // soundPlayerEvent ($897C): a falling tune for bad news, a rising one for good.
        if (PlayerEvents.Quality[r.Event] < 0)
            for (int i = 0; i < 7; i++) yield return game.WaitJiffy(0x14);
        else
            for (int i = 0; i < 9; i++) yield return game.WaitJiffy(8);
        // addMessageEnd ($810F): the text scrolls (handler $0E).
        t.Running = false;
        t.EventShown++;
    }

    /// <summary>$66F0: the player's colour flashes until the button is pressed. A CPU's turn starts
    /// after 7 flashes (and the event text), or when a human presses.</summary>
    static IEnumerable WaitStart(Game game, Turn t, bool cpu)
    {
        var g = game.State;
        var v = game.Vbi;
        // A busy loop entered at $66F5; each pass sees what the last vertical blank changed.
        while (true)
        {
            if (cpu && AnyHumanPressing(g, v)) yield break;
            if (v.Timers[0] == 0)
            {
                // The colour flashes.
                v.Timers[0] = 0x0A;
                if (cpu)
                {
                    if (AnyHumanPressing(g, v)) yield break;
                    if ((sbyte)--t.Flashes < 0)
                    {
                        t.Flashes = 0;
                        if (t.EventShown != 1) yield break;
                    }
                }
            }
            if (v.Button[t.Ctrl] == 0) yield break;
            yield return null;
        }
    }

    /// <summary>sub_373C: a human holds the button.</summary>
    static bool AnyHumanPressing(GameState g, Vbi v)
    {
        for (int x = 3; x >= 0; x--)
            if ((g.PlayersAi[x] & 0x80) == 0 && v.Button[x] == 0) return true;
        return false;
    }

    static void ShowMessage(Game game, int code, string text) => game.Emit(new TurnMessageEvent(code, text));

    /// <summary>sub_5236: the walker on the map, in front of the store.</summary>
    static void MapMode(Game game, Turn t)
    {
        byte x = t.X, mx;
        if (x == 0x7B) mx = 0x7B;
        else if (x < 0x7B) { x = 0x71; mx = 0x77; }
        else { x = 0x87; mx = 0x81; }
        t.X = x; t.MuleX = mx;
        t.Y = t.MuleY = 0x74;
        t.Map = 1;
        t.Running = true;
        t.Phase = 4;
        game.Vbi.Handler = () => Vbl(game, t);
    }

    /// <summary>sub_51BD's positions: the walker in the town street, at the side he came in.</summary>
    static void TownMode(Turn t)
    {
        byte x = t.X, mx;
        if (x == 0x7C) mx = 0x7C;
        else if (x < 0x7C) { x = 0x4C; mx = 0x44; }
        else { x = 0xAC; mx = 0xB4; }
        t.X = x; t.MuleX = mx;
        t.Y = t.MuleY = 0x58;
        t.Map = 0;
        t.Running = true;
    }

    const int ToTownFrames = 26 - 18;   // sub_51BD: three waits of 6 jiffies plus drawing
    const int ToMapFrames = 1;    // sub_5227: drawing past the four waits

    /// <summary>sub_51BD: the town screen.</summary>
    static IEnumerable ToTown(Game game, Turn t)
    {
        t.Running = false;
        ClearWalker(t);
        // sub_5064: the town's players 2 and 3 borrow the first quarter-row's positions.
        game.State.MountainsX[0] = 0;
        game.State.RiverStoreX[0] = 0x70;
        yield return game.WaitJiffy(6);
        yield return game.WaitJiffy(6);
        yield return game.WaitJiffy(6);
        yield return game.Frames(ToTownFrames);
        t.Toggle = 0;
        TownMode(t);
        game.Emit(new TurnScreenEvent(false));
    }

    /// <summary>sub_5227: the map screen (falls into sub_5236).</summary>
    static IEnumerable ToMap(Game game, Turn t)
    {
        t.Running = false;
        ClearWalker(t);
        // The drawing before the first wait can run past the vertical blank when the caller has
        // just drawn a lot (the outfit message and the money).
        if (game.Race?.Invoke("map late") == true) yield return null;
        yield return ToMapDisplay(game);
        yield return game.Frames(ToMapFrames);
        MapMode(game, t);
        game.Emit(new TurnScreenEvent(true));
    }

    static IEnumerable Human(Game game, Turn t)
    {
        var g = game.State;
        var v = game.Vbi;
        byte cp = g.CurrentPlayer;
        yield return ToTown(game, t);
        t.WarnTime = 0x1E; t.Warn = 1;
        v.Timers[1] = 4;
        t.Phase = 4;
        game.Vbi.Handler = () => Vbl(game, t);
        bool inTown = true;
        while (true)
        {
            if (inTown)
            {
                var r = new Ref();
                yield return Town(game, t, r);
                if (r.Result == Outcome.TimeOut) { yield return TimeOut(game, t); break; }
                if (r.Result == Outcome.Gambling) { yield return Gambling(game, t); break; }
                inTown = false;
            }
            else
            {
                var r = new Ref();
                yield return OnMap(game, t, r);
                if (r.Result == Outcome.TimeOut) { yield return TimeOut(game, t); break; }
                inTown = true;
            }
        }
        game.Vbi.Handler = null;
    }

    enum Outcome { None, TimeOut, Gambling, Exit }
    sealed class Ref { public Outcome Result; }

    /// <summary>EnterStore/LoopInStore ($6800).</summary>
    static IEnumerable Town(Game game, Turn t, Ref result)
    {
        var g = game.State;
        var v = game.Vbi;
        byte cp = g.CurrentPlayer;
        game.Trace?.Invoke("enter");
        t.Mask = 0; t.Msg = 0;
        while (true)
        {
            // A message and the end of the time in the same vertical blank: the loop sees the time
            // first unless the blank came just after its time check.
            bool late = t.Time == 0 && t.Msg != 0 && game.Race?.Invoke("message first") == true;
            if (t.Time == 0 && !late)
            {
                t.X = 0x7B; t.Mask = 0x0F;
                yield return ToMap(game, t);
                t.Phase = 4;
                yield return game.WaitJiffy(4);
                result.Result = Outcome.TimeOut;
                yield break;
            }
            if (t.Msg != 0)
            {
                if (t.Msg >= 3 || t.Exits == 0) yield return MessageWithBeep(game, t, t.Msg);
                else ShowStatic(game, t.Msg);
                v.Timers[0] = 0x1E;
                t.Msg = 0;
                if (late) continue;
            }
            if (v.Timers[0] == 0) { v.Timers[0]--; game.Emit(new TurnMessageEvent(0, "")); }
            if (t.X < 0x49 || t.X >= 0xAD)
            {
                if (t.Outfit == 0 && t.HasMule != 0)
                {
                    t.X = t.X >= 0xAD ? (byte)0xAC : (byte)0x4C;
                    t.Msg = 12;
                }
                else
                {
                    t.Exits++;
                    result.Result = Outcome.Exit;
                    game.Trace?.Invoke("exit");
                    yield return ToMap(game, t);
                    yield return game.WaitJiffy(6);
                    v.Timers[2] = 0;
                    yield break;
                }
            }
            byte a = t.Y;
            bool outfitted = false;
            if (a < 0x39)
            {
                yield return Outfitting(game, t);
                v.Timers[1] = 6;
                a = t.Y;
                outfitted = true;
            }
            // The loop spins with nothing to wait on: a new pass sees only what the next vertical blank
            // changes, unless this pass did something itself.
            if (a < 0x78) { if (!outfitted) yield return null; continue; }
            t.Mask = 0x0F;
            v.Timers[1] = 6;
            byte b = t.Building;
            if (b == 7) { result.Result = Outcome.Gambling; yield break; }
            if (b == 5)
            {
                if (t.Outfit == 5)
                {
                    if ((t.Marked & 0x80) != 0) t.Msg = 9;
                    else
                    {
                        int i;
                        for (i = g.NbLandsToSellByPlayers - 1; i >= 0; i--) if (g.LandsToSell[i] == t.Marked) break;
                        if (i < 0) g.LandsToSell[g.NbLandsToSellByPlayers++] = t.Marked;
                        v.Timers[1] = 6;
                        ShowMessage(game, 0x8D, "LAND WILL BE SOLD AT NEXT AUCTION");
                        t.Marked = 0xFF; t.Outfit = 0;
                    }
                }
                else
                {
                    ShowStatic(game, 13);
                    v.Timers[1] = 0xFA;
                    if (t.Time < t.WarnTime) t.WarnTime = (byte)(t.Time - 1);
                    Second(game, 15, "THEN COME BACK.");
                    t.Outfit = t.Building; t.Marked = 0xFF;
                }
            }
            else if (b == 6)
            {
                if (t.Outfit == 6)
                {
                    if ((t.Marked & 0x80) != 0) t.Msg = 8;
                    else
                    {
                        CrystiteFound(game, t.Marked);
                        t.Marked = 0xFF; t.Outfit = 0;
                    }
                }
                else
                {
                    ShowStatic(game, 14);
                    Second(game, 15, "THEN COME BACK.");
                    t.Outfit = t.Building; t.Marked = 0xFF;
                }
            }
            else
            {
                t.Outfit = 0;
                if (t.HasMule != 0)
                {
                    // The mule goes back to the corral.
                    g.GoodsStoreNb[4]++;
                    g.PlayersCash[cp] += g.MulePrice;
                    t.MuleX = t.X;
                    do yield return MuleStep(game, t, 2); while (t.MuleY < 0x8A);
                    t.HasMule = 0;
                    game.Emit(new MuleSoldBackEvent(cp));
                }
                else if (g.GoodsStoreNb[4] == 0) t.Msg = 4;
                else
                {
                    g.GoodsStoreNb[4]--;
                    t.MuleY = 0x84; t.MuleX = 0x9C;
                    t.HasMule = 1;
                    g.PlayersCash[cp] -= g.MulePrice;
                    game.Emit(new MuleBoughtEvent(cp));
                }
            }
            // $6992: back out into the street, facing up.
            yield return game.WaitJiffy(6);
            t.Y = 0x74;
            yield return game.WaitJiffy(6);
            t.Mask = 0x0E;
            v.Timers[0] = 0xC8;
        }
    }

    static void ShowStatic(Game game, int code) => game.Emit(new TurnMessageEvent(code, ""));

    /// <summary>addStaticTurnMessage2 ($4A2B): beeps first unless an effect is playing.</summary>
    static IEnumerable MessageWithBeep(Game game, Turn t, int code)
    {
        if ((game.Vbi.Sound & 0x80) != 0) game.Play(4);
        ShowStatic(game, code);
        yield break;
    }

    /// <summary>addMessageTurn ($4A35): a second line under the static message.</summary>
    static void Second(Game game, int code, string text) => game.Emit(new TurnMessageEvent(code, text));

    /// <summary>sub_6B03: two jiffies, then until the walker's next step; the mule moves by
    /// <paramref name="dy"/>.</summary>
    static IEnumerable MuleStep(Game game, Turn t, byte dy)
    {
        yield return game.WaitJiffy(2);
        while (t.Phase != 0) yield return null;
        t.MuleY += dy;
    }

    /// <summary>addMessageAndBuyOutfittingMule ($69D0).</summary>
    static IEnumerable Outfitting(Game game, Turn t)
    {
        var g = game.State;
        t.Mask = 0x0F;
        t.Outfit = t.Building;
        ShowStatic(game, 20);
        t.MuleX = t.X;
        do yield return MuleStep(game, t, 0xFF); while (t.MuleY >= 0x39);
        do yield return MuleStep(game, t, 1); while (t.MuleY < 0x50);
        t.Y = 0x3C;
        yield return game.WaitJiffy(6);
        t.Mask = 0x0D;
        game.Emit(new MuleOutfittedEvent(g.CurrentPlayer, t.Outfit));
        game.Vbi.Timers[0] = 0xC8;
        g.PlayersCash[g.CurrentPlayer] -= Price(g, t.Outfit);
    }

    /// <summary>crystiteFound ($69A7): the assay result; the player now knows the plot.</summary>
    static void CrystiteFound(Game game, byte plot)
    {
        var g = game.State;
        byte c = g.CrystiteAt(plot);
        g.PlotsKnownCrystite[plot] = c;
        game.Emit(new AssayEvent(plot, c >= 3 ? 3 : c));
    }

    /// <summary>Gambling ($6A82): the pub pays random(time left * 2) plus a bonus by round, at most 250.</summary>
    static IEnumerable Gambling(Game game, Turn t)
    {
        var g = game.State;
        t.Running = false;
        game.Vbi.Handler = null;
        t.Warn = 0;
        game.Trace?.Invoke("gamble");
        int x = g.NumRound >> 2;
        int won = game.Rng.Below((byte)(t.Time << 1)) + GamblingBonus[x];
        if (won > 255) won = 250;
        g.PlayersCash[g.CurrentPlayer] += (ushort)won;
        game.Emit(new GamblingEvent(g.CurrentPlayer, won));
        yield return PubAnimation(game);
        yield return game.WaitJiffy(0x78);
        yield return EndOfTurn(game, t);
    }

    const int PubAnimationFrames = 157;

    static IEnumerable PubAnimation(Game game)
    {
        yield return game.Frames(PubAnimationFrames);
    }

    /// <summary>TimeHasRunOut ($6C71).</summary>
    static IEnumerable TimeOut(Game game, Turn t)
    {
        t.Toggle = 0;
        game.Trace?.Invoke("timeout");
        ShowStatic(game, 10);
        t.Time--;
        yield return game.Beeps(2);
        yield return game.WaitJiffy(0xC8);
        t.Running = false;
        game.Vbi.Handler = null;
        yield return EndOfTurn(game, t);
    }

    const int EndFrames = 1;

    /// <summary>$6ADC.</summary>
    static IEnumerable EndOfTurn(Game game, Turn t)
    {
        t.Toggle = 0;
        ClearWalker(t);
        if (t.Map == 0)
        {
            yield return ToMapDisplay(game);
            yield return game.Frames(EndFrames);
        }
    }

    /// <summary>sub_50F5 alone.</summary>
    static IEnumerable ToMapDisplay(Game game)
    {
        var g = game.State;
        for (int i = 0; i < 4; i++) yield return game.WaitJiffy(6);
        g.RiverStoreX[0] = g.OldRiverStoreX;
        g.MountainsX[0] = g.OldMountainX;
    }

    /// <summary>ExitStore/LoopOnMap ($6B14).</summary>
    static IEnumerable OnMap(Game game, Turn t, Ref result)
    {
        var g = game.State;
        var v = game.Vbi;
        byte cp = g.CurrentPlayer;
        while (true)
        {
            if (t.Time == 0) { result.Result = Outcome.TimeOut; yield break; }
            if (v.Timers[0] == 0) { game.Emit(new TurnMessageEvent(0, "")); v.Timers[0]--; }
            if (v.Timers[2] == 0 && v.Button[t.Ctrl] == 0)
            {
                t.Mask = 0x0F;
                if ((((byte)(t.Y - 0x10)) & 0x10) != 0 || (((byte)(t.X - 8)) & 8) != 0)
                {
                    yield return game.WaitSoundThenBeep();
                    yield return MuleEscapes(game, t);
                }
                else yield return PlotAction(game, t);
                v.Timers[2] = 8;
                yield return game.WaitJiffy(6);
                t.Mask = 0;
            }
            if ((Hits(game, t) & 8) != 0 && t.Y >= 0x66 && t.Y < 0x8C)
            {
                if (t.X >= 0x78 && t.X < 0x81) t.X = 0x7C;
                yield return ToTown(game, t);
                yield break;
            }
            yield return null;
        }
    }

    /// <summary>The main loop reads the collision register during the frame.</summary>
    static byte Hits(Game game, Turn t)
    {
        var c = MapCollisions.Compute(game.State, t);
        t.Seen = ((byte)(t.Seen.Players | c.Players), t.Seen.Playfield || c.Playfield);
        return c.Players;
    }

    /// <summary>$6B5F: the button on a plot.</summary>
    static IEnumerable PlotAction(Game game, Turn t)
    {
        var g = game.State;
        var v = game.Vbi;
        byte cp = g.CurrentPlayer;
        int plot = PixelToPlot(t.X, t.Y);
        byte owner = g.PlotsPlayer[plot];
        if ((owner & 0x80) != 0 || owner != cp)
        {
            if (t.Outfit != 6)
            {
                yield return MessageWithBeep(game, t, 6);
                yield return MuleEscapes(game, t);
                v.Timers[0] = 0x32;
                yield break;
            }
        }
        if (t.Outfit >= 5)
        {
            if ((t.Marked & 0x80) == 0) { yield return MessageWithBeep(game, t, 25); v.Timers[0] = 0x32; yield break; }
            t.Marked = (byte)plot;
            ShowStatic(game, t.Outfit == 5 ? 16 : 17);
            Second(game, 18, "RETURN TO OFFICE");
            v.Sound = cp;
            v.Timers[0] = 0x64;
            yield break;
        }
        if (g.PlotsExp[plot] == 0 && t.HasMule == 0) { yield return MessageWithBeep(game, t, 1); v.Timers[0] = 0x32; yield break; }
        bool swap = g.PlotsExp[plot] != 0 && t.HasMule != 0;
        if (t.Outfit >= 3 && (g.CrystiteAt(plot) & 0x80) != 0) { yield return MessageWithBeep(game, t, 27); v.Timers[0] = 0x32; yield break; }
        (g.PlotsExp[plot], t.Outfit) = (t.Outfit, g.PlotsExp[plot]);
        if (!swap)
        {
            t.HasMule ^= 1;
        }
        game.Emit(new PlotChangedEvent(plot, g.PlotsExp[plot]));
        if (t.HasMule != 0)
        {
            t.MuleY = t.Y;
            t.MuleX = (byte)(t.X + 4);
            ShowStatic(game, 19);
        }
        else ShowStatic(game, 26);
        v.Sound = cp;
        v.Timers[0] = 0x64;
    }

    /// <summary>sub_6C92: outside the tournament's beginner level, a mule let go runs off.</summary>
    static IEnumerable MuleEscapes(Game game, Turn t)
    {
        var g = game.State;
        if (g.Level == 0 || t.HasMule == 0) yield break;
        bool running = t.Running;
        byte time = t.Time;
        t.Toggle = 0; t.Phase = 0; t.Map = 1; t.HasMule = 1;
        t.Running = false; t.Time = 0xFF;
        t.MuleWalkOnly = true;
        while (t.MuleX >= 0x28 && t.MuleX < 0xDC) yield return null;
        t.MuleWalkOnly = false;
        t.Outfit = 0; t.HasMule = 0; t.Toggle = 0;
        t.Time = time;
        t.Running = running;
        game.Emit(new MuleRanAwayEvent(g.CurrentPlayer));
    }

    /// <summary>pixelToPlot ($48BD).</summary>
    public static int PixelToPlot(byte x, byte y) => (y - 44) / 32 * GameState.Columns + (x - 56) / 16;

    /// <summary>moveWampus ($8B5E): to a random mountain quarter-row, hidden, for two ticks.</summary>
    static void MoveWampus(Game game, Turn t)
    {
        var g = game.State;
        byte m = game.Rng.Below(20);
        t.WampusY = (byte)(m * 8 + 48);
        t.WampusVisible = 0;
        byte x = g.MountainsX[m];
        t.WampusX2 = x;
        t.WampusX = (x & 1) != 0 ? x : (byte)(x + 1);
        t.WampusTime = 2;
    }

    /// <summary>The walk handler: $55FE (time bar, wampus) then $5625 (steps).</summary>
    static void Vbl(Game game, Turn t)
    {
        var v = game.Vbi;
        t.Latch = LatchedNow(game.State, t);
        t.Seen = default;
        if (t.MuleWalkOnly) Steps(game, t);
        else if (t.Running)
        {
            if (v.Timers[1] == 0 && t.Time != 0 && (t.Time & 0x80) == 0)
            {
                t.Time--;
                if (t.WampusTime != 0) t.WampusTime--;
                v.Timers[1] = t.TimeWait;
            }
            Wampus(game, t);
            Steps(game, t);
        }
    }

    /// <summary>What the GTIA has latched over the frame being shown: the state the main loop left,
    /// plus anything the main loop saw before it changed the sprites (it polls during the frame,
    /// so it sees the walker's lines before it acts).</summary>
    public static (byte Players, bool Playfield) LatchedNow(GameState g, Turn t)
    {
        var (p, f) = MapCollisions.Compute(g, t);
        return ((byte)(p | t.Seen.Players), f || t.Seen.Playfield);
    }

    static void Wampus(Game game, Turn t)
    {
        // TODO: catching and showing the wampus.
    }

    /// <summary>$5625.</summary>
    static void Steps(Game game, Turn t)
    {
        var g = game.State;
        var v = game.Vbi;
        t.Phase++;
        if (t.Phase != 6) return;
        t.Phase = 0;
        if ((t.Time & 0x80) != 0) { MuleWalksOff(t); return; }
        byte dir = (byte)(v.Stick[t.Ctrl] | t.Mask);
        t.Dir = dir;
        int vy = (dir & 3) - 1;
        byte dy = vy < 0 ? (byte)0 : DyTable[vy];
        if (t.Map == 0) dy <<= 1;
        t.Dy = dy;
        t.NewY = (byte)(dy + t.Y);
        byte anim = vy < 0 ? (byte)0x53 : VertAnim[vy];
        int vx = ((dir & 0x0C) >> 2) - 1;
        byte dx = vx < 0 ? (byte)0 : DxTable[vx];
        if (t.Map == 0) dx <<= 1;
        t.Dx = dx;
        t.NewX = (byte)(dx + t.X);
        int idx = (vx < 0 ? 0 : HorzAnim[vx]) + anim;
        anim = t.Map == 0 ? TownAnim[idx] : MapAnim[idx];
        t.Toggle ^= 1;
        if (t.Toggle != 0) anim += t.Map != 0 ? (byte)8 : (byte)0x18;
        t.Anim = anim;
        if (t.Map == 0) TownChecks(game, t); else MapChecks(game, t);
        t.Y = t.NewY;
        t.X = t.NewX;
        t.Hpos0 = t.X;
        if (t.Map != 0) DrawWalker(g, t);
        if (t.HasMule == 0) return;
        if (t.Map == 0) TownMule(t); else MapMule(t);
    }

    /// <summary>$577C: doors, money and the mule rules at the town buildings.</summary>
    static void TownChecks(Game game, Turn t)
    {
        var g = game.State;
        int col = (byte)(t.NewX - 0x30) >> 2;
        bool below;
        if (t.NewY < 0x54) below = false;
        else if (t.NewY >= 0x5E) below = true;
        else { t.Mask = 0; t.Building = 0; return; }
        if (t.Y < 0x54 || t.Y >= 0x5E)
        {
            t.NewX = t.X;
            t.Mask |= 0x0C;
        }
        int a = t.Dir & 3;
        if (!below)
        {
            if (a != 2) return;
            byte b = TopDoors[col];
            if (b == 0) { t.NewY = t.Y; return; }
            t.Building = b;
            if (t.HasMule != 0) { CashCheck(g, t, b); return; }
            t.Msg = 1;
            t.NewY = t.Y;
            return;
        }
        if (a != 1) return;
        byte d = BottomDoors[col];
        if (d == 0) { t.NewY = t.Y; return; }
        t.Building = d;
        if (d == 8)
        {
            if (t.HasMule != 0) return;
            if (!CashCheck(g, t, d)) return;
            if (g.GoodsStoreNb[4] != 0) return;
            t.Msg = 4;
            t.NewY = t.Y;
            return;
        }
        if (!CashCheck(g, t, d)) return;
        if (t.HasMule == 0) return;
        t.Msg = 2;
        t.NewY = t.Y;
    }

    /// <summary>sub_57FB: not enough money stops the walker with message 3. Returns whether he may go on.</summary>
    static bool CashCheck(GameState g, Turn t, byte outfit)
    {
        if (g.PlayersCash[g.CurrentPlayer] >= Price(g, outfit)) return true;
        t.Msg = 3;
        t.NewY = t.Y;
        return false;
    }

    /// <summary>$581A: the map's edges and the mountain and river slowdowns.</summary>
    static void MapChecks(Game game, Turn t)
    {
        if (t.NewX < 0x37 || t.NewX >= 0xC6) t.NewX = t.X;
        if (t.NewY < 0x2C || t.NewY >= 0xC5) { t.NewY = t.Y; return; }
        var (hits, playfield) = t.Latch;
        if ((hits & 4) != 0)
        {
            if (playfield) return;
            t.Phase = 0xF1;
        }
        else if ((hits & 8) != 0) t.Phase = 0xF8;
    }

    /// <summary>$569E: clear around the walker (sub_53B7), then draw his frame (sub_5370), 7 rows on
    /// the map.</summary>
    static void DrawWalker(GameState g, Turn t)
    {
        for (int i = 0; i <= 0x1C; i++) t.Pm0[(byte)(t.Y - 3 + i)] = 0;
        var frames = WalkerFrames(g.PlayersRace[g.CurrentPlayer]);
        for (int k = 1; k < 8; k++) t.Pm0[(byte)(t.Y + k)] = frames[(t.Anim + k) & 0x7F];
    }

    // TODO: the snapshot holds only the Mechtron and Bonzoid frames; the others need the disk image.
    static byte[] WalkerFrames(byte race) => MapGraphics.WalkerMap((Race)race) ?? MapGraphics.MechtronMap;

    /// <summary>sub_4C61 on the walker's page.</summary>
    static void ClearWalker(Turn t) => Array.Clear(t.Pm0);

    /// <summary>$5853: when the time has run out, a mule being led walks off the screen.</summary>
    static void MuleWalksOff(Turn t)
    {
        if (t.HasMule == 0) return;
        if (t.MuleX < 0x7B) { t.MuleX -= 4; t.Anim = 0x10; }
        else { t.MuleX += 4; t.Anim = 0; }
        t.Toggle ^= 8;
        t.Anim += t.Toggle;
    }

    /// <summary>$53C9: the mule follows the walker in the town.</summary>
    static void TownMule(Turn t)
    {
        bool beside = false;
        if (t.Dx == 0)
        {
            byte r = (byte)(t.MuleX + 9 - t.X);
            if ((r & 0x80) != 0) t.MuleX += r == 0xFC ? (byte)4 : (byte)8;
        }
        byte d = (byte)(t.X + 0x0A - t.MuleX);
        bool skipX = false;
        if ((d & 0x80) == 0 && d < 0x1C)
        {
            if (d < 0x14 || t.Dx != 4) { beside = true; skipX = true; }
            else t.MuleX -= 8;
        }
        if (!skipX)
        {
            t.MuleX += t.Dx;
            byte e = (byte)(t.Y - t.MuleY);
            if (e != 0) t.MuleY = (e & 0x80) == 0 ? (byte)(t.MuleY + 4) : (byte)(t.MuleY - 4);
        }
        byte f = (byte)(t.Y + 0x16 - t.MuleY);
        if ((f & 0x80) == 0 && f < 0x2C)
        {
            if (beside) t.Anim = 0x60;
            return;
        }
        t.MuleY += t.Dy;
        byte h = (byte)(t.X - t.MuleX);
        if (h == 0) return;
        if ((h & 0x80) != 0) t.MuleX -= 4;
        else if (t.Dx == 0) t.MuleX += 4;
    }

    /// <summary>sub_56E5: the mule follows the walker on the map.</summary>
    static void MapMule(Turn t)
    {
        bool close = false;
        byte r = (byte)(t.X + 6 - t.MuleX);
        if ((r & 0x80) == 0 && r < 0x0E) close = true;
        else
        {
            t.MuleX += t.Dx;
            byte e = (byte)(t.Y - t.MuleY);
            if (e != 0) t.MuleY = (e & 0x80) == 0 ? (byte)(t.MuleY + 2) : (byte)(t.MuleY - 2);
        }
        byte f = (byte)(t.Y + 9 - t.MuleY);
        if ((f & 0x80) == 0 && f < 0x12)
        {
            if (close) t.Anim = 0x20;
            return;
        }
        t.MuleY += t.Dy;
        byte h = (byte)(t.X - t.MuleX);
        if (h == 0) return;
        t.MuleX = (h & 0x80) == 0 ? (byte)(t.MuleX + 2) : (byte)(t.MuleX - 2);
    }
}
