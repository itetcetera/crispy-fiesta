using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>A step of a round event's animation, for the client: what moves, and where (screen units).</summary>
public sealed record RoundEventStepEvent(RoundEventKind Kind, int Step, int X = 0, int Y = 0) : GameEvent;

/// <summary>A ship on the map: the transport (or the pirates) landing or taking off, at x.</summary>
public sealed record ShipEvent(bool Pirates, bool TakeOff, byte X) : GameEvent;

/// <summary>
/// The round events as the original shows them ($722E-$8092): each one's animation and waits, and when it
/// shows the production (animMapProd) relative to its effect. The effects themselves are in <see cref="RoundEvents"/>.
/// </summary>
public static class RoundEventScreens
{
    static readonly byte[] Snapshot7387 =
    {
        0x00, 0x0C, 0x18, 0x24, 0x30, 0x3C, 0x48, 0x54, 0x60,   // $7387
        0x00, 0x0A, 0x14, 0x1E, 0x28, 0x32, 0x3C, 0x46, 0x50,   // $7390
        0x50, 0x53, 0x60, 0x62, 0x71, 0x74, 0x78, 0x7A, 0x7B,   // $7399: the ship's x, by step
        0x58, 0x5B, 0x66, 0x6A, 0x6E, 0x71, 0x78, 0x7A, 0x7B,   // $73A2
    };

    // The pest's path ($775F: y steps, halved; $7766: x steps), read by the step counter 20..1.
    static readonly byte[] PestPath =
    {
        0xFF, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFF, 0x02, 0x01, 0x02, 0x02, 0x01, 0x02, 0x02, 0x00, 0x01,
        0xFF, 0x00, 0xFF, 0x01, 0x00, 0xFF, 0xFE, 0xFE, 0xFE, 0xFE, 0xFE, 0xFF,
    };

    // Frames of the original's CPU-bound animations (the meteorite's flight, the radiation's forty
    // jumps), measured on the original; the frames they take vary with the cycle they start on.
    // Fitted over 40 games of each: within a frame for the meteorite, two for the radiation.
    static int MeteoriteFrames(int steps) => (int)Math.Round(steps * 0.55);
    const double RadiationStep = 0.235, RadiationJumpStart = 0.06;
    static int RadiationFrames(int totalSteps) => (int)Math.Round(26 + totalSteps * 0.233);

    /// <summary>roundEvent ($7B28): a card, its effect and animation, and the production.</summary>
    public static IEnumerable Run(Game game)
    {
        var g = game.State;
        var rng = game.Rng;
        int x = RoundEvents.Draw(g, rng);
        if (x < 0)
        {
            game.Emit(new RoundEventShownEvent(null));
            yield return GameLoop.AnimMapProd(game);
            yield break;
        }
        var kind = (RoundEventKind)x;
        game.Trace?.Invoke($"kind {kind}");
        switch (kind)
        {
            case RoundEventKind.PestAttack: yield return Pest(game); break;
            case RoundEventKind.PirateShip: yield return Pirates(game); break;
            case RoundEventKind.AcidRain: yield return AcidRain(game); break;
            case RoundEventKind.Planetquake: yield return Planetquake(game); break;
            case RoundEventKind.Sunspots: yield return Sunspots(game); break;
            case RoundEventKind.Meteorite: yield return Meteorite(game); break;
            case RoundEventKind.Radiation: yield return Radiation(game); break;
            default: yield return Fire(game); break;
        }
    }

    static RoundEventResult Apply(Game game, RoundEventKind kind)
    {
        var r = RoundEvents.Apply(game.State, game.Rng, kind);
        game.Emit(new RoundEventShownEvent(r));
        return r;
    }

    /// <summary>$78CB: the production first; then a pest crawls in (handler $06, v07) and eats the plot's
    /// food one unit a bite, fifteen frames after each bite starts.</summary>
    static IEnumerable Pest(Game game)
    {
        var g = game.State;
        var v = game.Vbi;
        yield return GameLoop.AnimMapProd(game);
        var before = g.PlotsProd.ToArray();
        var r = Apply(game, RoundEventKind.PestAttack);
        if (!r.Shown) yield break;
        int plot = r.Plot;
        int food = before[plot];
        game.Emit(new TitleEvent("PEST ATTACK"));
        // Six segments, head first ($9F x, $89 y); the seventh slot is what shifts out.
        var px = new byte[7];
        var py = new byte[7];
        px[0] = (byte)(56 + plot % 9 * 16 + 13);
        py[0] = (byte)(44 + plot / 9 * 32 - 3);
        byte count = 8, steps = 0x14, seg = 7, dir = 0, bite = 0, pitch = 0; // $A9 $AB $D9 $DB $DD $DC
        byte end = (byte)(food + 6); // $88
        byte crunch = 0; // $AE
        var p = v.Pokey;
        v.Handler = () =>
        {
            // Each bite a tone on voice 1 slides up and back down; voice 2 crunches with every step.
            p[Vbi.Audf(2)] = 0xC0;
            if (bite == 0 && seg == 0 && steps < end && steps >= 6) { pitch = 0x5F; p[Vbi.Audc(1)] = 0xAA; bite = 2; }
            if (bite == 2)
            {
                p[Vbi.Audf(1)] = pitch;
                pitch += 0x14;
                if (pitch == 0xFF) bite = 1;
            }
            if (bite == 1)
            {
                byte a = (byte)(pitch - 0x14);
                if (a == 0x5F) { p[Vbi.Audc(1)] = 0xA0; bite = 0; pitch = 0; }
                else p[Vbi.Audf(1)] = pitch = a;
            }
            if (--count != 0) return;
            count = 6;
            crunch ^= 4;
            p[Vbi.Audc(2)] = crunch;
            int s;
            bool shift = false;
            if (px[5] == 0)
            {
                // Coming in: the tail is not on screen yet.
                seg--;
                s = seg;
                if ((s & 0x80) != 0) { s = 0; seg = 0; dir = 0; }
                shift = true;
            }
            else
            {
                s = -1;
                if (dir != 0)
                {
                    s = seg - 1;
                    if (s < 0) dir = 0;
                }
                if (s < 0)
                {
                    s = seg + 1;
                    if (s == 6) { dir = 1; shift = true; }
                }
            }
            if (shift)
            {
                for (int k = 5; k >= 0; k--) { px[k + 1] = px[k]; py[k + 1] = py[k]; }
                px[0] = (byte)(px[0] + PestPath[7 + steps]);
                py[0] = (byte)(py[0] + 2 * PestPath[steps]);
                steps--;
            }
            seg = (byte)s;
            game.Emit(new RoundEventStepEvent(RoundEventKind.PestAttack, seg, px[0], py[0]));
        };
        // loc_7949: a unit goes fifteen frames after each bite starts.
        int left = food;
        while (steps != 0)
        {
            if (bite == 0) { yield return null; continue; }
            yield return game.WaitJiffy(15);
            left--;
            game.Emit(new ProductionShownEvent(plot, left));
        }
        v.Handler = null;
        v.Silence();
        // sub_7E87: the plot flashes (handler $0B, v12: a step every three frames after six, eight steps).
        yield return Flash(game);
    }

    static IEnumerable Flash(Game game)
    {
        var v = game.Vbi;
        byte step = 1, count = 6;
        v.Handler = () =>
        {
            if (--count != 0) return;
            count = 3;
            if (step == 0) return;
            if (++step == 9) { step = 0; v.Handler = null; }
        };
        while (step != 0) yield return null;
    }

    /// <summary>$76F0: the production; the pirate ship lands, every crystite plot loses its output one at
    /// a time (six frames each), and the ship takes off.</summary>
    static IEnumerable Pirates(Game game)
    {
        var g = game.State;
        yield return GameLoop.AnimMapProd(game);
        game.Emit(new TitleEvent("PIRATE SHIP"));
        byte exp = (byte)((g.Level == 2 ? Good.Crystite : Good.Smithore) + 1);
        var hit = new List<int>();
        for (int x = 44; x >= 0; x--) if (g.PlotsExp[x] == exp) hit.Add(x);
        g.AiMode = 1;
        // sub_768B ends with sub_1363, the sound reset.
        game.Vbi.Pokey[Vbi.Audctl] = 0;
        game.Vbi.Silence();
        yield return ShipLand(game, true);
        Apply(game, RoundEventKind.PirateShip);
        foreach (int x in hit)
        {
            game.Emit(new ProductionShownEvent(x, 0));
            yield return game.WaitJiffy(6);
        }
        yield return game.WaitJiffyLevel();
        yield return ShipTakeOff(game, true);
    }

    /// <summary>animTransportShipLand ($76D4): the ship is back after the last round.</summary>
    public static IEnumerable TransportShipLand(Game game)
    {
        var g = game.State;
        game.Trace?.Invoke("ship land");
        game.Emit(new TitleEvent("TRANSPORT SHIP"));
        g.AiMode = 0; // sub_76AF: $B375 tells the handler which ship
        yield return ShipLand(game, false);
        yield return GameLoop.AnimMapProd(game);
    }

    /// <summary>animShipLand ($75F5) with handler $05 (v06): the ship flies in from the right one unit a
    /// frame, then rises in steps every six frames and lands in nine.</summary>
    static IEnumerable ShipLand(Game game, bool pirates)
    {
        var v = game.Vbi;
        byte x0 = 0xF7, x1 = 0xFF, step = 0, count = 0x0A, wait = 0x0A; // $9F $A0 $AB $A9 $D9
        ShipStepSound(v);
        v.Handler = () =>
        {
            ShipSound(v, pirates);
            if (step == 0)
            {
                if ((x0 | x1) == 0) { step = 9; x1 = 8; return; }
                if (x0 != 0) x0--;
                if (x1 != 0) x1--;
                return;
            }
            if (--count != 0) return;
            count = 6;
            if (x0 < 0x4E) { x0++; x1++; game.Emit(new ShipEvent(pirates, false, x0)); return; }
            x0 = Snapshot7387[18 + 9 - step];
            ShipStepSound(v);
            game.Emit(new ShipEvent(pirates, false, x0));
            if (--step == 0) { count = 0xFF; wait = 0; }
        };
        while (wait != 0) yield return null;
        v.Handler = null;
        v.Silence();
    }

    /// <summary>The ship's engines, set every frame of its handler (v06, $7515): the pirates' a low rumble
    /// on voices 1 and 3, the transport's a hiss with voices 1 and 3 joined (AUDCTL 1, 15 kHz).</summary>
    static void ShipSound(Vbi v, bool pirates)
    {
        var p = v.Pokey;
        if (pirates)
        {
            p[Vbi.Audf(3)] = 0x32;
            p[Vbi.Audc(3)] = 0x03;
            p[Vbi.Audc(1)] = 0x06;
            p[Vbi.Audf(1)] = 0xC8;
            return;
        }
        p[Vbi.Audf(3)] = 0xFF;
        p[Vbi.Audf(1)] = 0xF0;
        p[Vbi.Audc(1)] = p[Vbi.Audc(3)] = 0xA6;
        p[Vbi.Audctl] = 0x01;
    }

    /// <summary>sub_74E6 puts a byte of the ship's picture ($7420 + $81) in AUDC2; at every step it reads
    /// is 0.</summary>
    static void ShipStepSound(Vbi v) => v.Pokey[Vbi.Audc(2)] = 0;

    /// <summary>anipShipTakeOff ($7674): back up the nine steps every eight frames, then away left one
    /// unit every eight frames.</summary>
    internal static IEnumerable ShipTakeOff(Game game, bool pirates)
    {
        var v = game.Vbi;
        byte x0 = 0x7B, step = 0, count = 0x0A, wait = 0x0A;
        v.Handler = () =>
        {
            ShipSound(v, pirates);
            if (--count != 0) return;
            count = 8;
            if (step == 0xF7)
            {
                wait = x0;
                if (x0 != 0) x0--;
            }
            else
            {
                x0 = Snapshot7387[18 + (byte)(step + 8)];
                ShipStepSound(v);
                step--;
            }
            game.Emit(new ShipEvent(pirates, true, x0));
        };
        while (wait != 0) yield return null;
        v.Handler = null;
        v.Silence();
        // $76EA: after the transport, the voices unjoined.
        if (!pirates) v.Pokey[Vbi.Audctl] = 0;
    }

    /// <summary>$7A72: a cloud crosses the screen (handler $08, v09: a unit every six frames), then the
    /// effect and the production.</summary>
    static IEnumerable AcidRain(Game game)
    {
        var v = game.Vbi;
        game.Emit(new TitleEvent("ACID RAIN STORM"));
        game.Emit(new MessageEvent("FOOD OUTPUT UP, ENERGY OUTPUT DOWN."));
        var r = Apply(game, RoundEventKind.AcidRain);
        byte count = 5, x = 0x14;
        var p = v.Pokey;
        // The title, the message and the cloud's row (sub_4C61) sometimes take the original past a
        // frame (docs/PLAN.md, timing effects).
        if (game.Race?.Invoke("rain title late") == true) yield return null;
        v.Handler = () =>
        {
            // The rain hisses on voice 1; now and then (one step in four) thunder cracks on voice 2.
            p[Vbi.Audf(1)] = 0x16;
            p[Vbi.Audf(2)] = 0x8E;
            p[Vbi.Audc(1)] = 0x04;
            if (--count != 0) return;
            count = 6;
            x++;
            byte drops = (byte)((v.Noise() & 3) << 4); // $D9: which drops the cloud shows
            v.Noise(); // its colour
            p[Vbi.Audc(2)] = AcidRainCrack[drops >> 4];
            game.Emit(new RoundEventStepEvent(RoundEventKind.AcidRain, r.Row, x));
        };
        while (x < 0xDC) yield return null;
        v.Handler = null;
        v.Silence();
        yield return GameLoop.AnimMapProd(game);
    }

    // The first byte of each of the four cloud pictures at $798E, which sub_79CE also puts in AUDC2.
    static readonly byte[] AcidRainCrack = { 0x0E, 0x00, 0x00, 0x00 };

    /// <summary>$722E: sixteen shakes nineteen frames apart, then the effect and the production.</summary>
    static IEnumerable Planetquake(Game game)
    {
        game.Emit(new TitleEvent("PLANETQUAKE"));
        game.Emit(new MessageEvent("MINING PRODUCTION HALF OF NORMAL."));
        var r = Apply(game, RoundEventKind.Planetquake);
        // A rumble on voices 1 to 3 that dies away over the sixteen shakes.
        var p = game.Vbi.Pokey;
        // The title and the message sometimes take the original past a frame (docs/PLAN.md, timing
        // effects).
        if (game.Race?.Invoke("event title late") == true) yield return null;
        p[Vbi.Audctl] = 0; // sub_1363
        game.Vbi.Silence();
        p[Vbi.Audc(1)] = p[Vbi.Audc(2)] = p[Vbi.Audc(3)] = 0x8C;
        for (int k = 16; k > 0; k--)
        {
            p[Vbi.Audf(1)] = 0xFF;
            p[Vbi.Audf(2)] = 0xC8;
            p[Vbi.Audf(3)] = 0x96;
            game.Emit(new RoundEventStepEvent(RoundEventKind.Planetquake, k, r.MountainQuarter));
            // sub_5E1B: the shake is drawn at the next frame's line 242, then nineteen frames.
            yield return null;
            yield return game.WaitJiffy(19);
            p[Vbi.Audc(1)] = (byte)(k | 0x80);
            p[Vbi.Audc(2)] = (byte)((k >> 1) | 0x80);
            p[Vbi.Audc(3)] = (byte)((k >> 2) | 0x80);
        }
        game.Vbi.Silence();
        yield return GameLoop.AnimMapProd(game);
    }

    /// <summary>$7C12: the effect, 250 frames and the level wait, then the production.</summary>
    static IEnumerable Sunspots(Game game)
    {
        game.Emit(new TitleEvent("SUNSPOT ACTIVITY"));
        game.Emit(new MessageEvent("ENERGY OUTPUT IS UP."));
        game.Vbi.Timers[0] = 0x96;
        Apply(game, RoundEventKind.Sunspots);
        yield return game.WaitJiffy(250);
        yield return game.WaitJiffyLevel();
        yield return GameLoop.AnimMapProd(game);
    }

    /// <summary>$7C95: the meteorite falls on its plot (a step a cycle-bound loop pass, each drawing it
    /// with two random masks), fifteen ten-frame waits, then the effect and the production.</summary>
    static IEnumerable Meteorite(Game game)
    {
        var g = game.State;
        game.Emit(new TitleEvent("METEORITE STRIKE"));
        if (g.Level == 2) game.Emit(new MessageEvent("METEOR MAKES NEW CRYSTITE DEPOSIT."));
        var r = Apply(game, RoundEventKind.Meteorite);
        int px = 56 + r.Plot % 9 * 16, py = 44 + r.Plot / 9 * 32;
        // sub_3974: a line from ($BE, $BE) to the plot; its length is the larger difference.
        int steps = Math.Max(Math.Abs(py + 12 - 0xBE), Math.Abs(px + 7 - 0xBE));
        // The fall's sound on voices 1 and 2: a whistle rising and growing as it nears, then the
        // impact's rumble dying away.
        var p = game.Vbi.Pokey;
        // The title and the message sometimes take the original past a frame (docs/PLAN.md, timing
        // effects).
        if (game.Race?.Invoke("event title late") == true) yield return null;
        p[Vbi.Audc(1)] = p[Vbi.Audc(2)] = 0x0F;
        p[Vbi.Audc(1)] = 0x8E;
        p[Vbi.Audf(1)] = 0x3C;
        yield return game.WaitJiffy(0x28);
        for (int k = steps; k > 0; k--)
        {
            // sub_7DCC: each step redraws both halves (sub_7C71, a random mask per line).
            for (int i = 0; i < 30; i++) game.Rng.NextByte();
        }
        // The steps are bound by the drawing, not the frames: the port spreads them evenly, and each
        // frame shows the registers of the last step done.
        int frames = game.Think?.Invoke($"meteorite {steps}") ?? MeteoriteFrames(steps);
        for (int f = 0; f < frames; f++)
        {
            int done = (int)((long)steps * (f + 1) / frames);
            if (done > 0) MeteoriteStepSound(p, (byte)(steps - done + 1));
            yield return null;
        }
        game.Trace?.Invoke("meteorite");
        for (int k = 15; k > 0; k--)
        {
            p[Vbi.Audc(1)] = p[Vbi.Audc(2)] = (byte)k;
            yield return game.WaitJiffy(10);
        }
        for (int i = 0; i < 30; i++) game.Rng.NextByte();
        game.Vbi.Silence();
        game.Emit(new RoundEventStepEvent(RoundEventKind.Meteorite, 0, px, py));
        yield return GameLoop.AnimMapProd(game);
    }

    /// <summary>$7D6C: a step of the fall, <paramref name="left"/> steps from the ground.</summary>
    static void MeteoriteStepSound(byte[] p, byte left)
    {
        p[Vbi.Audf(1)] = p[Vbi.Audf(2)] = (byte)(0xFF - left);
        p[Vbi.Audc(1)] = p[Vbi.Audc(2)] = (byte)(((left >> 3) & 0x0F) | 0x80);
    }

    /// <summary>$7EA6: the mule jumps about its plot forty times (each jump a random spot and a random
    /// 0-19 steps, cycle-bound, then a frame), then it is lost and the production shows.</summary>
    static IEnumerable Radiation(Game game)
    {
        var rng = game.Rng;
        var r = Apply(game, RoundEventKind.Radiation);
        if (!r.Shown)
        {
            yield return GameLoop.AnimMapProd(game);
            yield break;
        }
        game.Emit(new TitleEvent("RADIATION"));
        game.Emit(new MessageEvent("MULE GOES CRAZY!"));
        int total = 0;
        // Each jump's sound on voice 1 ($7F4F): a buzz rising a step and a volume lower with every step,
        // silenced at the landing. The jumps are bound by the drawing, so the port spreads the
        // register settings evenly over its frames, as for the meteorite.
        // Each jump's settings at its time in frames: a step takes about a quarter of a frame, and after
        // the landing the next jump waits for the next frame (waitJiffy). The times are then scaled to
        // the frames the jumps take.
        var sound = new List<(double t, byte f, byte c)>();
        double t = 0;
        for (int k = 40; k > 0; k--)
        {
            rng.NextByte(); rng.NextByte(); rng.NextByte();
            int n = rng.Below(20);
            total += n;
            t += RadiationJumpStart;
            sound.Add((t, 0, 0xEC));
            for (int i = 1; i <= n; i++) sound.Add((t += RadiationStep, (byte)i, (byte)(0xEC - i)));
            sound.Add((t, 0, 0));
            t = Math.Floor(t) + 1;
        }
        var p = game.Vbi.Pokey;
        int frames = game.Think?.Invoke($"radiation {total}") ?? RadiationFrames(total);
        int at = 0;
        for (int f = 0; f < frames; f++)
        {
            double now = (f + 1) * t / frames;
            while (at < sound.Count && sound[at].t <= now) at++;
            if (at > 0) (p[Vbi.Audf(1)], p[Vbi.Audc(1)]) = (sound[at - 1].f, sound[at - 1].c);
            yield return null;
        }
        game.Trace?.Invoke("radiation");
        // sub_7FC6 and loc_6C9B: the mule walks off sideways, away from the middle, a step every six
        // frames (handler $0D), until it leaves the map.
        var v = game.Vbi;
        byte mx = (byte)(56 + r.Plot % 9 * 16 + 5), phase = 0, toggle = 0;
        p[Vbi.Audctl] = 0; // sub_1363
        v.Silence();
        v.Handler = () =>
        {
            if (++phase != 6) return;
            phase = 0;
            mx = mx < 0x7B ? (byte)(mx - 4) : (byte)(mx + 4);
            toggle ^= 8; // $5879: a footstep on voice 1
            p[Vbi.Audc(1)] = (byte)(toggle | 0xA0);
            game.Emit(new RoundEventStepEvent(RoundEventKind.Radiation, 0, mx));
        };
        while (mx >= 0x28 && mx < 0xDC) yield return null;
        v.Handler = null;
        v.Silence(); // $7FF0
        yield return GameLoop.AnimMapProd(game);
    }

    /// <summary>$7FF3: the production first; the store burns (four 60-frame flickers), 255 frames and the
    /// level wait. No production after it.</summary>
    static IEnumerable Fire(Game game)
    {
        yield return GameLoop.AnimMapProd(game);
        game.Emit(new TitleEvent("FIRE IN STORE"));
        game.Emit(new MessageEvent("ALL THE STOCK IN THE STORE IS LOST."));
        Apply(game, RoundEventKind.FireInStore);
        // A siren on voice 1: two notes, half a second each, four times.
        var p = game.Vbi.Pokey;
        p[Vbi.Audctl] = 0; // sub_1363
        game.Vbi.Silence();
        for (int k = 4; k > 0; k--)
        {
            p[Vbi.Audc(1)] = 0xA4;
            p[Vbi.Audf(1)] = 0x3C;
            yield return game.WaitJiffy(30);
            p[Vbi.Audf(1)] = 0x79;
            yield return game.WaitJiffy(30);
        }
        game.Vbi.Silence();
        yield return game.WaitJiffy(255);
        yield return game.WaitJiffyLevel();
    }
}
