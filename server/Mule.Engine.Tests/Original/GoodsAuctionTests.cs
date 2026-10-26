using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Runs parts of the original goods auction frame by frame on the scanline Atari and the port
/// on its frame clock, with the same scripted controls, and compares the outcome and the timing.</summary>
public class GoodsAuctionTests
{
    readonly ITestOutputHelper _out;
    public GoodsAuctionTests(ITestOutputHelper o) => _out = o;

    static bool TraceWaits;

    sealed record Press(int Player, int From, int Length);

    sealed record Scenario(GameState G, byte Good, List<Press> Presses);

    static Scenario Make(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 7 + 11);
        g.NbRounds = 12;
        g.NumRound = (byte)r.Next(1, 13);
        g.Level = (byte)(r.Next(4) switch { 0 => 0, 1 => 1, _ => 2 });
        g.FoodReq = (byte)(g.NumRound <= 4 ? 3 : g.NumRound <= 8 ? 4 : 5);
        int cpus = r.Next(5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = p < cpus ? (byte)0xFF : (byte)(8 + p);
        g.MaxIndexCpu = (byte)(cpus - 1);
        for (int i = 0; i < 16; i++) g.GoodsPlayersProd[i] = (byte)(r.Next(3) == 0 ? 0 : r.Next(0, 20));
        for (int p = 0; p < 4; p++) g.PlayersGoodReq[p] = (byte)r.Next(0, 8);
        byte good = (byte)r.Next(4);
        var presses = new List<Press>();
        for (int p = cpus; p < 4; p++)
            if (r.Next(3) != 0) presses.Add(new Press(p, r.Next(0, 1500), r.Next(1, 40)));
        return new Scenario(g, good, presses);
    }

    static bool Held(Scenario s, int p, long f) => s.Presses.Any(x => x.Player == p && f >= x.From && f < x.From + x.Length);

    sealed record Result(List<string> Events, GameState End, byte[] Pos, byte[] Mask, byte[] InZone, byte[] B309, ushort[] Bid, ushort PriceStart, ushort PriceEnd, long Frames);

    static MachineGame Machine(GameState g)
    {
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        return m;
    }

    static Result RunOriginal(Scenario s, out byte clock)
    {
        var m = Machine(s.G);
        m.Ram[0x91] = s.Good;
        var events = new List<string>();
        long vbi = 0;
        byte rt = 0;
        ushort prev = 0;
        m.M.AfterStep = c =>
        {
            bool rti = m.Ram[prev] == 0x40;
            prev = c.PC;
            if (rti) return;
            switch (c.PC)
            {
                case 0x5BB5:
                    vbi++;
                    for (int p = 0; p < 4; p++) m.M.Buttons[p] = Held(s, p, vbi);
                    break;
                case 0x6110: rt = m.Ram[0x14]; break; // the port's clock is set to match here
                case 0x5892: events.Add($"{vbi} bars {m.Ram[0x89]}");  break;
                case 0x637C: events.Add($"{vbi} requirement"); break;
                case 0x638F: events.Add($"{vbi} press"); break;
                case 0x5A1E: if (TraceWaits) events.Add($"{vbi} cut wait"); break;
            }
        };
        m.Random = () => throw new InvalidOperationException("random read in the status screen");
        m.M.CallRoutine(OriginalGame.Sym("animUsageSpoilageProd"), 20_000);
        clock = rt;
        var r = m.Ram;
        ushort W(int a) => (ushort)(r[a] | (r[a + 1] << 8));
        return new Result(events, StateMapper.FromRam(r), r.AsSpan(0xA3, 4).ToArray(), r.AsSpan(0xE3, 4).ToArray(),
            r.AsSpan(0x9F, 4).ToArray(), r.AsSpan(0xB309, 8).ToArray(), Enumerable.Range(0, 4).Select(p => W(0xB301 + 2 * p)).ToArray(),
            W(0xB31D), W(0xB31F), vbi);
    }

    /// <summary>The port, with its jiffy clock matching the original's when the status bars start.</summary>
    static Result RunPort(Scenario s, byte clock)
    {
        byte at = 0;
        RunPortOnce(s, 0, t => at = t);
        return RunPortOnce(s, (byte)(clock - at), null);
    }

    static Result RunPortOnce(Scenario s, byte clock, Action<byte>? anchor)
    {
        var state = StateMapper.FromRam(Clone(s.G));
        var game = new Game(state, new ScriptedRandom(0));
        game.Vbi.RtClock = clock;
        var a = new Auction { Good = s.Good };
        // Auction variables the screen leaves alone keep what the original's memory held.
        var ram = Machine(s.G).Ram;
        ram.AsSpan(0xA3, 4).CopyTo(a.Pos);
        ram.AsSpan(0xE3, 4).CopyTo(a.Mask);
        ram.AsSpan(0x9F, 4).CopyTo(a.InZone);
        ram.AsSpan(0xB309, 8).CopyTo(a.B309);
        for (int p = 0; p < 4; p++) a.Bid[p] = (ushort)(ram[0xB301 + 2 * p] | (ram[0xB302 + 2 * p] << 8));
        game.Start(GoodsAuction.Status(game, a));
        var events = new List<string>();
        var input = new PlayerInput[4];
        int seen = 0;
        game.Trace = t =>
        {
            if (t == "c1=0") anchor?.Invoke(game.Vbi.RtClock);
            else if (TraceWaits && t == "cut wait") events.Add($"{game.Frame} {t}");
        };
        while (!game.Finished)
        {
            for (int p = 0; p < 4; p++) input[p] = new PlayerInput(Held(s, p, game.Frame + 1));
            game.RunFrame(input);
            for (; seen < game.Events.Count; seen++)
                switch (game.Events[seen])
                {
                    case GoodsBarsEvent b: events.Add($"{game.Frame} bars {b.Kind}"); break;
                    case GoodsRequirementEvent: events.Add($"{game.Frame} requirement"); break;
                    case MessageEvent { Text: "PRESS BUTTON TO CONTINUE" }: events.Add($"{game.Frame} press"); break;
                }
            if (game.Frame > 50_000) throw new TimeoutException();
        }
        return new Result(events, state, a.Pos.ToArray(), a.Mask.ToArray(), a.InZone.ToArray(), a.B309.ToArray(), a.Bid.ToArray(), a.PriceStart, a.PriceEnd, game.Frame);
    }

    [Fact]
    public void Status_matches()
    {
        for (int seed = 0; seed < 60; seed++)
        {
            var s = Make(seed);
            var o = RunOriginal(s, out byte clock);
            var p = RunPort(s, clock);
            string ctx = $"seed {seed} good {s.Good} level {s.G.Level} round {s.G.NumRound}";
            _out.WriteLine($"{ctx}: frames {o.Frames} vs {p.Frames}; {string.Join(", ", o.Events)} | {string.Join(", ", p.Events)}");
            var diff = StateMapper.Diff(o.End, p.End);
            Assert.True(diff.Count == 0, $"{ctx}: " + string.Join("; ", diff.Take(10)));
            Assert.Equal(o.Pos, p.Pos);
            Assert.Equal(o.Mask, p.Mask);
            Assert.Equal(o.InZone, p.InZone);
            Assert.Equal(o.B309, p.B309);
            Assert.Equal(o.Bid, p.Bid);
            Assert.Equal(o.PriceStart, p.PriceStart);
            Assert.Equal(o.PriceEnd, p.PriceEnd);
            Assert.Equal(o.Events.Count, p.Events.Count);
            for (int i = 0; i < o.Events.Count; i++)
            {
                var (fo, to) = Split(o.Events[i]);
                var (fp, tp) = Split(p.Events[i]);
                Assert.True(to == tp && Math.Abs(fo - fp) <= 3, $"{ctx} event {i}: original '{o.Events[i]}', port '{p.Events[i]}'");
            }
            Assert.True(Math.Abs(o.Frames - p.Frames) <= 3, $"{ctx}: {o.Frames} frames in the original, {p.Frames} in the port");
        }
    }

    [Fact]
    public void Debug_status()
    {
        TraceWaits = true;
        var s = Make(0);
        var o = RunOriginal(s, out byte clock);
        var p = RunPort(s, clock);
        TraceWaits = false;
        _out.WriteLine(string.Join("\n", o.Events));
        _out.WriteLine("port");
        _out.WriteLine(string.Join("\n", p.Events));
    }

    static (long, string) Split(string e) { int i = e.IndexOf(' '); return (long.Parse(e[..i]), e[(i + 1)..]); }

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }
}
