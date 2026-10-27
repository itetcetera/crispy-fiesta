using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>The opening ($4758-$4787: the landing, the first score, the take-off) against the original.</summary>
public class OpeningTests
{
    readonly ITestOutputHelper _out;
    public OpeningTests(ITestOutputHelper o) => _out = o;

    static readonly Dictionary<ushort, string> Checkpoints = new()
    {
        [0x475B] = "opening map", [0x4765] = "ship land", [0x4768] = "opening score", [0x476E] = "opening leave",
        [0x4787] = "takeoff", [0x478A] = "opening done",
    };

    static GameState NewGame(int seed, int humans)
    {
        var races = new[] { Race.Mechtron, Race.Humanoid, Race.Flapper, Race.Bonzoid };
        var colors = new byte[] { 196, 132, 52, 84 };
        var seats = Enumerable.Range(0, 4).Select(p => new SeatSetup(p < 4 - humans, p < 4 - humans ? Race.Mechtron : races[p], colors[p])).ToList();
        return Setup.NewGame(new ScriptedRandom(seed + 500), seats);
    }

    [Fact]
    public void Opening_matches()
    {
        for (int seed = 0; seed < 8; seed++)
        {
            var g = NewGame(seed, seed % 4);
            // The original.
            var m = new MachineGame();
            StateMapper.ToRam(g, m.Ram);
            m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
            m.M.CallRoutine(OriginalGame.Sym("initBB00ToBFFF"));
            m.Ram[0xC1] = 0xFF;
            m.M.Cpu.A = 1;
            m.M.CallRoutine(OriginalGame.Sym("waitJiffy"));
            var timers = new[] { m.Ram[0x14], m.Ram[0xC3], m.Ram[0xC4], m.Ram[0xC5], m.Ram[0xC6] };
            var rng = new ScriptedRandom(seed);
            m.Random = rng.NextByte;
            var o = new List<(long Frame, string What)>();
            long vbi = 0;
            ushort prevPc = 0;
            m.M.AfterStep = c =>
            {
                bool afterRti = m.M.Read(prevPc) == 0x40;
                prevPc = c.PC;
                if (afterRti) return;
                if (c.PC == 0x5BB5) { vbi++; m.Ram[0x4D] = 0; return; }
                if (Checkpoints.TryGetValue(c.PC, out var w)) o.Add((vbi, w));
            };
            // The humans hold their buttons down throughout.
            for (int q = 0; q < 4; q++) m.M.Buttons[q] = !g.IsCpu(q);
            byte s = (byte)(m.M.Cpu.S - 2);
            m.Ram[0x478A] = 0xA2; m.Ram[0x478B] = s; m.Ram[0x478C] = 0x9A; m.Ram[0x478D] = 0x60; // LDX #s, TXS, RTS
            m.M.CallRoutine(0x4758, 100_000);
            var oEnd = StateMapper.FromRam(m.Ram);

            // The port.
            var c = new Cpu6502();
            StateMapper.ToRam(g, c);
            var state = StateMapper.FromRam(c);
            var prng = new ScriptedRandom(seed);
            var game = new Game(state, prng);
            game.Vbi.RtClock = timers[0];
            for (int i = 0; i < 4; i++) game.Vbi.Timers[i] = timers[i + 1];
            var p = new List<(long Frame, string What)>();
            var names = new HashSet<string>(Checkpoints.Values);
            game.Trace = e => { if (names.Contains(e)) p.Add((game.Frame, e)); };
            game.StartNow(GameLoop.Opening(game));
            var input = Enumerable.Range(0, 4).Select(q => new PlayerInput(!g.IsCpu(q))).ToArray();
            while (!game.Finished)
            {
                game.RunFrame(input);
                if (game.Frame > 100_000) throw new TimeoutException();
            }

            string F(List<(long Frame, string What)> l) => string.Join(", ", l.Select(e => $"{e.What}@{e.Frame}"));
            _out.WriteLine($"seed {seed} humans {seed % 4}\n  original: {F(o)}\n  port:     {F(p)}\n  random {rng.Position} vs {prng.Position}");
            var diff = StateMapper.Diff(oEnd, state, "mapScreen");
            foreach (var d in diff.Take(10)) _out.WriteLine("  " + d);
            Assert.Equal(o.Select(e => e.What), p.Select(e => e.What));
            for (int i = 1; i < o.Count; i++)
            {
                long od = o[i].Frame - o[i - 1].Frame, pd = p[i].Frame - p[i - 1].Frame;
                Assert.True(Math.Abs(od - pd) <= 3, $"seed {seed}: {o[i].What} after {od} frames in the original, {pd} in the port");
            }
            Assert.Equal(rng.Position, prng.Position);
            Assert.Empty(diff);
        }
    }
}
