using Mule.Engine.Core;
using Mule.Engine.Rules;
using Xunit;

namespace Mule.Engine.Tests.Original;

public class AiDifferentialTests
{
    static GameState State(int seed)
    {
        var g = DifferentialTests.RandomState(seed);
        var r = new Random(seed * 31 + 7);
        if (r.Next(3) == 0) g.NumRound = 1;
        for (int i = 0; i < 3; i++) g.GoodsShortage[i] = (byte)(r.Next(3) == 0 ? 1 : 0);
        r.Shuffle(g.OrdersPlayer);
        int cpus = r.Next(5);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = p < cpus ? (byte)0xFF : (byte)8;
        g.MaxIndexCpu = (byte)(cpus - 1);
        // Some ties so the 1-in-4 tie roll is exercised.
        if (r.Next(2) == 0) for (int i = 0; i < 45; i++) if (r.Next(3) == 0) g.PlotsKnownCrystite[i] = 128;
        return g;
    }

    [Fact]
    public void CalcPlayersPlotsQuality_matches()
    {
        for (int seed = 0; seed < 400; seed++)
        {
            var g = State(seed);
            var orig = new OriginalGame();
            StateMapper.ToRam(g, orig.Cpu);
            var rngO = new ScriptedRandom(seed);
            orig.Random = rngO.NextByte;
            orig.Call("calcPlayersPlotsQuality");
            var after = StateMapper.FromRam(orig.Cpu);

            var port = StateMapper.FromRam(Clone(g));
            var rngP = new ScriptedRandom(seed);
            Ai.CalcPlayersPlotsQuality(port, rngP);
            Assert.True(rngO.Position == rngP.Position, $"seed {seed}: random reads original {rngO.Position}, port {rngP.Position}");
            var diff = StateMapper.Diff(after, port);
            Assert.True(diff.Count == 0, $"seed {seed}: " + string.Join("; ", diff.Take(10)));
        }
    }

    [Fact]
    public void ChooseCpuPlots_matches_grantLand_start()
    {
        int conflicts = 0;
        for (int seed = 0; seed < 400; seed++)
        {
            var g = State(seed);
            if ((g.MaxIndexCpu & 0x80) != 0) continue;
            var orig = new OriginalGame();
            StateMapper.ToRam(g, orig.Cpu);
            var rngO = new ScriptedRandom(seed);
            orig.Random = rngO.NextByte;
            orig.Call("grantLand", stopAt: 0x4EEF);
            var after = StateMapper.FromRam(orig.Cpu);

            var port = StateMapper.FromRam(Clone(g));
            var rngP = new ScriptedRandom(seed);
            var sel = new byte[4];
            LandGrant.ChooseCpuPlots(port, rngP, sel);
            Assert.True(rngO.Position == rngP.Position, $"seed {seed}: random reads original {rngO.Position}, port {rngP.Position}");
            for (int p = 0; p <= g.MaxIndexCpu; p++)
                Assert.True(orig.Cpu.Mem[0x9F + p] == sel[p], $"seed {seed} cpu {p}: original plot {orig.Cpu.Mem[0x9F + p]}, port {sel[p]}");
            var diff = StateMapper.Diff(after, port);
            Assert.True(diff.Count == 0, $"seed {seed}: " + string.Join("; ", diff.Take(10)));
            // Coverage: did two CPUs first want the same plot?
            var probe = StateMapper.FromRam(Clone(g));
            var rngQ = new ScriptedRandom(seed);
            Ai.CalcPlayersPlotsQuality(probe, rngQ);
            var first = Enumerable.Range(0, g.MaxIndexCpu + 1).Select(p => Ai.CalcPlayerBestPlotAi(probe, rngQ, p, 0)).ToList();
            if (first.Distinct().Count() < first.Count) conflicts++;
            Assert.Contains(after.PlayersPlotsQuality, q => q > 0);
        }
        Assert.True(conflicts > 20, $"only {conflicts} conflicts");
    }

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }
}
