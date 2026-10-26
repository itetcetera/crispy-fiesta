using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Plays the development turns on the scanline Atari with a scripted player that reads the
/// screen positions, records its controls, then replays them to the port and compares frame by frame.</summary>
public class PlayerTurnTests
{
    readonly ITestOutputHelper _out;
    public PlayerTurnTests(ITestOutputHelper o) => _out = o;

    static readonly bool Debug = Environment.GetEnvironmentVariable("TURN_DEBUG") != null;
    static readonly HashSet<ushort> WampusRandomPcs = new() { 0x8BD6, 0x8C11 };

    sealed record Scenario(GameState G, int Seed);

    static Scenario Make(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 31 + 7);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = (byte)(8 + p);
        g.MaxIndexCpu = 0xFF;
        g.Level = (byte)(r.Next(3) == 0 ? 0 : 2);
        for (int p = 0; p < 4; p++) if (r.Next(3) == 0) g.GoodsPlayersNb[p] = (byte)r.Next(0, 3);
        return new Scenario(g, seed);
    }

    /// <summary>The scripted player: goes to random doors and in, now and then waits.</summary>
    sealed class Agent
    {
        readonly Random _r;
        int _target = -1, _wait, _pressAt = -1;
        bool _down, _entering;
        int _lastY = -1;
        public Agent(int seed) => _r = new Random(seed);
        static readonly int[] Top = { 10, 16, 22, 28 }, Bottom = { 10, 15, 20, 25, 27, 29 };

        public (bool button, byte stick) Next(byte[] ram)
        {
            byte x = ram[0x9F], y = ram[0xA3], c1 = ram[0xC1], ad = ram[0xAD];
            if (c1 == 0xFF || ad != 0)
            {
                // Waiting to start: press after a little while.
                if (_pressAt < 0) _pressAt = _r.Next(5, 120);
                if (--_pressAt == 0) { _pressAt = -1; return (true, 15); }
                return (false, 15);
            }
            if (_wait > 0) { _wait--; return (false, 15); }
            if (_target < 0)
            {
                if (_r.Next(6) == 0) { _wait = _r.Next(10, 80); return (false, 15); }
                bool mule = ram[0xE0] != 0, outfit = ram[0xBC] != 0;
                int k = _r.Next(10);
                if (!mule) { _down = k < 8; _target = k < 5 ? Bottom[3 + _r.Next(3)] : _down ? Bottom[_r.Next(3)] : Top[_r.Next(Top.Length)]; }
                else if (!outfit) { _down = k >= 7; _target = _down ? Bottom[_r.Next(Bottom.Length)] : Top[_r.Next(Top.Length)]; }
                else { _down = k < 6; _target = _down ? Bottom[_r.Next(Bottom.Length)] : Top[_r.Next(Top.Length)]; }
            }
            int col = (x - 0x30) >> 2;
            bool street = y >= 0x54 && y < 0x5E;
            byte door = _down ? (byte)0x0D : (byte)0x0E;
            if (_entering && _lastY >= 0 && (_down ? y < _lastY : y > _lastY)) _entering = false; // came back out
            _lastY = y;
            if (!street)
            {
                if (_entering && col == _target) return (false, door);
                _entering = false;
                if (_r.Next(3) == 0) _target = -1;
                return (false, y < 0x54 ? (byte)0x0D : (byte)0x0E);
            }
            if (col < _target) return (false, 0x07);
            if (col > _target) return (false, 0x0B);
            _entering = true;
            return (false, door);
        }
    }

    sealed record Run(List<string> Events, GameState End, List<(bool, byte)[]> Inputs, int RandomReads);

    static Run RunOriginal(Scenario s)
    {
        var m = new MachineGame();
        StateMapper.ToRam(s.G, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        var rng = new ScriptedRandom(s.Seed);
        m.Random = rng.NextByte;
        m.M.RandomOverride = pc => m.M.InVblank && WampusRandomPcs.Contains(pc) ? rng.NextByte() : null;
        var agents = Enumerable.Range(0, 4).Select(p => new Agent(s.Seed * 4 + p)).ToArray();
        var events = new List<string>();
        var inputs = new List<(bool, byte)[]>();
        long vbi = 0;
        string last = "";
        var r = m.Ram;
        m.M.AfterStep = c =>
        {
            switch (c.PC)
            {
                case 0x5BB5:
                    {
                        // The state the last frame left, then this frame's controls.
                        string w = $"w {r[0x9F]:X2} {r[0xA3]:X2} {r[0xD3]} {r[0xE0]} {r[0xBC]} {r[0xAD]}" + (r[0xE0] != 0 ? $" m {r[0xA0]:X2} {r[0xA4]:X2}" : "") + (Debug ? $" a {r[0xA9]:X2} c {r[0xC1]:X2} s {r[0x9E]:X2}" : "");
                        if (w != last && vbi > 0) { events.Add($"{vbi} {w}"); last = w; }
                        vbi++;
                        var inp = new (bool, byte)[4];
                        for (int p = 0; p < 4; p++)
                        {
                            inp[p] = p == r[0xAC] ? agents[p].Next(r) : (false, (byte)15);
                            m.M.Buttons[p] = inp[p].Item1;
                            m.M.Sticks[p] = inp[p].Item2;
                        }
                        inputs.Add(inp);
                        break;
                    }
                case 0x6645: events.Add($"{vbi} player {r[0xB328 + r[0xB378]]}"); break;
                case 0x6741: events.Add($"{vbi} pressed"); break;
                case 0x66CB: events.Add($"{vbi} event"); break;
                case 0x66CE: if (Debug) events.Add($"{vbi} evdone {r[0xBC]}"); break;
                case 0x66F5: if (Debug && events[^1].Contains("evdone")) events.Add($"{vbi} poll"); break;
                case 0x67E0: events.Add($"{vbi} food"); break;
                case 0x6800: events.Add($"{vbi} enter"); break;
                case 0x6B14: events.Add($"{vbi} exit"); break;
                case 0x6A82: events.Add($"{vbi} gamble"); break;
                case 0x6C71: events.Add($"{vbi} timeout"); break;
                case 0x6AF2: events.Add($"{vbi} end"); break;
                case 0x4A32: events.Add($"{vbi} msg {c.X}"); break;
            }
        };
        m.M.CallRoutine(OriginalGame.Sym("playersTurn"), 60_000);
        events.Add($"{vbi} done");
        return new Run(events, StateMapper.FromRam(m.Ram), inputs, rng.Position);
    }

    static Run RunPort(Scenario s, List<(bool, byte)[]> inputs, List<string>? original = null)
    {
        var state = StateMapper.FromRam(Clone(s.G));
        var rng = new ScriptedRandom(s.Seed);
        var game = new Game(state, rng);
        var events = new List<string>();
        Turn? turn = null;
        game.Trace = e =>
        {
            if (e.StartsWith("player") || e == "pressed" || e == "event" || e == "food" || e == "enter" || e == "exit" || e == "gamble" || e == "timeout" || e == "end")
                events.Add($"{game.Frame} {e}");
        };
        if (original != null)
        {
            var at = original.Select(e => (long.Parse(e[..e.IndexOf(' ')]), e[(e.IndexOf(' ') + 1)..])).ToList();
            // The original's events from the start of the turn of the player now starting.
            IEnumerable<(long, string)> FromPlayer() => at.Skip(at.FindLastIndex(e => e.Item1 <= game.Frame && e.Item2.StartsWith("player")));
            game.Race = q =>
            {
                bool r = q switch
                {
                    "message first" => at.Any(e => e.Item1 == game.Frame && e.Item2.StartsWith("msg")),
                    "title quick" => FromPlayer().First(e => e.Item2 == "event").Item1 == game.Frame + 6,
                    "player late" => game.Frame > 0 && FromPlayer().Skip(1).TakeWhile(e => e.Item2 != "event").FirstOrDefault(e => e.Item2.StartsWith("w ")).Item1 > game.Frame,
                    _ => false,
                };
                if (r) Races++;
                return r;
            };
        }
        game.StartNow(PlayerTurn.Run(game));
        string last = "";
        var input = new PlayerInput[4];
        int f = 0;
        while (!game.Finished)
        {
            var inp = f < inputs.Count ? inputs[f] : Enumerable.Repeat((false, (byte)15), 4).ToArray();
            for (int p = 0; p < 4; p++) input[p] = new PlayerInput(inp[p].Item1, inp[p].Item2);
            int seen = game.Events.Count;
            game.RunFrame(input);
            f++;
            for (int i = seen; i < game.Events.Count; i++)
                if (game.Events[i] is TurnMessageEvent tm && tm.Code is > 0 and < 30 && tm.Text == "") events.Add($"{game.Frame} msg {tm.Code}");
            turn = game.Turn;
            if (turn != null)
            {
                string w = $"w {turn.X:X2} {turn.Y:X2} {turn.Time} {turn.HasMule} {turn.Outfit} {turn.Map}" + (turn.HasMule != 0 ? $" m {turn.MuleX:X2} {turn.MuleY:X2}" : "") + (Debug ? $" a {turn.Phase:X2} s {game.Vbi.Sound:X2}" : "");
                if (w != last) { events.Add($"{game.Frame} {w}"); last = w; }
            }
            if (game.Frame > 60_000) throw new TimeoutException();
        }
        events.Add($"{game.Frame} done");
        return new Run(events, state, inputs, rng.Position);
    }

    static int Races;

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }

    [Fact]
    public void Town_matches()
    {
        int count = int.Parse(Environment.GetEnvironmentVariable("TURN_SEEDS") ?? "10");
        int from = int.Parse(Environment.GetEnvironmentVariable("TURN_FROM") ?? "0");
        for (int seed = from; seed < from + count; seed++)
        {
            var s = Make(seed);
            var o = RunOriginal(s);
            var p = RunPort(s, o.Inputs, o.Events);
            if (Debug)
            {
                File.WriteAllLines($"/tmp/claude-0/turn_o_{seed}.txt", o.Events);
                File.WriteAllLines($"/tmp/claude-0/turn_p_{seed}.txt", p.Events);
                File.WriteAllLines($"/tmp/claude-0/turn_in_{seed}.txt", o.Inputs.Select((x, i) => $"{i + 1} " + string.Join(" ", x.Select(y => $"{(y.Item1 ? "B" : "-")}{y.Item2:X}"))));
                continue;
            }
            _out.WriteLine($"seed {seed}: {o.Events.Count} vs {p.Events.Count} events");
            int n = Math.Min(o.Events.Count, p.Events.Count);
            for (int i = 0; i < n; i++)
                if (o.Events[i] != p.Events[i])
                {
                    for (int k = Math.Max(0, i - 12); k < Math.Min(n, i + 6); k++) _out.WriteLine($"  {o.Events[k],-40} | {p.Events[k]}");
                    Assert.Fail($"seed {seed} event {i}: original '{o.Events[i]}', port '{p.Events[i]}'");
                }
            Assert.Equal(o.Events.Count, p.Events.Count);
            var diff = StateMapper.Diff(o.End, p.End);
            Assert.True(diff.Count == 0, $"seed {seed}:\n" + string.Join("\n", diff.Take(20)));
            Assert.Equal(o.RandomReads, p.RandomReads);
        }
    }
}
