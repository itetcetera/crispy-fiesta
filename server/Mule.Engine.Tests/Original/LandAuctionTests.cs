using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original;

/// <summary>Runs the original land auctions (colony plots, and plots players sell, with private offers)
/// frame by frame on the scanline Atari and the port on its frame clock, with the same scripted sticks
/// and buttons, and compares every auction step and the outcome.</summary>
public class LandAuctionTests
{
    readonly ITestOutputHelper _out;
    public LandAuctionTests(ITestOutputHelper o) => _out = o;

    /// <summary>A human's stick held from <see cref="From"/> frames after an auction starts.</summary>
    sealed record Move(int Player, int From, int Length, byte Stick);

    /// <summary>A human's button held from <see cref="From"/> frames after an auction starts.</summary>
    sealed record Press(int Player, int From, int Length);

    /// <summary>Colony auctions (<see cref="Count"/> plots) or, with <see cref="Sales"/>, plots players sell.</summary>
    sealed record Scenario(GameState G, int Count, List<Move> Moves, List<Press> Presses, bool Sales);

    /// <summary>Plots players put up for sale: humans' plots, with the seller's stick moving the price
    /// and the seller's button opening private offers and passing them on.</summary>
    static Scenario MakeSales(int seed)
    {
        var c = Make(seed);
        var g = c.G;
        var r = new Random(seed * 17 + 3);
        var mine = Enumerable.Range(0, 45).Where(i => g.PlotsPlayer[i] < 4 && !g.IsCpu(g.PlotsPlayer[i])).ToList();
        if (mine.Count == 0)
        {
            g.PlotsPlayer[r.Next(2) == 0 ? 0 : 44] = 3;
            g.PlayersAi[3] = 11;
            if (g.MaxIndexCpu == 3) g.MaxIndexCpu = 2;
            mine = Enumerable.Range(0, 45).Where(i => g.PlotsPlayer[i] == 3).ToList();
        }
        var shuffled = mine.ToArray();
        r.Shuffle(shuffled);
        mine = shuffled.ToList();
        int n = Math.Min(mine.Count, r.Next(1, 3));
        g.NbLandsToSellByPlayers = (byte)n;
        for (int i = 0; i < n; i++) g.LandsToSell[i] = (byte)mine[i];
        var presses = new List<Press>();
        foreach (var plot in mine.Take(n).Select(x => g.PlotsPlayer[x]).Distinct())
        {
            int k = r.Next(0, 8);
            int at = r.Next(20, 300);
            for (int j = 0; j < k; j++) { presses.Add(new Press(plot, at, r.Next(1, 30))); at += r.Next(30, 200); }
        }
        return c with { Presses = presses, Sales = true };
    }

    static Scenario Make(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 31 + 7);
        g.NbRounds = 12;
        g.NumRound = (byte)r.Next(1, 12);
        for (int i = 0; i < 45; i++)
            if (r.Next(3) != 0) { g.PlotsPlayer[i] = GameState.Nobody; g.PlotsExp[i] = 0; }
        Production.InitVarsFromMaps(g);
        int cpus = r.Next(5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = p < cpus ? (byte)0xFF : (byte)(8 + p);
        g.MaxIndexCpu = (byte)(cpus - 1);
        for (int p = 0; p < 4; p++) g.PlayersCash[p] = (ushort)r.Next(0, 2500);
        g.LastBidStartLa = r.Next(3) == 0 ? (ushort)0 : (ushort)(r.Next(20, 90) * 4);
        g.LastPriceStart[5] = (ushort)Math.Max(4, g.LastBidStartLa - 48);
        g.LevelAuctionTimeWait = 25; // tournament
        g.AuctionTimeReload = (byte)r.Next(1, 30);
        var moves = new List<Move>();
        for (int p = cpus; p < 4; p++)
        {
            int at = r.Next(0, 40);
            while (at < 900)
            {
                int len = r.Next(4, 120);
                byte stick = r.Next(4) switch { 0 => 0x0D, 1 => 0x0F, _ => 0x0E };
                moves.Add(new Move(p, at, len, stick));
                at += len + r.Next(0, 30);
            }
        }
        return new Scenario(g, r.Next(1, 3), moves, new List<Press>(), false);
    }

    /// <summary>Buttons: all held while the plot cursor waits for them, then as scripted.</summary>
    static bool ButtonAt(Scenario s, int player, long rel, bool waiting) =>
        waiting || (rel >= 0 && s.Presses.Any(p => p.Player == player && rel >= p.From && rel < p.From + p.Length));

    static byte StickAt(List<Move> moves, int player, long rel)
    {
        foreach (var m in moves)
            if (m.Player == player && rel >= m.From && rel < m.From + m.Length) return m.Stick;
        return 0x0F;
    }

    /// <summary>Events with frames counted from the start of their auction; the starts themselves
    /// (frames since the previous start, or since the call) are kept apart because the original's
    /// drawing time before an auction is only modelled to within a few frames.</summary>
    record Trace(List<string> Events, List<long> Starts, List<byte> Clocks, GameState End, int RandomReads);

    static (List<string>, List<long>) Split(List<(long Frame, string Text)> raw)
    {
        var events = new List<string>();
        var starts = new List<long>();
        long last = 0;
        foreach (var (f, t) in raw)
        {
            if (t.StartsWith("start")) { starts.Add(f - last); last = f; }
            events.Add($"{f - last} {t}");
        }
        return (events, starts);
    }

    /// <summary>Hits of the private offer paths in the original: offer opened ($A7AD), passed to the
    /// next buyer ($A8A7), every buyer passed and the auction resumed ($A5BB).</summary>
    static readonly Dictionary<ushort, int> Covered = new() { [0xA7AD] = 0, [0xA8A7] = 0, [0xA5BB] = 0 };

    const ushort Start = 0xA397, State = 0xA588;

    static MachineGame Machine(GameState g, ScriptedRandom rng)
    {
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        m.Random = rng.NextByte;
        return m;
    }

    static Trace RunOriginal(Scenario s, int seed)
    {
        var rng = new ScriptedRandom(seed);
        var m = Machine(s.G, rng);
        m.Ram[0x91] = (byte)s.Count;
        var raw = new List<(long, string)>();
        bool waiting = false;
        // The port's clock starts one frame before the original's at its first vertical blank.
        var clocks = new List<byte> { 0 };
        long vbi = 0, start = -1;
        ushort prev = 0;
        m.M.AfterStep = c =>
        {
            // A vertical blank taken at a traced address returns to it: count the address once.
            bool rti = m.Ram[prev] == 0x40;
            prev = c.PC;
            if (rti) return;
            if (Covered.ContainsKey(c.PC)) Covered[c.PC]++;
            switch (c.PC)
            {
                case 0x5BB5:
                    if (vbi++ == 0) clocks[0] = (byte)(m.Ram[0x14] - 1);
                    for (int p = 0; p < 4; p++)
                    {
                        m.M.Buttons[p] = ButtonAt(s, p, start >= 0 ? vbi - start : -1, waiting);
                        m.M.Sticks[p] = start >= 0 ? StickAt(s.Moves, p, vbi - start) : (byte)0x0F;
                    }
                    break;
                case 0x4C01: waiting = true; break;
                case Start:
                    waiting = false;
                    start = vbi;
                    clocks.Add(m.Ram[0x14]);
                    raw.Add((vbi, $"start {m.Ram[0xCB]} {m.Ram[0xB31D] | (m.Ram[0xB31E] << 8)}"));
                    break;
                case State:
                    raw.Add((vbi, Fmt(m.Ram.AsSpan(0xA3, 4).ToArray(), m.Ram[0xAC], (ushort)(m.Ram[0xB321] | (m.Ram[0xB322] << 8)), m.Ram[0xD3])));
                    break;
            }
        };
        m.M.CallRoutine(OriginalGame.Sym(s.Sales ? "landsAuctionByPlayers" : "landsAuction"), 40_000);
        raw.Add((vbi, "end"));
        var (events, starts) = Split(raw);
        return new Trace(events, starts, clocks, StateMapper.FromRam(m.Ram), rng.Position);
    }

    static string Fmt(byte[] pos, byte leader, ushort high, byte time) =>
        $"pos {string.Join(",", pos.Select(b => b.ToString("X2")))} lead {leader:X2} high {high} time {time:X2}";

    /// <summary>The port, with its jiffy clock set at each auction start to the original's there.</summary>
    static Trace RunPort(Scenario s, int seed, List<byte> clocks)
    {
        var state = StateMapper.FromRam(Clone(s.G));
        var rng = new ScriptedRandom(seed);
        var game = new Game(state, rng);
        game.Vbi.RtClock = clocks[0];
        game.Start(s.Sales ? LandAuction.LandsAuctionByPlayers(game) : LandAuction.LandsAuction(game, s.Count));
        long start = -1;
        bool waiting = false;
        var raw = new List<(long, string)>();
        var input = new PlayerInput[4];
        int seen = 0, n = 1;
        while (!game.Finished)
        {
            for (int p = 0; p < 4; p++)
                input[p] = new PlayerInput(ButtonAt(s, p, start >= 0 ? game.Frame + 1 - start : -1, waiting), start >= 0 ? StickAt(s.Moves, p, game.Frame + 1 - start) : (byte)0x0F);
            game.RunFrame(input);
            for (; seen < game.Events.Count; seen++)
            {
                switch (game.Events[seen])
                {
                    case TitleEvent { Title: "YOUR STICK BUTTON TO START" }: waiting = true; break;
                    case AuctionStartedEvent a:
                        waiting = false;
                        start = game.Frame;
                        if (n < clocks.Count) game.Vbi.RtClock = clocks[n++];
                        raw.Add((game.Frame, $"start {a.Plot} {a.Price}"));
                        break;
                    case AuctionStateEvent st:
                        raw.Add((game.Frame, Fmt(st.Positions, st.Leader, st.High, st.Time)));
                        break;
                }
            }
            if (game.Frame > 200_000) throw new TimeoutException();
        }
        raw.Add((game.Frame, "end"));
        var (events, starts) = Split(raw);
        return new Trace(events, starts, new List<byte>(), state, rng.Position);
    }

    [Fact]
    public void LandsAuction_matches_frame_by_frame() => Compare(Make, 10, false);

    [Fact]
    public void LandsAuctionByPlayers_matches_frame_by_frame() => Compare(MakeSales, 8, true);

    void Compare(Func<int, Scenario> make, int minSold, bool offers)
    {
        foreach (var k in Covered.Keys.ToList()) Covered[k] = 0;
        int sold = 0, late = 0, steps = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var s = make(seed);
            var o = RunOriginal(s, seed);
            var p = RunPort(s, seed, o.Clocks);
            _out.WriteLine($"seed {seed}: cpus {s.G.MaxIndexCpu + 1} count {s.Count}: starts {string.Join(",", o.Starts)} vs {string.Join(",", p.Starts)}, steps {o.Events.Count}, {o.Events[^2]}");
            Assert.Equal(o.Starts.Count, p.Starts.Count);
            for (int i = 0; i < o.Starts.Count; i++)
                // Drawing time is modelled to within a frame or two, and the original's button wait
                // (loc_30A2) misses a timer tick whenever the vertical blank lands between its check
                // and its reset of the timer, costing a timer period (4 frames) each time.
                Assert.True(Math.Abs(o.Starts[i] - p.Starts[i]) <= 11, $"seed {seed}: auction {i} starts after {o.Starts[i]} frames in the original, {p.Starts[i]} in the port");
            int n = Math.Min(o.Events.Count, p.Events.Count);
            for (int i = 0; i < n; i++)
            {
                // The main loop's drawing now and then runs past a vertical blank, so the original
                // reaches a step's end one frame late; the step itself must be the same.
                var (fo, to) = Parse(o.Events[i]);
                var (fp, tp) = Parse(p.Events[i]);
                if (fo != fp) late++;
                Assert.True(to == tp && Math.Abs(fo - fp) <= 1, $"seed {seed} event {i}: original '{o.Events[i]}', port '{p.Events[i]}'\n orig: {string.Join(" | ", o.Events.Skip(Math.Max(0, i - 3)).Take(6))}\n port: {string.Join(" | ", p.Events.Skip(Math.Max(0, i - 3)).Take(6))}");
            }
            Assert.Equal(o.Events.Count, p.Events.Count);
            Assert.Equal(o.End.PlotsPlayer, p.End.PlotsPlayer);
            Assert.Equal(o.End.PlayersCash, p.End.PlayersCash);
            Assert.Equal(o.End.LastBidStartLa, p.End.LastBidStartLa);
            Assert.Equal(o.End.LastPriceStart, p.End.LastPriceStart);
            Assert.Equal(o.RandomReads, p.RandomReads);
            steps += o.Events.Count;
            sold += Enumerable.Range(0, 45).Count(i => s.G.PlotsPlayer[i] != o.End.PlotsPlayer[i]);
        }
        _out.WriteLine($"{steps} steps, {late} a frame apart, {sold} plots sold");
        _out.WriteLine(string.Join(", ", Covered.Select(kv => $"${kv.Key:X4} x{kv.Value}")));
        if (offers) Assert.All(Covered.Values, n => Assert.True(n > 0, "a private offer path was never taken"));
        Assert.True(late * 50 < steps, $"{late} of {steps} steps a frame apart");
        Assert.True(sold > minSold, $"only {sold} plots sold");
    }

    static (long, string) Parse(string e) { int i = e.IndexOf(' '); return (long.Parse(e[..i]), e[(i + 1)..]); }

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }
}
