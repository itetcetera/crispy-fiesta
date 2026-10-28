using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Plays whole rounds of CPU-only games on the scanline Atari and in the port and compares the
/// frame each phase starts on, the random reads and the state at the score.</summary>
public class GameLoopTests
{
    readonly ITestOutputHelper _out;
    public GameLoopTests(ITestOutputHelper o) => _out = o;

    static readonly int NoiseAhead = int.Parse(Environment.GetEnvironmentVariable("NOISE_AHEAD") ?? "1");
    static readonly bool SndDebug = Environment.GetEnvironmentVariable("SND_DEBUG") != null;
    static readonly HashSet<ushort> WampusRandomPcs = new() { 0x8BD6, 0x8C11, 0x119A };

    // The phases of round ($478A), as the port traces them.
    static readonly Dictionary<ushort, string> Checkpoints = new()
    {
        [0x478A] = "round", [0x4794] = "grant", [0x4797] = "sales", [0x47A5] = "no auction", [0x47CC] = "auction",
        [0x47E0] = "turns", [0x47E3] = "production", [0x47F0] = "ship back", [0x4800] = "round event", [0x4803] = "buttons",
        [0x481B] = "goods 2", [0x4820] = "goods 3", [0x482A] = "goods 0", [0x482F] = "goods 1", [0x4842] = "score", [0x47A8] = "no auction map", [0x47D2] = "map done", [0x4F00] = "grant wait", [0x47E6] = "prod calc", [0x47E9] = "prod done",
        [0x6EA5] = "score shown", [0x71B3] = "score walk", [0x6F19] = "score wait", [0x6F43] = "score press", [0x6F60] = "score end", [0x7F73] = "radiation", [0x7D35] = "meteorite", [0x4A9D] = "prod anim",
    };

    // The development turns' points (as in the turn tests).
    static readonly Dictionary<ushort, string> TurnPoints = new()
    {
        [0x6645] = "player", [0x6741] = "pressed", [0x66CB] = "event", [0x6A82] = "gamble", [0x6C71] = "timeout",
        [0x6AF2] = "end", [0x384E] = "install", [0x38CD] = "assay", [0x3908] = "sync",
    };

    static GameState Make(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 13 + 5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = 0xFF;
        g.MaxIndexCpu = 3;
        g.Level = 2;
        g.NbRounds = 12;
        g.NumRound = (byte)r.Next(0, 12);
        g.FoodReq = (byte)(g.NumRound + 1 <= 4 ? 3 : g.NumRound + 1 <= 8 ? 4 : 5);
        return g;
    }

    sealed record Run(List<(long Frame, string What)> Events, GameState End, int RandomReads, byte[] Timers)
    {
        public string Message { get; init; } = "";
        public List<(long Frame, string What)> Turn { get; init; } = new();
        public List<(long Frame, string What)> Auction { get; init; } = new();
        /// <summary>Each goods auction's trading points, as the goods auction tests record them.</summary>
        public List<List<string>> Trading { get; init; } = new();
        public List<string> Messages { get; init; } = new();
        /// <summary>The state at the end of each round's score.</summary>
        public List<GameState> Ends { get; init; } = new();
        /// <summary>The POKEY sound registers each time they change, by frame.</summary>
        public List<(long Frame, string What)> Sounds { get; init; } = new();
    }

    static readonly Dictionary<ushort, string> AuctionPoints = new()
    {
        [0x657B] = "select", [0x9FFA] = "trade", [0x8EAA] = "unit", [0x91F5] = "collusion", [0x952A] = "over", [0x9D6D] = "no sellers", [0x5892] = "bars", [0x6110] = "c1=0", [0x637C] = "requirement", [0x638F] = "press",
    };

    /// <summary>When set, the original's picture is saved there as PNGs: shortly after each phase point,
    /// and every 300 frames (reference shots for the client's screens).</summary>
    static readonly string? Shots = Environment.GetEnvironmentVariable("SHOTS");

    static Run RunOriginal(GameState g, int seed, bool whole = false)
    {
        var m = new MachineGame();
        if (Shots != null) { Directory.CreateDirectory(Shots); m.M.EnablePicture(); }
        var shotAt = new SortedDictionary<long, string>();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        m.M.CallRoutine(OriginalGame.Sym("initBB00ToBFFF"));
        m.M.CallRoutine(OriginalGame.Sym("drawMap"));
        m.Ram[0xC1] = 0xFF;
        m.M.Cpu.A = 1;
        m.M.CallRoutine(OriginalGame.Sym("waitJiffy"));
        var timers = new[] { m.Ram[0x14], m.Ram[0xC3], m.Ram[0xC4], m.Ram[0xC5], m.Ram[0xC6] };
        var rng = new ScriptedRandom(seed);
        m.Random = rng.NextByte;
        m.M.RandomOverride = pc => m.M.InVblank && WampusRandomPcs.Contains(pc) ? rng.NextByte() : null;
        var events = new List<(long, string)>();
        events.Add((0, "round"));
        string message = "";
        var messages = new List<string>();
        var ends = new List<GameState>();
        var turn = new List<(long, string)>();
        bool inTurns = false;
        var auction = new List<(long, string)>();
        bool inGoods = false, selected = false, traded = false;
        var trading = new List<List<string>>();
        string lastW = "", lastSnd = "";
        var sounds = new List<(long, string)>();
        temps = new(); clocks = new();
        long vbi = 0, lastPoint = 0;
        byte last9E = 0xFF;
        var lastRegs = new byte[9];
        ushort prevPc = 0, prevMain = 0;
        var ring = new ushort[64]; int ringAt = 0;
        m.M.AfterStep = c =>
        {
            bool afterRti = m.M.Read(prevPc) == 0x40;
            if (!m.M.InVblank) prevMain = c.PC;
            ring[ringAt++ & 63] = c.PC;
            if (SndDebug && !m.M.Pokey.AsSpan().SequenceEqual(lastRegs))
            {
                File.AppendAllText("/tmp/claude-0/sndw.txt", $"{vbi} {Convert.ToHexString(m.M.Pokey)} at {prevPc:X4} vbl {m.M.InVblank}\n");
                m.M.Pokey.CopyTo(lastRegs, 0);
            }
            if (SndDebug && m.Ram[0x9E] != last9E) { File.AppendAllText("/tmp/claude-0/snd9e.txt", $"{vbi} 9E {last9E:X2}->{m.Ram[0x9E]:X2} at {prevPc:X4} main {prevMain:X4} vbl {m.M.InVblank}\n"); last9E = m.Ram[0x9E]; }
            prevPc = c.PC;
            if (afterRti) return;
            if (c.PC == 0x5BB5)
            {
                // The screen saver (after $80 x 256 frames without a button held, $5C98) is left out
                // of the port (Marcus, 2026-10-27): as if someone held a button now and then.
                m.Ram[0x4D] = 0;
                if (Progress != null && vbi - lastPoint > 20_000)
                {
                    File.AppendAllText(Progress, $"stuck at {prevMain:X4}, frame {vbi}, S {c.S:X2}\n");
                    throw new TimeoutException($"original stuck at {prevMain:X4}");
                }
                if (inTurns)
                {
                    string w = $"w {m.Ram[0xD3]} {m.Ram[0xAD]}";
                    if (w != lastW && vbi > 0 && w.EndsWith(" 1")) { turn.Add((vbi, w)); lastW = w; }
                }
                if (Shots != null)
                {
                    if (vbi % 300 == 0) shotAt.TryAdd(vbi, $"t{vbi}");
                    if (shotAt.Remove(vbi, out var name))
                    {
                        File.WriteAllLines(Path.Combine(Shots, $"s{seed}_{vbi:D6}_{name.Replace(' ', '-')}.txt"), m.M.DebugDisplayList());
                        File.AppendAllText(Path.Combine(Shots, "pm.txt"), $"{vbi} {name} {m.M.DebugPm} pm {string.Join(",", Enumerable.Range(0, 5).Select(n => m.Ram.AsSpan(0xB800 + 0x300 + n * 0x100, 0x100).ToArray().Count(b => b != 0)))}\n");
                        Png.Write(Path.Combine(Shots, $"s{seed}_{vbi:D6}_{name.Replace(' ', '-')}.png"), m.M.Picture!, Mule.Atari.Atari800.PictureWidth, Mule.Atari.Atari800.PictureHeight);
                    }
                }
                string snd = Convert.ToHexString(m.M.Pokey);
                if (snd != lastSnd && vbi > 0) { sounds.Add((vbi, snd)); lastSnd = snd; }
                vbi++;
                return;
            }
            if (TurnPoints.TryGetValue(c.PC, out var tp) && inTurns)
                turn.Add((vbi, tp switch
                {
                    "player" => $"player {m.Ram[0xB328 + m.Ram[0xB378]]}",
                    "install" => $"install {m.Ram[0xB324]} {c.X}",
                    "assay" => $"assay {m.Ram[0xCB]}",
                    _ => tp,
                }));
            if (c.PC == 0x47E0) inTurns = true;
            if (c.PC == 0x47E3) inTurns = false;
            if (c.PC is 0x481B or 0x4820 or 0x482A or 0x482F) { inGoods = true; selected = traded = false; auction.Add((vbi, "goods")); trading.Add(new()); }
            if (c.PC == 0x4842) inGoods = false;
            if (inGoods)
            {
                var r0 = m.Ram;
                string? d = c.PC switch
                {
                    0x657B when !selected => "select",
                    0x9FFA when !traded => "trade",
                    0x8EAA => "unit",
                    0x9B47 => $"rnd vbl {r0[0x93]:X2} {r0[0xA9]:X2} {r0[0xAE]:X2} {r0[0xDC]:X2} {r0[0xDD]:X2} {r0[0xC5]:X2} {r0[0x14]:X2}",
                    0x9147 => "rnd quitb",
                    0x9150 => "rnd quits",
                    0x9159 => "rnd notices",
                    _ => null,
                };
                if (d != null) trading[^1].Add($"{vbi} {d}");
            }
            if (inGoods && AuctionPoints.TryGetValue(c.PC, out var ap)
                && !(ap == "select" && selected) && !(ap == "trade" && traded))
            {
                if (ap == "select") selected = true;
                if (ap == "trade") traded = true;
                auction.Add((vbi, ap));
            }
            if (c.PC == 0x6F19) { message = Decode(m.Ram); messages.Add(message); }
            if (c.PC == 0x6F60) ends.Add(StateMapper.FromRam(m.Ram));
            if (c.PC == 0x7D35 && events[^1].Item2 == "meteorite") return; // the fifteen waits after the fall
            if (Checkpoints.TryGetValue(c.PC, out var what)) { lastPoint = vbi; if (Progress != null && what is "round" or "score end") File.AppendAllText(Progress, $"original {what} {vbi} round {m.Ram[0xB1]}\n"); events.Add((vbi, what)); if (Shots != null) { shotAt.TryAdd(vbi + 20, what); shotAt.TryAdd(vbi + 120, what + "+120"); } clocks.Add($"{what} {m.Ram[0x14]} {m.Ram[0xC3]} {m.Ram[0xC4]} {m.Ram[0xC5]} {m.Ram[0xC6]}"); temps.Add($"{what}:{string.Join(",", m.Ram.AsSpan(0xB42D, 45).ToArray())}"); }
        };
        // The round ends at the score: return there instead of going on to the next round.
        if (!whole) m.Ram[0x4845] = 0x60;
        else
        {
            // After the last round, or when the colony fails, return instead of going back to the start.
            byte s = (byte)(m.M.Cpu.S - 2);
            m.Ram[0x484F] = 0xA2; m.Ram[0x4850] = s; m.Ram[0x4851] = 0x9A; m.Ram[0x4852] = 0x60; // LDX #s, TXS, RTS
            m.Ram[0x8ADE] = 0x4C; m.Ram[0x8ADF] = 0x4F; m.Ram[0x8AE0] = 0x48;                   // JMP $484F
        }
        try { m.M.CallRoutine(0x478A, whole ? 4_000_000 : 400_000); }
        catch (TimeoutException) when (Progress != null)
        {
            File.AppendAllText(Progress, $"timeout: vbi {vbi}, PC {m.M.Cpu.PC:X4}, S {m.M.Cpu.S:X2}, last {string.Join(" ", Enumerable.Range(0, 64).Select(k => ring[(ringAt + k) & 63].ToString("X4")))}\n");
            throw;
        }
        return new Run(events, StateMapper.FromRam(m.Ram), rng.Position, timers) { Message = message, Turn = turn, Auction = auction, Trading = trading, Messages = messages, Ends = ends, Sounds = sounds };
    }

    /// <summary>The scrolling message buffer at $B700, up to its end mark.</summary>
    // Frames of CPU-bound drawing and working out the port estimates may differ by (each measured
    // from the point before, so they do not add up).
    const int Tolerance = 2;

    /// <summary>The largest difference in frames between consecutive points of the two runs, or -1 when
    /// the points themselves differ.</summary>
    static int Slip(List<(long Frame, string What)> o, List<(long Frame, string What)> p)
    {
        if (!o.Select(e => e.What).SequenceEqual(p.Select(e => e.What))) return -1;
        int worst = 0;
        for (int i = 1; i < o.Count; i++)
            worst = Math.Max(worst, (int)Math.Abs((o[i].Frame - o[i - 1].Frame) - (p[i].Frame - p[i - 1].Frame)));
        return worst;
    }

    /// <summary>The points of the <paramref name="n"/>th goods auction.</summary>
    static List<(long Frame, string What)> AuctionOf(List<(long Frame, string What)> l, int n)
    {
        int start = -1, k = -1;
        for (int i = 0; i < l.Count; i++) if (l[i].What == "goods" && ++k == n) { start = i; break; }
        if (start < 0) return new();
        int end = l.FindIndex(start + 1, e => e.What == "goods");
        return l.GetRange(start, (end < 0 ? l.Count : end) - start);
    }

    static string Decode(byte[] r)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < 256 && r[0xB700 + i] != 0x80) i++;
        if (i == 256) return ""; // never ended: not shown
        for (int k = 0x14; k < i; k++) sb.Append(r[0xB700 + k] == 0 ? ' ' : (char)(r[0xB700 + k] - 0x20));
        return sb.ToString().TrimEnd();
    }

    static readonly Dictionary<string, string> ThinkTargets = new()
    {
        ["grant intro"] = "grant wait", ["no auction map"] = "no auction map", ["map done"] = "map done", ["prod calc"] = "prod calc", ["production"] = "prod done", ["score calc"] = "score shown", ["score draw"] = "score walk",
        ["score text"] = "score wait", ["score press"] = "score press", ["round start"] = "@next", ["prices"] = "goods 2", ["meteorite"] = "meteorite", ["radiation"] = "radiation",
    };

    static readonly List<string> Thinks = new();
    static readonly List<string> Kinds = new();
    static readonly string? Progress = Environment.GetEnvironmentVariable("GAME_PROGRESS");
    static List<string> temps = new(), ptemps = new(), clocks = new(), pclocks = new();

    static Run RunPort(GameState g, int seed, byte[] timers, List<(long Frame, string What)>? original, List<(long Frame, string What)>? at = null, Run? orig = null, bool whole = false)
    {
        var messages = new List<string>();
        var ends = new List<GameState>();
        var score = new ScoreScreen.Outcome();
        var c = new Cpu6502();
        StateMapper.ToRam(g, c);
        var state = StateMapper.FromRam(c);
        var rng = new ScriptedRandom(seed);
        var game = new Game(state, rng);
        long VFrame() => game.Frame - game.HeldFrames;
        game.Vbi.RtClock = timers[0];
        if (orig != null)
        {
            // The hardware noise read in a blank (acid rain's thunder): the original's choice, from its AUDC2
            // as it stands after this blank.
            int noiseCalls = 0;
            game.Vbi.Noise = () =>
            {
                if (noiseCalls++ % 2 == 1) return 0;
                long f = VFrame() + NoiseAhead;
                var e = orig.Sounds.LastOrDefault(x => x.Frame <= f);
                return e.What != null && e.What.Substring(6, 2) == "0E" ? (byte)0 : (byte)1;
            };
        }
        for (int i = 0; i < 4; i++) game.Vbi.Timers[i] = timers[i + 1];
        var names = new HashSet<string>(Checkpoints.Values);
        var events = new List<(long, string)>();
        var turn = new List<(long, string)>();
        var auction = new List<(long, string)>();
        bool inTurns = false, inGoods = false;
        int goodsIndex = -1;
        long trade = -1;
        var oracles = new Dictionary<int, Func<string, bool>>();
        // An auction point of the original, from the auction's last <paramref name="anchor"/>: its frame there, and the port's.
        (long Original, long Port)? Since(string anchor, string target)
        {
            if (orig == null) return null;
            var oa = AuctionOf(orig.Auction, goodsIndex);
            var pa = AuctionOf(auction, goodsIndex);
            int pi = pa.FindLastIndex(e => e.What == anchor);
            if (pi < 0) return null;
            int nth = pa.Count(e => e.What == anchor) - 1;
            var oas = oa.Select((e, i) => (e, i)).Where(x => x.e.What == anchor).Skip(nth).FirstOrDefault();
            if (oas.e.What == null) return null;
            var ot = oa.Skip(oas.i).FirstOrDefault(e => e.What == target);
            if (ot.What == null) return null;
            return (ot.Frame - oas.e.Frame, VFrame() - pa[pi].Frame);
        }
        ptemps = new(); pclocks = new();
        game.Trace = e =>
        {
            if (Progress != null && e is "round" or "score end") File.AppendAllText(Progress, $"port {e} {VFrame()} round {state.NumRound}\n");
            if (e == "score wait") messages.Add(score.Message.TrimEnd());
            if (e == "score end") { var copy = new Cpu6502(); StateMapper.ToRam(state, copy); ends.Add(StateMapper.FromRam(copy)); }
            if (names.Contains(e)) { events.Add((VFrame(), e)); pclocks.Add($"{e} {game.Vbi.RtClock} {game.Vbi.Timers[0]} {game.Vbi.Timers[1]} {game.Vbi.Timers[2]} {game.Vbi.Timers[3]}"); ptemps.Add($"{e}:{string.Join(",", state.PlotsTemp3)}"); }
            if (e == "turns") inTurns = true;
            if (e == "production") inTurns = false;
            if (e.StartsWith("goods ")) { inGoods = true; auction.Add((VFrame(), "goods")); goodsIndex++; trade = -1; }
            if (e == "trade") trade = game.Frame;
            if (e == "score") inGoods = false;
            var w0 = e.StartsWith("unit") ? "unit" : e;
            if (e.StartsWith("kind ")) Kinds.Add(e);
            if (inGoods && (e is "select" or "c1=0" or "trade" or "collusion" or "over" or "no sellers" || w0 == "unit")) auction.Add((VFrame(), w0));
            if (inTurns && (e.StartsWith("player") || e.StartsWith("install") || e.StartsWith("assay") || e is "sync" or "pressed" or "event" or "gamble" or "timeout" or "end"))
                turn.Add((VFrame(), e));
        };
        if (at != null)
        {
            IEnumerable<(long Frame, string What)> FromPlayer() => at.Skip(at.FindLastIndex(e => e.Frame <= VFrame() && e.What.StartsWith("player")));
            game.Race = q => q switch
            {
                "select late" => Since("press", "select") is { } sl && sl.Port < sl.Original,
                "tick first" or "slow tick" or "slow unit" or "quit late" or "unit first" when orig != null && goodsIndex >= 0 && trade >= 0 =>
                    (oracles.TryGetValue(goodsIndex, out var or) ? or : oracles[goodsIndex] = GoodsAuctionTests.Oracle(game, () => trade, orig.Trading[goodsIndex]))(q),
                "title quick" => FromPlayer().First(e => e.What == "event").Frame == VFrame() + 6,
                "player late" => VFrame() > 0 && FromPlayer().Skip(1).TakeWhile(e => e.What != "event").FirstOrDefault(e => e.What.StartsWith("w ") && e.What.EndsWith(" 1")).Frame > VFrame(),
                "map late" => at.First(e => e.Frame > VFrame() && e.What.StartsWith("w ") && e.What.Split(' ')[2] == "1").Frame > VFrame() + 25,
                "sync late" => at.Skip(turn.Count).First(e => e.What == "sync").Frame > VFrame(),
                _ when q.StartsWith("time late ") => at.Skip(turn.Count).First(e => e.What.StartsWith("w ") && e.What.Split(' ')[1] == q[10..]).Frame > VFrame(),
                "gamble late" => at.First(e => e.Frame >= VFrame() && e.What == "end").Frame > VFrame() + 277,
                "event title late" => orig != null && orig.Sounds.FirstOrDefault(e => e.Frame >= VFrame()).Frame > VFrame(),
                "rain title late" => orig != null && orig.Sounds.FirstOrDefault(e => e.Frame >= VFrame()).Frame > VFrame() + 1,
                "prod title late" => orig != null && orig.Sounds.FirstOrDefault(e => e.Frame > VFrame() && e.What.StartsWith("C8")).Frame > VFrame() + 6,
                "no plots late" => orig != null && orig.Sounds.FirstOrDefault(e => e.Frame > VFrame()).Frame > VFrame() + 1,
                _ => false,
            };
        }
        if (original != null)
            // Times the port estimates (CPU work and drawing) are taken from the original.
            game.Think = q =>
            {
                if ((q.StartsWith("install") || q.StartsWith("assay")) && at != null)
                {
                    var (f, e) = at.Skip(turn.Count).FirstOrDefault(x => !x.What.StartsWith("w "));
                    if (e == null || !(e.StartsWith("install") || e.StartsWith("assay") || e == "gamble" || e == "timeout")) return null;
                    return (int)(f - VFrame());
                }
                if (q is "status draw" && Since("goods", "c1=0") is { } sd) return (int)(sd.Original - sd.Port);
                if (q is "trade draw" && Since("select", "trade") is { } td) return (int)(td.Original - td.Port);
                if (!ThinkTargets.TryGetValue(q.Split(' ')[0], out var target) && !ThinkTargets.TryGetValue(q, out target)) return null;
                int i = target == "@next" ? events.Count : original.FindIndex(events.Count, e => e.What == target);
                if (i < 0) return null;
                int n = (int)(original[i].Frame - VFrame());
                Thinks.Add($"{q} {n}");
                return n;
            };
        game.StartNow(whole ? GameLoop.Rounds(game, score) : GameLoop.Round(game, score));
        var input = new PlayerInput[4];
        for (int p = 0; p < 4; p++) input[p] = new PlayerInput(false, 15);
        string lastW = "", lastSnd = "";
        var sounds = new List<(long, string)>();
        int seenEv = 0;
        while (!game.Finished)
        {
            game.RunFrame(input);
            string snd = Convert.ToHexString(game.Vbi.Pokey);
            if (snd != lastSnd && !game.Finished) { sounds.Add((VFrame(), snd)); lastSnd = snd; }
            for (; seenEv < game.Events.Count; seenEv++)
                if (inGoods)
                    switch (game.Events[seenEv])
                    {
                        case GoodsBarsEvent: auction.Add((VFrame(), "bars")); break;
                        case GoodsRequirementEvent: auction.Add((VFrame(), "requirement")); break;
                        case MessageEvent { Text: "PRESS BUTTON TO CONTINUE" }: auction.Add((VFrame(), "press")); break;
                    }
            if (inTurns && game.Turn is { } t && !game.Finished)
            {
                string w = $"w {t.Time} {t.Map}";
                if (w != lastW && w.EndsWith(" 1")) { turn.Add((VFrame(), w)); lastW = w; }
            }
            if (VFrame() > 4_000_000) throw new TimeoutException("port did not finish");
        }
        return new Run(events, state, rng.Position, timers) { Message = score.Message.TrimEnd(), Turn = turn, Auction = auction, Messages = messages, Ends = ends, Sounds = sounds };
    }

    [Fact]
    public void Round_matches()
    {
        int count = int.Parse(Environment.GetEnvironmentVariable("ROUND_SEEDS") ?? "4");
        int from = int.Parse(Environment.GetEnvironmentVariable("ROUND_FROM") ?? "0");
        var failures = new List<string>();
        for (int seed = from; seed < from + count; seed++)
        {
            var g = Make(seed);
            var o = RunOriginal(g, seed);
            var ot = temps;
            Kinds.Clear();
            var p = RunPort(g, seed, o.Timers, o.Events, o.Turn, o);
            ptemps.RemoveAt(0); // the port's "round"
            pclocks.RemoveAt(0);
            for (int k = 0; k < Math.Min(clocks.Count, pclocks.Count); k++)
                if (clocks[k] != pclocks[k]) { _out.WriteLine($"  clocks first differ: original {clocks[k]}, port {pclocks[k]}\n    {string.Join(" | ", clocks.Skip(Math.Max(0, k - 2)).Take(4))}\n    {string.Join(" | ", pclocks.Skip(Math.Max(0, k - 2)).Take(4))}"); break; }
            for (int k = 0; k < Math.Min(ot.Count, ptemps.Count); k++)
                if (ot[k] != ptemps[k]) { _out.WriteLine($"  plotsTemp3 first differs at {ot[k]}\n    port {ptemps[k]}"); break; }
            static List<(long, string)> Settled(List<(long Frame, string What)> l)
            {
                // The time and map flag left from before each turn until its event shows.
                var r = new List<(long, string)>();
                bool before = false;
                foreach (var e in l)
                {
                    if (e.What.StartsWith("player")) before = true;
                    if (e.What == "event") before = false;
                    if (!(before && e.What.StartsWith("w "))) r.Add(e);
                }
                return r;
            }
            var (oTurn, pTurn) = (Settled(o.Turn), Settled(p.Turn));
            int ti = 0;
            while (ti < oTurn.Count && ti < pTurn.Count && oTurn[ti] == pTurn[ti]) ti++;
            // What the CPUs did, and the time left after each action, must be the same; a frame or two
            // of drawing time may differ (each point is taken from the one before).
            var oActs = oTurn.Where(e => !e.Item2.StartsWith("w ")).ToList();
            var pActs = pTurn.Where(e => !e.Item2.StartsWith("w ")).ToList();
            bool turnsDiffer = Slip(oActs, pActs) is not (>= 0 and <= Tolerance)
                || !oTurn.Where(e => e.Item2.StartsWith("w ")).Select(e => e.Item2).SequenceEqual(pTurn.Where(e => e.Item2.StartsWith("w ")).Select(e => e.Item2));
            if (turnsDiffer)
                _out.WriteLine($"  turns differ at {ti}: original {string.Join(", ", oTurn.Skip(Math.Max(0, ti - 3)).Take(6))}\n    port {string.Join(", ", pTurn.Skip(Math.Max(0, ti - 3)).Take(6))}");
            {
                // The goods auctions, each from its start.
                long o0 = 0, p0 = 0;
                int n = Math.Min(o.Auction.Count, p.Auction.Count);
                for (int k = 0; k < n; k++)
                {
                    var (fo, wo) = o.Auction[k];
                    var (fp, wp) = p.Auction[k];
                    if (wo == "goods") { o0 = fo; p0 = fp; }
                    if (wo != wp || fo - o0 != fp - p0)
                    {
                        _out.WriteLine($"  auction differs at {k}: original {wo}@{fo - o0}, port {wp}@{fp - p0} ({string.Join(", ", o.Auction.Skip(Math.Max(0, k - 3)).Take(5).Select(e => $"{e.What}@{e.Frame}"))} | {string.Join(", ", p.Auction.Skip(Math.Max(0, k - 3)).Take(5).Select(e => $"{e.What}@{e.Frame}"))})");
                        // Re-anchor and go on.
                        o0 = fo; p0 = fp;
                    }
                }
                if (o.Auction.Count != p.Auction.Count) _out.WriteLine($"  auction events {o.Auction.Count} vs {p.Auction.Count}");
            }
            string F(List<(long Frame, string What)> l) => string.Join(", ", l.Select(e => $"{e.What}@{e.Frame}"));
            _out.WriteLine($"seed {seed} round {g.NumRound + 1} {string.Join(",", Kinds)}\n  original: {F(o.Events)}\n  port:     {F(p.Events)}\n  random {o.RandomReads} vs {p.RandomReads}");
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen");
            foreach (var d in diff.Take(10)) _out.WriteLine("  " + d);
            _out.WriteLine($"  message original: {o.Message}\n  message port: {p.Message}");
            int slip = Slip(o.Events, p.Events);
            _out.WriteLine($"  largest slip {slip} frames");
            string? sound = SoundDiff(o, p);
            if (sound != null) _out.WriteLine("  sound " + sound);
            if (sound != null && sound.Contains("sound mismatch")) failures.Add($"seed {seed} sound");
            if (slip is not (>= 0 and <= Tolerance) || diff.Count > 0 || o.RandomReads != p.RandomReads || o.Message != p.Message || turnsDiffer) failures.Add($"seed {seed}");
        }
        _out.WriteLine(string.Join("\n", Thinks));
        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }

    /// <summary>Where the sound first differs: each change of the registers, counted in frames from the
    /// phase point before it.</summary>
    static string? SoundDiff(Run o, Run p)
    {
        if (SndDebug)
        {
            File.WriteAllLines("/tmp/claude-0/snd_o.txt", Rel(o));
            File.WriteAllLines("/tmp/claude-0/snd_p.txt", Rel(p));
        }
        static List<string> Rel(Run r)
        {
            var l = new List<string>();
            int k = 0;
            foreach (var (f, s) in r.Sounds)
            {
                while (k + 1 < r.Events.Count && r.Events[k + 1].Frame <= f) k++;
                l.Add($"{r.Events[k].What}+{f - r.Events[k].Frame} {s}");
            }
            return l;
        }
        var (a, b) = (Rel(o), Rel(p));
        int i = 0;
        while (i < a.Count && i < b.Count && a[i] == b[i]) i++;
        if (i == a.Count && i == b.Count) return null;
        string first = $"differs at change {i} of {a.Count}/{b.Count}:\n    " + string.Join("\n    ", Enumerable.Range(Math.Max(0, i - 3), 8)
            .Select(k => $"{(k < a.Count ? a[k] : ""),-40} | {(k < b.Count ? b[k] : "")}"));
        var bad = SoundMismatch(o, p);
        return bad == null ? null : first + "\n  " + bad;
    }

    /// <summary>The registers frame by frame, each phase from its start: they must be the same, except in
    /// the land auction, whose main loop is bound by the drawing (a tick is taken a frame early or late,
    /// docs/PLAN.md), where each frame's registers must be found within 8 frames on the other side, and a few frames (3%) may differ. Null when they match.</summary>
    static string? SoundMismatch(Run o, Run p)
    {
        if (o.Events.Count != p.Events.Count) return "phases differ";
        // What is heard: a voice at volume 0 is silent whatever its frequency and distortion.
        static string Audible(string hex)
        {
            var c = hex.ToCharArray();
            for (int v = 0; v < 4; v++)
                if (c[v * 4 + 3] == '0')
                    for (int k = 0; k < 4; k++) c[v * 4 + k] = '0';
            return new string(c);
        }
        string State(Run r, long f)
        {
            var e = r.Sounds.LastOrDefault(x => x.Frame <= f);
            return Audible(e.What ?? "000000000000000000");
        }
        var bad = new List<string>();
        for (int k = 0; k < o.Events.Count; k++)
        {
            long os = o.Events[k].Frame, ps = p.Events[k].Frame;
            long oe = k + 1 < o.Events.Count ? o.Events[k + 1].Frame : o.Sounds.Count > 0 ? Math.Max(os, o.Sounds[^1].Frame + 1) : os;
            long pe = k + 1 < p.Events.Count ? p.Events[k + 1].Frame : p.Sounds.Count > 0 ? Math.Max(ps, p.Sounds[^1].Frame + 1) : ps;
            long len = Math.Min(oe - os, pe - ps);
            if (len <= 0) continue;
            // The meteorite's fall and the radiation's jumps are bound by the drawing: the port spreads
            // their sound over the frames they take, so only their length is checked (by the phases).
            if (o.Events[k].What == "round event" && Kinds.Any(e => e.Contains("Meteorite") || e.Contains("Radiation"))) continue;
            // The land auctions' main loop is bound by the drawing: its ticks, and each next auction's
            // start, can be a few frames off within the phase.
            int w = o.Events[k].What == "auction" ? 8 : 0;
            var so = Enumerable.Range(0, (int)len + 2 * w).Select(t => State(o, os + t - w)).ToArray();
            var sp = Enumerable.Range(0, (int)len + 2 * w).Select(t => State(p, ps + t - w)).ToArray();
            int miss = 0, firstMiss = -1;
            for (int t = 0; t < len; t++)
            {
                bool Near(string[] other, string x)
                {
                    for (int d = 0; d <= 2 * w; d++) if (other[t + d] == x) return true;
                    return false;
                }
                if (!Near(sp, so[t + w]) || !Near(so, sp[t + w]))
                {
                    miss++;
                    if (firstMiss < 0) firstMiss = t;
                }
            }
            if (miss > (w == 0 ? 0 : len * 3 / 100))
                bad.Add($"{o.Events[k].What}: {miss} of {len} frames, first +{firstMiss} ({State(o, os + firstMiss)} | {State(p, ps + firstMiss)})");
        }
        return bad.Count == 0 ? null : "sound mismatch: " + string.Join("; ", bad);
    }

    /// <summary>A new tournament game with four CPUs, from the first round to the last (or a failed colony).
    /// About 40 minutes a game: set GAME_SEEDS to run any.</summary>
    static GameState NewGame(int seed)
    {
        var races = new[] { Race.Mechtron, Race.Bonzoid, Race.Mechtron, Race.Bonzoid };
        var colors = new byte[] { 0x46, 0x86, 0xC6, 0x26 };
        var seats = Enumerable.Range(0, 4).Select(p => new SeatSetup(true, races[p], colors[p])).ToList();
        var rng = new ScriptedRandom(seed + 1000);
        var g = Setup.NewGame(rng, seats);
        Scoring.CalcScore(g, rng); // the score shown on landing
        return g;
    }

    [Fact]
    public void Game_matches()
    {
        int count = int.Parse(Environment.GetEnvironmentVariable("GAME_SEEDS") ?? "0");
        int from = int.Parse(Environment.GetEnvironmentVariable("GAME_FROM") ?? "0");
        var failures = new List<string>();
        for (int seed = from; seed < from + count; seed++)
        {
            var g = NewGame(seed);
            var o = RunOriginal(g, seed, whole: true);
            Kinds.Clear();
            var p = RunPort(g, seed, o.Timers, o.Events, o.Turn, o, whole: true);
            int slip = Slip(o.Events, p.Events);
            int rounds = o.Ends.Count;
            _out.WriteLine($"seed {seed}: {rounds} rounds in the original, {p.Ends.Count} in the port; events {Kinds.Count}: {string.Join(",", Kinds)}; largest slip {slip}; random {o.RandomReads} vs {p.RandomReads}");
            if (slip < 0)
            {
                int k = 0;
                while (k < Math.Min(o.Events.Count, p.Events.Count) && o.Events[k].What == p.Events[k].What) k++;
                _out.WriteLine($"  points differ at {k}: {string.Join(", ", o.Events.Skip(Math.Max(0, k - 3)).Take(6))}\n    port {string.Join(", ", p.Events.Skip(Math.Max(0, k - 3)).Take(6))}");
            }
            bool bad = slip is not (>= 0 and <= Tolerance) || rounds != p.Ends.Count || o.RandomReads != p.RandomReads;
            for (int r = 0; r < Math.Min(rounds, p.Ends.Count); r++)
            {
                var diff = StateMapper.Diff(o.Ends[r], p.Ends[r], "mapScreen");
                if (diff.Count > 0) { _out.WriteLine($"  round {r + 1}: " + string.Join("; ", diff.Take(8))); bad = true; break; }
                if (o.Messages[r] != p.Messages[r]) { _out.WriteLine($"  round {r + 1} message: '{o.Messages[r]}' vs '{p.Messages[r]}'"); bad = true; break; }
            }
            var final = StateMapper.Diff(o.End, p.End, "mapScreen");
            foreach (var d in final.Take(8)) _out.WriteLine("  end " + d);
            if (final.Count > 0) bad = true;
            _out.WriteLine($"  last message: {o.Messages.LastOrDefault()}");
            if (bad) failures.Add($"seed {seed}");
        }
        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }
}
