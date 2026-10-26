using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Runs the original land grant frame by frame on the scanline Atari and the port on its
/// frame clock, with the same button presses, and compares when and what each one did.</summary>
public class LandGrantTests
{
    readonly ITestOutputHelper _out;
    public LandGrantTests(ITestOutputHelper o) => _out = o;

    sealed record Press(int Player, int From, int Length);

    static (GameState g, List<Press> presses) Scenario(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 13 + 5);
        g.NumRound = (byte)r.Next(1, 13);
        for (int i = 0; i < 45; i++)
            if (r.Next(3) != 0) { g.PlotsPlayer[i] = GameState.Nobody; g.PlotsExp[i] = 0; }
        Production.InitVarsFromMaps(g);
        r.Shuffle(g.OrdersPlayer);
        int cpus = r.Next(5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = p < cpus ? (byte)0xFF : (byte)(8 + p);
        g.MaxIndexCpu = (byte)(cpus - 1);
        g.CursorWait = (byte)(r.Next(3) switch { 0 => 8, 1 => 4, _ => 3 });
        var presses = new List<Press>();
        for (int p = cpus; p < 4; p++)
        {
            int n = r.Next(1, 4);
            for (int k = 0; k < n; k++) presses.Add(new Press(p, r.Next(0, 600), r.Next(1, 5)));
        }
        return (g, presses);
    }

    static List<string> Relative(List<string> events, long anchor) =>
        events.Select(e => { var i = e.IndexOf('|'); return $"{long.Parse(e[..i]) - anchor} {e[(i + 1)..]}"; }).ToList();

    static bool Held(List<Press> presses, int player, long rel) =>
        presses.Any(p => p.Player == player && rel >= p.From && rel < p.From + p.Length);

    record Trace(List<string> Events, byte[] Plots, int RandomReads, long IntroFrames, long TotalFrames);

    static Trace RunOriginal(GameState g, List<Press> presses, int seed)
    {
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        var rng = new ScriptedRandom(seed);
        m.Random = rng.NextByte;
        var events = new List<string>();
        long vbi = 0, anchor = -1;
        m.M.AfterStep = c =>
        {
            switch (c.PC)
            {
                case 0x5BB5:
                    vbi++;
                    for (int p = 0; p < 4; p++) m.M.Buttons[p] = anchor >= 0 && Held(presses, p, vbi - anchor);
                    break;
                case 0x4F42:
                    if (anchor < 0) anchor = vbi;
                    events.Add($"{vbi}|cursor {c.A}");
                    break;
                case 0x4F75: events.Add($"{vbi}|grant {c.X} {c.Y}"); break;
                case 0x4FA3: events.Add($"{vbi}|hidden"); break;
            }
        };
        m.M.CallRoutine(OriginalGame.Sym("grantLand"), 20_000);
        events.Add($"{vbi}|end");
        events = Relative(events, anchor);
        return new Trace(events, m.Ram.AsSpan(0xB000, 45).ToArray(), rng.Position, anchor, vbi);
    }

    record PortTrace(Trace T, byte RtAtAnchor);

    static Trace RunPort(GameState g, List<Press> presses, int seed, byte rtAtAnchor)
    {
        var first = RunPortOnce(g, presses, seed, 0);
        return RunPortOnce(g, presses, seed, (byte)(rtAtAnchor - first.RtAtAnchor)).T;
    }

    static PortTrace RunPortOnce(GameState g, List<Press> presses, int seed, byte rt0)
    {
        var state = StateMapper.FromRam(Clone(g));
        var rng = new ScriptedRandom(seed);
        var game = new Game(state, rng);
        game.Vbi.RtClock = rt0;
        byte rtAnchor = 0;
        game.Start(LandGrant.Run(game));
        long anchor = -1;
        var events = new List<string>();
        var input = new PlayerInput[4];
        int seen = 0;
        while (!game.Finished)
        {
            for (int p = 0; p < 4; p++) input[p] = new PlayerInput(anchor >= 0 && Held(presses, p, game.Frame + 1 - anchor));
            game.RunFrame(input);
            for (; seen < game.Events.Count; seen++)
            {
                var e = game.Events[seen];
                if (e is GrantCursorEvent c)
                {
                    if (anchor < 0) { anchor = game.Frame; rtAnchor = game.Vbi.RtClock; }
                    events.Add($"{game.Frame}|cursor {c.Plot}");
                }
                else if (e is PlotGrantedEvent pg) events.Add($"{game.Frame}|grant {pg.Plot} {pg.Player}");
                else if (e is GrantCursorHiddenEvent) events.Add($"{game.Frame}|hidden");
            }
            if (game.Frame > 100_000) throw new TimeoutException();
        }
        events.Add($"{game.Frame}|end");
        events = Relative(events, anchor);
        return new PortTrace(new Trace(events, state.PlotsPlayer.ToArray(), rng.Position, anchor, game.Frame), rtAnchor);
    }

    [Fact]
    public void LandGrant_matches_frame_by_frame()
    {
        int grants = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var (g, presses) = Scenario(seed);
            var o = RunOriginal(g, presses, seed);
            // The port's clock is set at the first cursor frame to the original's clock at that frame.
            byte rt = RtAtAnchor(g, presses, seed);
            var p = RunPort(g, presses, seed, rt);
            int free = Enumerable.Range(0, 45).Count(i => i != 22 && g.PlotsPlayer[i] == GameState.Nobody);
            _out.WriteLine($"seed {seed}: cpus {g.MaxIndexCpu + 1} free {free} round {g.NumRound}: intro original {o.IntroFrames} port {p.IntroFrames}, total {o.TotalFrames - o.IntroFrames} vs {p.TotalFrames - p.IntroFrames}");
            Assert.True(Math.Abs(o.IntroFrames - p.IntroFrames) <= 3, $"seed {seed}: intro original {o.IntroFrames}, port {p.IntroFrames}");
            int n = Math.Min(o.Events.Count, p.Events.Count);
            for (int i = 0; i < n; i++)
                Assert.True(o.Events[i] == p.Events[i], $"seed {seed} event {i}: original '{o.Events[i]}', port '{p.Events[i]}'\n orig: {string.Join(" | ", o.Events.Skip(Math.Max(0, i - 4)).Take(8))}\n port: {string.Join(" | ", p.Events.Skip(Math.Max(0, i - 4)).Take(8))}");
            Assert.Equal(o.Events.Count, p.Events.Count);
            Assert.Equal(o.Plots, p.Plots);
            Assert.Equal(o.RandomReads, p.RandomReads);
            grants += o.Events.Count(e => e.Contains("grant"));
        }
        Assert.True(grants > 40, $"only {grants} grants");
    }

    static byte RtAtAnchor(GameState g, List<Press> presses, int seed)
    {
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        m.Random = new ScriptedRandom(seed).NextByte;
        byte rt = 0;
        bool done = false;
        m.M.AfterStep = c => { if (c.PC == 0x4F42 && !done) { rt = m.Ram[0x14]; done = true; } };
        try { m.M.Halt = _ => done; m.M.CallRoutine(OriginalGame.Sym("grantLand"), 20_000); } catch (TimeoutException) { }
        return rt;
    }

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }
}
