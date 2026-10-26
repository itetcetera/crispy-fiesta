using Mule.Engine.Core;
using Mule.Engine.Tests.Original;
using Xunit;

namespace Mule.Engine.Tests.Probes;

public class PestHangProbe
{
    [Theory]
    [InlineData(44)]
    [InlineData(10)]
    public void Pest_with_no_candidate(byte start)
    {
        var g = DifferentialTests.RandomState(1);
        Array.Clear(g.PlotsExp); // no food mules anywhere
        var orig = new OriginalGame();
        StateMapper.ToRam(g, orig.Cpu);
        orig[OriginalGame.Sym("animMapProd")] = 0x60; // RTS: skip the display
        bool first = true;
        orig.Random = () => { if (first) { first = false; return start; } return 0; };
        bool hung = false;
        try { orig.Call("roundEventPestAttack"); } catch (TimeoutException) { hung = true; }
        Assert.True(hung == (start == 44), $"start {start}: hung={hung}"); Console.WriteLine($"start {start} hung {hung} cycles {orig.Cpu.Cycles}");
    }
}
