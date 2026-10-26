using Mule.Engine.Core;
using Mule.Engine.Rules;
using Xunit;

namespace Mule.Engine.Tests.Original;

/// <summary>
/// Runs an original routine from the snapshot and its C# port on the same state and the same
/// random bytes, and requires identical RAM state and random read counts.
/// </summary>
public class DifferentialTests
{
    /// <summary>Builds a varied, plausible mid-game state.</summary>
    internal static GameState RandomState(int seed)
    {
        var r = new Random(seed);
        var g = new GameState();
        Setup.GenerateMap(g, new SystemRandomSource(seed));
        Setup.InitValues(g);
        g.NumRound = (byte)r.Next(1, 13);
        g.FoodReq = (byte)(g.NumRound <= 4 ? 3 : g.NumRound <= 8 ? 4 : 5);
        int owned = r.Next(0, 45);
        for (int i = 0; i < owned; i++)
        {
            int plot = r.Next(45);
            if (plot == GameState.StorePlot) continue;
            g.PlotsPlayer[plot] = (byte)r.Next(4);
            byte exp = (byte)r.Next(5);
            if (exp >= Exp.Smithore && GameState.RiverPlots.Contains(plot)) exp = Exp.Food;
            g.PlotsExp[plot] = exp;
            g.PlotsProd[plot] = (byte)r.Next(9);
            g.PlotsKnownCrystite[plot] = r.Next(2) == 0 ? (byte)128 : (byte)r.Next(4);
        }
        for (int i = 0; i < 16; i++) g.GoodsPlayersNb[i] = (byte)(r.Next(4) == 0 ? 0 : r.Next(0, r.Next(2) == 0 ? 12 : 60));
        for (int i = 0; i < 4; i++) g.GoodsStoreNb[i] = (byte)(r.Next(3) == 0 ? 0 : r.Next(0, 40));
        g.GoodsStoreNb[Good.Mule] = (byte)r.Next(0, 15);
        g.GoodsPrice[Good.Food] = (ushort)r.Next(30, 251);
        g.GoodsPrice[Good.Energy] = (ushort)r.Next(25, 251);
        g.GoodsPrice[Good.Smithore] = (ushort)r.Next(50, 251);
        g.GoodsPrice[Good.Crystite] = (ushort)r.Next(50, 150);
        for (int p = 0; p < 4; p++) g.PlayersCash[p] = (ushort)r.Next(0, 5000);
        Production.InitVarsFromMaps(g);
        return g;
    }

    static (GameState original, GameState port) Run(GameState start, int seed, string routine, Action<GameState, IRandomSource> port)
    {
        var orig = new OriginalGame();
        StateMapper.ToRam(start, orig.Cpu);
        var rngO = new ScriptedRandom(seed);
        orig.Random = rngO.NextByte;
        orig.Call(routine);
        var after = StateMapper.FromRam(orig.Cpu);

        var copy = StateMapper.FromRam(CloneRam(start));
        var rngP = new ScriptedRandom(seed);
        port(copy, rngP);
        Assert.True(rngO.Position == rngP.Position, $"{routine}: random reads original {rngO.Position}, port {rngP.Position}");
        return (after, copy);
    }

    static Cpu6502 CloneRam(GameState g)
    {
        var c = new Cpu6502();
        StateMapper.ToRam(g, c);
        return c;
    }

    static void AssertSame(GameState original, GameState port, string what)
    {
        var diff = StateMapper.Diff(original, port);
        Assert.True(diff.Count == 0, what + ":\n" + string.Join("\n", diff.Take(20)));
    }

    [Fact]
    public void Binomial_matches_calcBinomial()
    {
        var orig = new OriginalGame();
        var r = new Random(5);
        for (int i = 0; i < 3000; i++)
        {
            byte a = (byte)r.Next(256), y = (byte)r.Next(0, 6);
            var rngO = new ScriptedRandom(i);
            orig.Random = rngO.NextByte;
            orig.Cpu.A = a; orig.Cpu.Y = y;
            orig.Call("calcBinomial");
            var rngP = new ScriptedRandom(i);
            byte port = rngP.Binomial(a, y);
            Assert.Equal(orig.Cpu.A, port);
            Assert.Equal(rngO.Position, rngP.Position);
        }
    }

    [Fact]
    public void Below_matches_random_0_A()
    {
        var orig = new OriginalGame();
        for (int i = 0; i < 2000; i++)
        {
            byte n = (byte)(i % 256);
            var rngO = new ScriptedRandom(i);
            orig.Random = rngO.NextByte;
            orig.Cpu.A = n;
            orig.Call("random_0_A");
            var rngP = new ScriptedRandom(i);
            Assert.Equal(orig.Cpu.A, rngP.Below(n));
            Assert.Equal(rngO.Position, rngP.Position);
        }
    }

    [Fact]
    public void ExpandCrystite_matches()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            var g = RandomState(seed);
            var r = new Random(seed);
            Array.Clear(g.PlotsTemp);
            Array.Clear(g.PlotsTemp3);
            for (int i = 0; i < 3; i++) g.PlotsTemp[r.Next(45)] = (byte)r.Next(1, 5);
            var (o, p) = Run(g, seed, "expandCrystite", (s, _) => Setup.ExpandCrystite(s));
            AssertSame(o, p, $"seed {seed}");
        }
    }

    [Fact]
    public void CalcPlotsProd_matches()
    {
        for (int seed = 0; seed < 1500; seed++)
        {
            var g = RandomState(seed);
            g.DoMissingEnergyMalus = (byte)(seed % 3 == 0 ? 1 : 0);
            var (o, p) = Run(g, seed, "calcPlotsProd", Production.CalcPlotsProd);
            AssertSame(o, p, $"seed {seed}");
        }
    }

    [Fact]
    public void CalcGoodsPriceFromNow_matches()
    {
        for (int seed = 0; seed < 3000; seed++)
        {
            var g = RandomState(seed);
            var (o, p) = Run(g, seed, "calcGoodsPriceFromNow", Prices.CalcGoodsPriceFromNow);
            AssertSame(o, p, $"seed {seed}");
        }
    }

    [Fact]
    public void BuildAndCalcPriceMule_matches()
    {
        for (int seed = 0; seed < 1000; seed++)
        {
            var g = RandomState(seed);
            var (o, p) = Run(g, seed, "buildAndCalcPriceMule", (s, _) => Prices.BuildAndCalcPriceMule(s));
            AssertSame(o, p, $"seed {seed}");
        }
    }
}
