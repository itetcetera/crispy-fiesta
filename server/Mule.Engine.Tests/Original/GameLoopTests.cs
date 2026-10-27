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

    static readonly HashSet<ushort> WampusRandomPcs = new() { 0x8BD6, 0x8C11, 0x119A };

    // The phases of round ($478A), as the port traces them.
    static readonly Dictionary<ushort, string> Checkpoints = new()
    {
        [0x478A] = "round", [0x4794] = "grant", [0x4797] = "sales", [0x47A5] = "no auction", [0x47CC] = "auction",
        [0x47E0] = "turns", [0x47E3] = "production", [0x47F0] = "ship back", [0x4800] = "round event", [0x4803] = "buttons",
        [0x481B] = "goods 2", [0x4820] = "goods 3", [0x482A] = "goods 0", [0x482F] = "goods 1", [0x4842] = "score", [0x47A8] = "no auction map", [0x47D2] = "map done", [0x4F00] = "grant wait", [0x47E6] = "prod calc", [0x47E9] = "prod done",
        [0x6EA5] = "score shown", [0x71B3] = "score walk", [0x6F19] = "score wait", [0x6F60] = "score end", [0x7F73] = "radiation", [0x7D35] = "meteorite",
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
    }

    static Run RunOriginal(GameState g, int seed)
    {
        var m = new MachineGame();
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
        var turn = new List<(long, string)>();
        bool inTurns = false;
        string lastW = "";
        temps = new();
        long vbi = 0;
        ushort prevPc = 0;
        m.M.AfterStep = c =>
        {
            bool afterRti = m.M.Read(prevPc) == 0x40;
            prevPc = c.PC;
            if (afterRti) return;
            if (c.PC == 0x5BB5)
            {
                if (inTurns)
                {
                    string w = $"w {m.Ram[0xD3]} {m.Ram[0xAD]}";
                    if (w != lastW && vbi > 0 && w.EndsWith(" 1")) { turn.Add((vbi, w)); lastW = w; }
                }
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
            if (c.PC == 0x6F19) message = Decode(m.Ram);
            if (c.PC == 0x7D35 && events[^1].Item2 == "meteorite") return; // the fifteen waits after the fall
            if (Checkpoints.TryGetValue(c.PC, out var what)) { events.Add((vbi, what)); temps.Add($"{what}:{string.Join(",", m.Ram.AsSpan(0xB42D, 45).ToArray())}"); }
        };
        // The round ends at the score: return there instead of going on to the next round.
        m.Ram[0x4845] = 0x60;
        m.M.CallRoutine(0x478A, 400_000);
        return new Run(events, StateMapper.FromRam(m.Ram), rng.Position, timers) { Message = message, Turn = turn };
    }

    /// <summary>The scrolling message buffer at $B700, up to its end mark.</summary>
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
        ["score text"] = "score wait", ["round start"] = "@next", ["prices"] = "goods 2", ["meteorite"] = "meteorite", ["radiation"] = "radiation",
    };

    static readonly List<string> Thinks = new();
    static List<string> temps = new(), ptemps = new();

    static Run RunPort(GameState g, int seed, byte[] timers, List<(long Frame, string What)>? original, List<(long Frame, string What)>? at = null)
    {
        var c = new Cpu6502();
        StateMapper.ToRam(g, c);
        var state = StateMapper.FromRam(c);
        var rng = new ScriptedRandom(seed);
        var game = new Game(state, rng);
        long VFrame() => game.Frame - game.HeldFrames;
        game.Vbi.RtClock = timers[0];
        for (int i = 0; i < 4; i++) game.Vbi.Timers[i] = timers[i + 1];
        var names = new HashSet<string>(Checkpoints.Values);
        var events = new List<(long, string)>();
        var turn = new List<(long, string)>();
        bool inTurns = false;
        ptemps = new();
        game.Trace = e =>
        {
            if (names.Contains(e)) { events.Add((VFrame(), e)); ptemps.Add($"{e}:{string.Join(",", state.PlotsTemp3)}"); }
            if (e == "turns") inTurns = true;
            if (e == "production") inTurns = false;
            if (inTurns && (e.StartsWith("player") || e.StartsWith("install") || e.StartsWith("assay") || e is "sync" or "pressed" or "event" or "gamble" or "timeout" or "end"))
                turn.Add((VFrame(), e));
        };
        if (at != null)
        {
            IEnumerable<(long Frame, string What)> FromPlayer() => at.Skip(at.FindLastIndex(e => e.Frame <= VFrame() && e.What.StartsWith("player")));
            game.Race = q => q switch
            {
                "title quick" => FromPlayer().First(e => e.What == "event").Frame == VFrame() + 6,
                "player late" => VFrame() > 0 && FromPlayer().Skip(1).TakeWhile(e => e.What != "event").FirstOrDefault(e => e.What.StartsWith("w ") && e.What.EndsWith(" 1")).Frame > VFrame(),
                "map late" => at.First(e => e.Frame > VFrame() && e.What.StartsWith("w ") && e.What.Split(' ')[2] == "1").Frame > VFrame() + 25,
                "sync late" => at.Skip(turn.Count).First(e => e.What == "sync").Frame > VFrame(),
                _ when q.StartsWith("time late ") => at.Skip(turn.Count).First(e => e.What.StartsWith("w ") && e.What.Split(' ')[1] == q[10..]).Frame > VFrame(),
                "gamble late" => at.First(e => e.Frame >= VFrame() && e.What == "end").Frame > VFrame() + 277,
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
                if (!ThinkTargets.TryGetValue(q.Split(' ')[0], out var target) && !ThinkTargets.TryGetValue(q, out target)) return null;
                int i = target == "@next" ? events.Count : original.FindIndex(events.Count, e => e.What == target);
                if (i < 0) return null;
                int n = (int)(original[i].Frame - VFrame());
                Thinks.Add($"{q} {n}");
                return n;
            };
        var score = new ScoreScreen.Outcome();
        game.StartNow(GameLoop.Round(game, score));
        var input = new PlayerInput[4];
        for (int p = 0; p < 4; p++) input[p] = new PlayerInput(false, 15);
        string lastW = "";
        while (!game.Finished)
        {
            game.RunFrame(input);
            if (inTurns && game.Turn is { } t && !game.Finished)
            {
                string w = $"w {t.Time} {t.Map}";
                if (w != lastW && w.EndsWith(" 1")) { turn.Add((VFrame(), w)); lastW = w; }
            }
            if (VFrame() > 400_000) throw new TimeoutException("port did not finish");
        }
        return new Run(events, state, rng.Position, timers) { Message = score.Message.TrimEnd(), Turn = turn };
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
            var p = RunPort(g, seed, o.Timers, o.Events, o.Turn);
            ptemps.RemoveAt(0); // the port's "round"
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
            bool turnsDiffer = ti < Math.Max(oTurn.Count, pTurn.Count);
            if (turnsDiffer)
                _out.WriteLine($"  turns differ at {ti}: original {string.Join(", ", oTurn.Skip(Math.Max(0, ti - 3)).Take(6))}\n    port {string.Join(", ", pTurn.Skip(Math.Max(0, ti - 3)).Take(6))}");
            string F(List<(long Frame, string What)> l) => string.Join(", ", l.Select(e => $"{e.What}@{e.Frame}"));
            _out.WriteLine($"seed {seed} round {g.NumRound + 1}\n  original: {F(o.Events)}\n  port:     {F(p.Events)}\n  random {o.RandomReads} vs {p.RandomReads}");
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen");
            foreach (var d in diff.Take(10)) _out.WriteLine("  " + d);
            _out.WriteLine($"  message original: {o.Message}\n  message port: {p.Message}");
            if (!o.Events.SequenceEqual(p.Events) || diff.Count > 0 || o.RandomReads != p.RandomReads || o.Message != p.Message || turnsDiffer) failures.Add($"seed {seed}");
        }
        _out.WriteLine(string.Join("\n", Thinks));
        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }
}
