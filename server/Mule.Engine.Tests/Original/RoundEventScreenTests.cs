using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Runs each round event with its animation on the scanline Atari and in the port, and compares
/// the frame the production starts and the event ends on, the random reads and the state.</summary>
public class RoundEventScreenTests
{
    readonly ITestOutputHelper _out;
    public RoundEventScreenTests(ITestOutputHelper o) => _out = o;

    static readonly Dictionary<ushort, string> Checkpoints = new()
    {
        [0x4A9D] = "production", [0x7F73] = "radiation", [0x7D35] = "meteorite",
    };

    static GameState Make(int seed, int kind)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 13 + 5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = 0xFF;
        g.MaxIndexCpu = 3;
        g.Level = 2;
        g.NbRounds = 12;
        g.NumRound = (byte)r.Next(1, 12);
        for (int e = 0; e < 8; e++) g.RoundEventsProb[e] = 0;
        g.RoundEventsProb[kind] = (byte)r.Next(1, 4);
        // Some food plots producing for the pests, some crystite for the pirates.
        for (int x = 0; x < 45; x++)
            if (g.PlotsPlayer[x] != GameState.Nobody && g.PlotsExp[x] != 0 && r.Next(3) == 0 && !GameState.RiverPlots.Contains(x))
                g.PlotsExp[x] = (byte)(kind == 1 ? Exp.Crystite : Exp.Food);
        Production.InitVarsFromMaps(g);
        MapDisplay.DrawMap(g);
        return g;
    }

    static readonly List<string> Thinks = new();

    sealed record Run(List<(long Frame, string What)> Events, GameState End, int RandomReads);

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
        var rng = new ScriptedRandom(seed);
        m.Random = rng.NextByte;
        var events = new List<(long, string)>();
        long vbi = 0;
        ushort prevPc = 0;
        var seen = new HashSet<string>();
        m.M.AfterStep = c =>
        {
            bool afterRti = m.M.Read(prevPc) == 0x40;
            prevPc = c.PC;
            if (afterRti) return;
            if (c.PC == 0x5BB5) vbi++;
            else if (Checkpoints.TryGetValue(c.PC, out var what) && seen.Add(what)) events.Add((vbi, what));
        };
        m.M.CallRoutine(OriginalGame.Sym("roundEvent"), 20_000);
        events.Add((vbi, "end"));
        return new Run(events, StateMapper.FromRam(m.Ram), rng.Position);
    }

    static Run RunPort(GameState g, int seed, List<(long Frame, string What)> original)
    {
        var c = new Cpu6502();
        StateMapper.ToRam(g, c);
        var state = StateMapper.FromRam(c);
        var rng = new ScriptedRandom(seed);
        var game = new Game(state, rng);
        long VFrame() => game.Frame - game.HeldFrames;
        var events = new List<(long, string)>();
        bool shown = false;
        game.Think = q =>
        {
            var e = original.FirstOrDefault(x => x.What == q.Split(' ')[0]);
            if (e.What == null) return null;
            Thinks.Add($"{q} {e.Frame - VFrame()}");
            return (int)(e.Frame - VFrame());
        };
        game.StartNow(RoundEventScreens.Run(game));
        var input = new PlayerInput[4];
        for (int p = 0; p < 4; p++) input[p] = new PlayerInput(false, 15);
        int seenEvents = 0;
        void Scan()
        {
            for (; seenEvents < game.Events.Count; seenEvents++)
                if (game.Events[seenEvents] is TitleEvent pt && pt.Title.StartsWith(" PRODUCTION") && !shown) { shown = true; events.Add((VFrame(), "production")); }
        }
        Scan();
        while (!game.Finished)
        {
            game.RunFrame(input);
            Scan();
            if (VFrame() > 20_000) throw new TimeoutException("port did not finish");
        }
        events.Add((VFrame(), "end"));
        return new Run(events, state, rng.Position);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void Event_matches(int kind)
    {
        int count = int.Parse(Environment.GetEnvironmentVariable("EVENT_SEEDS") ?? "6");
        var failures = new List<string>();
        for (int seed = 0; seed < count; seed++)
        {
            var g = Make(seed, kind);
            var o = RunOriginal(g, seed);
            var p = RunPort(g, seed, o.Events);
            string F(List<(long Frame, string What)> l) => string.Join(", ", l.Select(e => $"{e.What}@{e.Frame}"));
            var orig = o.Events.Where(e => e.What is "production" or "end").ToList();
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen", "sprMount", "sprRiver");
            _out.WriteLine($"{(RoundEventKind)kind} seed {seed} round {g.NumRound}: original {F(o.Events)} | port {F(p.Events)} | random {o.RandomReads} vs {p.RandomReads}");
            foreach (var d in diff.Take(8)) _out.WriteLine("  " + d);
            if (!orig.SequenceEqual(p.Events) || diff.Count > 0 || o.RandomReads != p.RandomReads) failures.Add($"seed {seed}");
        }
        File.AppendAllLines("/tmp/claude-0/event_thinks.txt", Thinks);
        Thinks.Clear();
        Assert.True(failures.Count == 0, string.Join(", ", failures));
    }
}

public class AnimMapProdTests
{
    readonly ITestOutputHelper _out;
    public AnimMapProdTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void AnimMapProd_matches()
    {
        var bad = new List<string>();
        for (int seed = 0; seed < 300; seed++)
        {
            var g = DifferentialTests.RandomState(seed);
            var m = new MachineGame();
            StateMapper.ToRam(g, m.Ram);
            m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
            m.M.CallRoutine(OriginalGame.Sym("drawMap"));
            m.M.Cpu.A = 1;
            m.M.CallRoutine(OriginalGame.Sym("waitJiffy"));
            var rng = new ScriptedRandom(seed);
            m.Random = rng.NextByte;
            long vbi = 0;
            ushort prev = 0;
            var o = new List<string>();
            int retLine = -1, retPos = 0; long retV = 0;
            m.M.AfterStep = c =>
            {
                bool rti = m.M.Read(prev) == 0x40; prev = c.PC; if (rti) return;
                if (c.PC == 0x5BB5) vbi++;
                if (c.PC == 0x4AF3) { o.Add($"{vbi} wait {c.A} L{m.M.Line} r{rng.Position - retPos}"); if (retLine >= 0) File.AppendAllText("/tmp/claude-0/miss.txt", $"{rng.Position - retPos} {(vbi - retV) * 262 + m.M.Line - retLine} {m.Ram[0xB087 + c.X]} {c.X}\n"); }
                if (c.PC == 0x4AF6 && o.Count > 0 && o[^1].Contains("wait")) { retLine = m.M.Line; retV = vbi; retPos = rng.Position; } if (c.PC == 0x4AF6 && o.Count > 0 && o[^1].Contains("wait")) o.Add($"{vbi} ret L{m.M.Line} rt{m.Ram[0x14]}");
                if (c.PC == 0x4B12) o.Add($"{vbi} scanned");
            };
            m.M.CallRoutine(OriginalGame.Sym("animMapProd"), 20000);
            o.Add($"{vbi} end");

            var c2 = new Cpu6502(); StateMapper.ToRam(g, c2);
            var game = new Game(StateMapper.FromRam(c2), new ScriptedRandom(seed));
            var p = new List<string>();
            game.Trace = e => p.Add($"{game.Frame} {e}");
            game.StartNow(GameLoop.AnimMapProd(game));
            var input = new PlayerInput[4];
            while (!game.Finished) game.RunFrame(input);
            p.Add($"{game.Frame} end");
            if (o[^1] != p[^1]) bad.Add($"seed {seed}: {string.Join(" | ", o.Where(x => !x.Contains("ret")).TakeLast(8))} || {string.Join(" | ", p.TakeLast(8))}");
        }
        foreach (var b in bad) _out.WriteLine(b);
        Assert.Empty(bad);
    }
}
