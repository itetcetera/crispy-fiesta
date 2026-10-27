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
    static bool TraceRandom = Environment.GetEnvironmentVariable("TRADE_RNG") != null;

    sealed class Logged : IRandomSource
    {
        readonly IRandomSource _r; readonly Action _log;
        public Logged(IRandomSource r, Action log) { _r = r; _log = log; }
        public byte NextByte() { _log(); return _r.NextByte(); }
    }
    static byte TradeClock;
    static List<string> OriginalEvents = new();
    static int RacesAnswered;

    sealed record Press(int Player, int From, int Length);

    sealed record Move(int Player, int From, int Length, byte Stick);

    sealed record Scenario(GameState G, byte Good, List<Press> Presses, List<Move> Moves, bool Select = false, bool Full = false)
    {
        public List<Move> TradeMoves { get; init; } = new();
        public List<Press> TradePresses { get; init; } = new();
    }

    static Scenario Make(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 7 + 11);
        g.NbRounds = 12;
        g.NumRound = (byte)r.Next(1, 13);
        g.Level = (byte)(r.Next(4) switch { 0 => 0, 1 => 1, _ => 2 });
        g.FoodReq = (byte)(g.NumRound <= 4 ? 3 : g.NumRound <= 8 ? 4 : 5);
        int cpus = r.Next(5);
        // Every fourth game, at least two people press together in the tournament, to collude.
        bool collude = seed % 4 == 3;
        if (collude) { cpus %= 3; g.Level = 2; }
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = p < cpus ? (byte)0xFF : (byte)(8 + p);
        g.MaxIndexCpu = (byte)(cpus - 1);
        for (int i = 0; i < 16; i++) g.GoodsPlayersProd[i] = (byte)(r.Next(3) == 0 ? 0 : r.Next(0, 20));
        for (int p = 0; p < 4; p++) g.PlayersGoodReq[p] = (byte)r.Next(0, 8);
        byte good = (byte)r.Next(4);
        var presses = new List<Press>();
        for (int p = cpus; p < 4; p++)
            if (r.Next(3) != 0) presses.Add(new Press(p, r.Next(0, 1500), r.Next(1, 40)));
        var moves = new List<Move>();
        for (int p = cpus; p < 4; p++)
            for (int k = r.Next(4); k > 0; k--) moves.Add(new Move(p, r.Next(0, 250), r.Next(1, 30), r.Next(2) == 0 ? (byte)0x0E : (byte)0x0D));
        var tradeMoves = new List<Move>();
        var tradePresses = new List<Press>();
        for (int p = cpus; p < 4; p++)
        {
            for (int f = r.Next(0, 60); f < 4000;)
            {
                int len = r.Next(4, 160);
                int k = r.Next(10);
                byte stick = k < 4 ? (byte)0x0E : k < 8 ? (byte)0x0D : (byte)0x0F;
                tradeMoves.Add(new Move(p, f, len, stick));
                f += len + (r.Next(3) == 0 ? r.Next(1, 60) : 0);
            }
            for (int k = r.Next(3); k > 0; k--) tradePresses.Add(new Press(p, r.Next(0, 1500), r.Next(1, 20)));
        }
        if (collude)
            for (int k = 0; k < 4; k++)
            {
                int t = r.Next(0, 2500);
                for (int p = cpus; p < 4; p++) tradePresses.Add(new Press(p, t + r.Next(0, 4), r.Next(2, 15)));
            }
        return new Scenario(g, good, presses, moves) { TradeMoves = tradeMoves, TradePresses = tradePresses };
    }

    static bool Held(Scenario s, int p, long f) => s.Presses.Any(x => x.Player == p && f >= x.From && f < x.From + x.Length);

    static bool HeldIn(List<Press> presses, int p, long f) => presses.Any(x => x.Player == p && f >= x.From && f < x.From + x.Length);

    /// <summary>Controls for a frame: the trading script once trading has started, else the earlier ones.</summary>
    static (bool, byte) Controls(Scenario s, int p, long f, long select, long trade)
    {
        if (trade >= 0)
        {
            long rel = f - trade;
            var m = s.TradeMoves.LastOrDefault(x => x.Player == p && rel >= x.From && rel < x.From + x.Length);
            return (HeldIn(s.TradePresses, p, rel), m?.Stick ?? 0x0F);
        }
        return (Held(s, p, f), StickAt(s, p, select >= 0 ? f - select : -1));
    }

    /// <summary>The stick, counted in frames from the start of the buy/sell choice.</summary>
    static byte StickAt(Scenario s, int p, long rel) =>
        rel < 0 ? (byte)0x0F : s.Moves.LastOrDefault(m => m.Player == p && rel >= m.From && rel < m.From + m.Length)?.Stick ?? 0x0F;

    sealed record Result(List<string> Events, GameState End, byte[] Pos, byte[] Mask, byte[] InZone, byte[] B309, ushort[] Bid, ushort PriceStart, ushort PriceEnd, long Frames,
        byte[] Seller, byte[] Role, byte[] CpuUnits, int RandomReads);

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
        var rng = new ScriptedRandom(1);
        var events = new List<string>();
        var r0 = m.Ram;
        long vbi = 0, start = -1, trade = -1;
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
                    for (int p = 0; p < 4; p++)
                        (m.M.Buttons[p], m.M.Sticks[p]) = Controls(s, p, vbi, start, trade);
                    break;
                case 0x9FFA: if (trade < 0) { trade = vbi; TradeClock = m.Ram[0x14]; events.Add($"{vbi} trade"); } break;
                case 0xA15C:
                    events.Add($"{vbi} step {r0[0xAC]:X2} {r0[0xAD]:X2} {r0[0xB321] | (r0[0xB322] << 8)} {r0[0xB323] | (r0[0xB324] << 8)} {r0[0xD3]:X2} {string.Join(",", r0.AsSpan(0xA3, 4).ToArray().Select(b => b.ToString("X2")))}");
                    break;
                case 0x9492: if (TraceRandom) events.Add($"{vbi} rnd time"); break;
                case 0x9071: if (TraceRandom) events.Add($"{vbi} rnd ai9"); break;
                case 0x9B47: if (TraceRandom || s.Full) events.Add($"{vbi} rnd vbl {r0[0x93]:X2} {r0[0xA9]:X2} {r0[0xAE]:X2} {r0[0xDC]:X2} {r0[0xDD]:X2} {r0[0xC5]:X2} {r0[0x14]:X2}"); break;
                case 0x9595: if (TraceRandom) events.Add($"{vbi} rnd move"); break;
                case 0xA00A: if (TraceRandom) events.Add($"{vbi} rnd waittick"); break;
                case 0x9147: if (TraceRandom || s.Full) events.Add($"{vbi} rnd quitb"); break;
                case 0x9150: if (TraceRandom || s.Full) events.Add($"{vbi} rnd quits"); break;
                case 0x9159: if (TraceRandom || s.Full) events.Add($"{vbi} rnd notices"); break;
                case 0x91DF: if (TraceRandom) events.Add($"{vbi} rnd tradeend"); break;
                case 0x8EAA: events.Add($"{vbi} unit {r0[0xAC]:X2} {r0[0xAD]:X2} {r0[0xC2]:X2}"); break;
                case 0x91F5: events.Add($"{vbi} collusion"); break;
                case 0x952A: events.Add($"{vbi} over"); break;
                case 0x9D6D: events.Add($"{vbi} no sellers"); break;
                case 0x915E: if (r0[0xAC] == 0xFF) events.Add($"{vbi} b546 {r0[0xB546]}"); break;
                case 0x6417: events.Add($"{vbi} no commodity"); break;
                case 0x657B: if (start < 0) { start = vbi; events.Add($"{vbi} select"); } break;
                case 0x9B05: events.Add($"{vbi} switch {c.X} {m.Ram[0xB1B5 + c.X]}"); break;
                case 0x6110: rt = m.Ram[0x14]; break; // the port's clock is set to match here
                case 0x5892: events.Add($"{vbi} bars {m.Ram[0x89]}");  break;
                case 0x637C: events.Add($"{vbi} requirement"); break;
                case 0x638F: events.Add($"{vbi} press"); break;
                case 0x5A1E: if (TraceWaits) events.Add($"{vbi} cut wait"); break;
            }
        };
        if (s.Full)
        {
            m.Random = () => { if (TraceRandom) events.Add($"{vbi} rnd {m.M.Cpu.PC:X4}"); return rng.NextByte(); };
            m.M.CallRoutine(OriginalGame.Sym("goodAuction"), 60_000);
        }
        else if (s.Select)
        {
            m.Random = rng.NextByte;
            m.Ram[0x97F1] = 0x60; // return before the trading
            m.M.CallRoutine(OriginalGame.Sym("goodAuction"), 20_000);
        }
        else
        {
            m.Random = () => throw new InvalidOperationException("random read in the status screen");
            m.M.CallRoutine(OriginalGame.Sym("animUsageSpoilageProd"), 20_000);
        }
        clock = rt;
        OriginalEvents = events;
        var r = m.Ram;
        ushort W(int a) => (ushort)(r[a] | (r[a + 1] << 8));
        return new Result(events, StateMapper.FromRam(r), r.AsSpan(0xA3, 4).ToArray(), r.AsSpan(0xE3, 4).ToArray(),
            r.AsSpan(0x9F, 4).ToArray(), r.AsSpan(0xB309, 8).ToArray(), Enumerable.Range(0, 4).Select(p => W(0xB301 + 2 * p)).ToArray(),
            W(0xB31D), W(0xB31F), vbi, r.AsSpan(0xB1B5, 4).ToArray(), r.AsSpan(0xB1A9, 4).ToArray(), r.AsSpan(0xB487, 4).ToArray(), rng.Position);
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
        var rng = new ScriptedRandom(1);
        Game game = null!;
        var events = new List<string>();
        game = new Game(state, TraceRandom ? new Logged(rng, () => events.Add($"{game.Frame} rnd")) : rng);
        game.Vbi.RtClock = clock;
        var a = new Auction { Good = s.Good };
        // Auction variables the screen leaves alone keep what the original's memory held.
        var ram = Machine(s.G).Ram;
        ram.AsSpan(0xA3, 4).CopyTo(a.Pos);
        ram.AsSpan(0xE3, 4).CopyTo(a.Mask);
        ram.AsSpan(0x9F, 4).CopyTo(a.InZone);
        ram.AsSpan(0xB309, 8).CopyTo(a.B309);
        for (int p = 0; p < 4; p++) a.Bid[p] = (ushort)(ram[0xB301 + 2 * p] | (ram[0xB302 + 2 * p] << 8));
        ram.AsSpan(0xB1B5, 4).CopyTo(a.Seller);
        ram.AsSpan(0xB1A9, 4).CopyTo(a.Role);
        ram.AsSpan(0xB487, 4).CopyTo(a.CpuUnits);
        game.Start(s.Full ? GoodsAuction.Run(game, a) : s.Select ? GoodsAuction.Prepare(game, a) : GoodsAuction.Status(game, a));
        var input = new PlayerInput[4];
        int seen = 0;
        long start = -1, trade = -1;
        if (s.Full && anchor == null) game.Race = Oracle(game, () => trade);
        game.Trace = t =>
        {
            if (t == "c1=0") anchor?.Invoke(game.Vbi.RtClock);
            else if (t == "select") { start = game.Frame; events.Add($"{game.Frame} select"); }
            else if (t == "trade")
            {
                // The trading is compared from its start, with the clock set to the original's there.
                trade = game.Frame;
                if (anchor == null) game.Vbi.RtClock = TradeClock;
                events.Add($"{game.Frame} trade");
            }
            else if (TraceRandom && t.StartsWith("rnd")) events.Add($"{game.Frame} {t}");
            else if (t == "race") events.Add($"{game.Frame} race");
            else if (t.StartsWith("step") || t.StartsWith("unit") || t is "collusion" or "over" or "no sellers") events.Add($"{game.Frame} {t}");
            else if (t.StartsWith("switch")) events.Add($"{game.Frame} {t}");
            else if (TraceWaits && t == "cut wait") events.Add($"{game.Frame} {t}");
        };
        while (!game.Finished)
        {
            for (int p = 0; p < 4; p++)
            {
                var (b, st) = Controls(s, p, game.Frame + 1, start, trade);
                input[p] = new PlayerInput(b, st);
            }
            game.RunFrame(input);
            for (; seen < game.Events.Count; seen++)
                switch (game.Events[seen])
                {
                    case GoodsBarsEvent b: events.Add($"{game.Frame} bars {b.Kind}"); break;
                    case GoodsRequirementEvent: events.Add($"{game.Frame} requirement"); break;
                    case MessageEvent { Text: "PRESS BUTTON TO CONTINUE" }: events.Add($"{game.Frame} press"); break;
                    case MessageEvent { Text: "NO COMMODITY" }: events.Add($"{game.Frame} no commodity"); break;
                }
            if (game.Frame > 100_000) throw new TimeoutException();
        }
        return new Result(events, state, a.Pos.ToArray(), a.Mask.ToArray(), a.InZone.ToArray(), a.B309.ToArray(), a.Bid.ToArray(), a.PriceStart, a.PriceEnd, game.Frame,
            a.Seller.ToArray(), a.Role.ToArray(), a.CpuUnits.ToArray(), rng.Position);
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
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen"); // the auction screen reuses the map's memory; drawMap redraws it
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
    public void Select_matches()
    {
        int switches = 0, called = 0, none = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var s = Make(seed) with { Select = true };
            if (seed % 6 == 0)
            {
                // Nothing held, produced or in store: no auction (smithore, crystite) or "NO COMMODITY".
                for (int x = 0; x < 4; x++) s.G.GoodsPlayersNb[s.Good * 4 + x] = s.G.GoodsPlayersProd[s.Good * 4 + x] = 0;
                s.G.GoodsStoreNb[s.Good] = 0;
            }
            var o = RunOriginal(s, out byte clock);
            var p = RunPort(s, clock);
            string ctx = $"seed {seed} good {s.Good} level {s.G.Level} round {s.G.NumRound} cpus {s.G.MaxIndexCpu + 1}";
            _out.WriteLine($"{ctx}: frames {o.Frames} vs {p.Frames}; {string.Join(", ", o.Events)} | {string.Join(", ", p.Events)}");
            if (!o.Events.Any(e => e.EndsWith("select"))) called++;
            if (o.Events.Any(e => e.EndsWith("no commodity"))) none++;
            switches += o.Events.Count(e => e.Contains("switch"));
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen"); // the auction screen reuses the map's memory; drawMap redraws it
            Assert.True(diff.Count == 0, $"{ctx}: " + string.Join("; ", diff.Take(10)));
            Assert.Equal(o.RandomReads, p.RandomReads);
            Assert.Equal(o.Seller, p.Seller);
            Assert.Equal(o.Role, p.Role);
            Assert.Equal(o.CpuUnits, p.CpuUnits);
            Assert.Equal(o.Pos, p.Pos);
            Assert.Equal(o.Mask, p.Mask);
            Assert.Equal(o.PriceStart, p.PriceStart);
            var oe = o.Events.Where(e => !e.Contains("cut wait")).ToList();
            var pe = p.Events.Where(e => !e.Contains("cut wait")).ToList();
            Assert.True(oe.Count == pe.Count, $"{ctx}: {string.Join(", ", oe)} | {string.Join(", ", pe)}");
            for (int i = 0; i < oe.Count; i++)
            {
                var (fo, to) = Split(oe[i]);
                var (fp, tp) = Split(pe[i]);
                Assert.True(to == tp && Math.Abs(fo - fp) <= 3, $"{ctx} event {i}: original '{oe[i]}', port '{pe[i]}'");
            }
            Assert.True(Math.Abs(o.Frames - p.Frames) <= 3, $"{ctx}: {o.Frames} frames in the original, {p.Frames} in the port");
        }
        _out.WriteLine($"{switches} switches, {called} called off, {none} with no commodity");
        Assert.True(switches > 0 && none > 0 && called > none);
    }

    /// <summary>Answers the port's race questions with what the original did at the same point.</summary>
    static Func<string, bool> Oracle(Game game, Func<long> trade) => Oracle(game, trade, OriginalEvents);

    internal static Func<string, bool> Oracle(Game game, Func<long> trade, List<string> original)
    {
        var ev = original.Select(e => Split(e)).ToList();
        var select = ev.Where(e => e.Item2 == "select").Select(e => e.Item1).DefaultIfEmpty(0).First();
        bool SelectLate(string q) => q == "select late" && game.Frame < select;
        // With nothing to sell there is no trading, and no other race.
        if (!ev.Any(e => e.Item2 == "trade")) return SelectLate;
        long t0 = ev.First(e => e.Item2 == "trade").Item1;
        var units = ev.Where(e => e.Item2.StartsWith("unit")).Select(e => e.Item1 - t0).ToHashSet();
        var ticks = ev.Where(e => e.Item2.StartsWith("rnd vbl")).GroupBy(e => e.Item1 - t0).ToDictionary(g => g.Key, g => g.First().Item2.Split(' ')[2]);
        var quits = ev.Where(e => e.Item2.StartsWith("rnd quit")).Select(e => e.Item1 - t0).ToHashSet();
        var lateQuits = ev.Where(e => e.Item2.StartsWith("rnd quit")).Select(e => e.Item1 - t0)
            .Where(f => ticks.TryGetValue(f + 1, out var v) && v != "FF").ToHashSet();
        // The trade delay (C5) is set one vertical blank later than usual.
        var delay = ev.Where(e => e.Item2.StartsWith("rnd vbl")).GroupBy(e => e.Item1 - t0).ToDictionary(g => g.Key, g => g.First().Item2.Split(' ')[7]);
        var slowUnits = units.Where(f => delay.TryGetValue(f + 2, out var d2) && d2 == "00"
            && delay.TryGetValue(f + 3, out var d3) && d3 != "00").Select(f => f + 1).ToHashSet();
        // Or the notices come one vertical blank later.
        var notices = ev.Where(e => e.Item2 == "rnd notices").Select(e => e.Item1 - t0).ToHashSet();
        slowUnits.UnionWith(units.Where(f => notices.Contains(f + 2) && !notices.Contains(f + 1)).Select(f => f + 1));
        return q =>
        {
            long rel = game.Frame - trade();
            bool r = q switch
            {
                "select late" => SelectLate(q),
                "tick first" => !units.Contains(rel),
                "slow tick" => lateQuits.Contains(rel),
                "slow unit" => slowUnits.Contains(rel),
                "quit late" => !quits.Contains(rel),
                "unit first" => units.Contains(rel),
                _ => false,
            };
            if (r) RacesAnswered++;
            return r;
        };
    }

    [Fact]
    public void Trading_matches()
    {
        int units = 0, steps = 0, collusions = 0, noSellers = 0;
        RacesAnswered = 0;
        var seen = new HashSet<string>();
        int from = int.Parse(Environment.GetEnvironmentVariable("TRADE_FROM") ?? "0");
        int count = int.Parse(Environment.GetEnvironmentVariable("TRADE_SEEDS") ?? "40");
        for (int seed = from; seed < from + count; seed++)
        {
            var s = Make(seed) with { Full = true };
            if (seed % 5 == 4)
            {
                // Nothing to sell, in the store or with the players.
                for (int x = 0; x < 4; x++) s.G.GoodsPlayersNb[s.Good * 4 + x] = 0;
                s.G.GoodsStoreNb[s.Good] = 0;
            }
            var o = RunOriginal(s, out byte clock);
            var p = RunPort(s, clock);
            string ctx = $"seed {seed} good {s.Good} level {s.G.Level} round {s.G.NumRound} cpus {s.G.MaxIndexCpu + 1}";
            if (TraceRandom) _out.WriteLine("MOVES " + string.Join(" ", s.Moves.Select(m => $"{m.Player}:{m.From}+{m.Length}={m.Stick:X}")));
            if (TraceRandom)
            {
                var a1 = o.Events.Where(e => e.Contains("rnd") || e.Contains("trade")).ToList();
                var a2 = p.Events.Where(e => e.Contains("rnd") || e.Contains("trade")).ToList();
                for (int i = 0; i < Math.Max(a1.Count, a2.Count); i++)
                    _out.WriteLine($"  {(i < a1.Count ? a1[i] : "")}  |  {(i < a2.Count ? a2[i] : "")}");
            }
            if (TraceRandom)
                for (int i = 0; i < Math.Max(o.Events.Count, p.Events.Count); i++)
                    _out.WriteLine($"  ALL {(i < o.Events.Count ? o.Events[i] : "")}  |  {(i < p.Events.Count ? p.Events[i] : "")}");
            var oe = o.Events.Where(e => !e.Contains("cut wait") && !e.Contains("rnd")).ToList();
            foreach (var e in oe.Where(e => e.Contains("b546"))) seen.Add(e[(e.IndexOf(' ') + 1)..]);
            oe = oe.Where(e => !e.Contains("b546")).ToList();
            var pe = p.Events.Where(e => !e.Contains("cut wait") && !e.Contains("rnd")).ToList();
            _out.WriteLine($"{ctx}: frames {o.Frames} vs {p.Frames}, {oe.Count} vs {pe.Count} events");
            units += oe.Count(e => e.Contains("unit"));
            steps += oe.Count(e => e.Contains("step"));
            collusions += oe.Count(e => e.Contains("collusion"));
            noSellers += oe.Count(e => e.Contains("no sellers"));
            long to0 = Split(oe.FirstOrDefault(e => e.EndsWith(" trade")) ?? "0 x").Item1;
            long tp0 = Split(pe.FirstOrDefault(e => e.EndsWith(" trade")) ?? "0 x").Item1;
            int n = Math.Min(oe.Count, pe.Count);
            for (int i = 0; i < n; i++)
            {
                var (fo, to) = Split(oe[i]);
                var (fp, tp) = Split(pe[i]);
                bool after = to0 != 0 && fo >= to0;
                if (to != tp || Math.Abs(after ? fo - to0 - (fp - tp0) : fo - fp) > 3)
                {
                    for (int k = Math.Max(0, i - 6); k < Math.Min(n, i + 3); k++) _out.WriteLine($"  {oe[k]}  |  {pe[k]}");
                    Assert.Fail($"{ctx} event {i}: original '{oe[i]}', port '{pe[i]}'");
                }
            }
            Assert.True(oe.Count == pe.Count, $"{ctx}: {oe.Count} events in the original, {pe.Count} in the port; last {oe.LastOrDefault()} | {pe.LastOrDefault()}");
            var diff = StateMapper.Diff(o.End, p.End, "mapScreen"); // the auction screen reuses the map's memory; drawMap redraws it
            Assert.True(diff.Count == 0, $"{ctx}: " + string.Join("; ", diff.Take(10)));
            Assert.Equal(o.RandomReads, p.RandomReads);
            Assert.Equal(o.Pos, p.Pos);
            Assert.True(Math.Abs(o.Frames - to0 - (p.Frames - tp0)) <= 3, $"{ctx}: {o.Frames} frames in the original, {p.Frames} in the port");
        }
        _out.WriteLine($"{units} units, {steps} steps, {collusions} collusions, {noSellers} without sellers, {RacesAnswered} races the original took the rare way; b546: {string.Join(", ", seen)}");
        Assert.True(units > 0);
        if (count >= 40) Assert.True(collusions > 0 && noSellers > 0);
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
