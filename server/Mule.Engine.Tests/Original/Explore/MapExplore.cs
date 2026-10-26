using Mule.Engine.Core;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original.Explore;

/// <summary>Scratch: how the original's map frame is built for the walker's collisions.</summary>
public class MapExplore
{
    readonly ITestOutputHelper _out;
    public MapExplore(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Dump_map_frame()
    {
        if (Environment.GetEnvironmentVariable("EXPLORE") == null) return;
        int dumpAt = int.Parse(Environment.GetEnvironmentVariable("DUMP_AT") ?? "400");
        var g = DifferentialTests.RandomState(1);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = (byte)(8 + p);
        g.MaxIndexCpu = 0xFF;
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        var rng = new ScriptedRandom(1);
        m.Random = rng.NextByte;
        long vbi = 0;
        var r = m.Ram;
        var o = new List<string>();
        bool dumping = false;
        var trace = new List<string>();
        m.M.OnLine = line =>
        {
            if (!dumping) return;
            var M = m.M;
            var pf = M.DebugPlayfield;
            string pfs = "";
            int start = -1;
            for (int x = 0; x <= 228; x++)
            {
                bool on = x < 228 && pf[x] != 0;
                if (on && start < 0) start = x;
                if (!on && start >= 0) { pfs += $" {start:X2}-{x - 1:X2}:{pf[start]}"; start = -1; }
            }
            o.Add($"L{line} hp {M.DebugHposP[0]:X2} {M.DebugHposP[1]:X2} {M.DebugHposP[2]:X2} {M.DebugHposP[3]:X2} gp {M.DebugGrafP[0]:X2} {M.DebugGrafP[1]:X2} {M.DebugGrafP[2]:X2} {M.DebugGrafP[3]:X2} sz {M.DebugSizeP[0]}{M.DebugSizeP[1]}{M.DebugSizeP[2]}{M.DebugSizeP[3]} gm {M.DebugGrafM:X2} pf{pfs}");
        };
        m.M.AfterStep = c =>
        {
            if (c.PC != 0x5BB5) return;
            if (dumping) { o.Add($"VBI {vbi} x {r[0x9F]:X2} y {r[0xA3]:X2} a9 {r[0xA9]:X2} aa {r[0xAA]:X2} map {r[0xAB]} P0PL? "); }
            vbi++;
            trace.Add($"{vbi} x {r[0x9F]:X2} y {r[0xA3]:X2} a9 {r[0xA9]:X2} ab {r[0xAB]} c1 {r[0xC1]:X2} d3 {r[0xD3]}");
            dumping = vbi == dumpAt;
            if (dumping)
            {
                int dl = r[0x230] | r[0x231] << 8;
                for (int row = 0; row < 22; row++) o.Add($"SCR {0xB528 + row * 20:X4}: " + string.Join(" ", Enumerable.Range(0, 20).Select(i => r[0xB528 + row * 20 + i].ToString("X2"))));
                o.Add("plotsPlayer " + string.Join(" ", Enumerable.Range(0, 45).Select(i => r[OriginalGame.Sym("plotsPlayer") + i].ToString("X2"))));
                o.Add($"DL {dl:X4}: " + string.Join(" ", Enumerable.Range(0, 80).Select(i => r[dl + i].ToString("X2"))) + $" sdmctl {r[0x22F]:X2} chbas {r[0x2F4]:X2} prior {r[0x26F]:X2}");
            }
            if (dumping) o.Add($"VBI {vbi} x {r[0x9F]:X2} y {r[0xA3]:X2} a9 {r[0xA9]:X2} aa {r[0xAA]:X2} ab {r[0xAB]} b2 {r[0xB2]} mx {string.Join(",", Enumerable.Range(0, 20).Select(i => r[0xB221 + i].ToString("X2")))}");
            int f = (int)vbi;
            for (int p = 0; p < 4; p++) { m.M.Buttons[p] = f % 100 == 50 && f < 150; m.M.Sticks[p] = (byte)(f < 160 ? 15 : f < 260 ? 0xB : f < 330 ? 0xE : 0xB); }
            if (vbi > dumpAt + 1) throw new OperationCanceledException();
        };
        try { m.M.CallRoutine(OriginalGame.Sym("playersTurn"), 4000); } catch (OperationCanceledException) { }
        File.WriteAllLines("/tmp/claude-0/mapdump.txt", o);
        File.WriteAllLines("/tmp/claude-0/maptrace.txt", trace);
    }
}
