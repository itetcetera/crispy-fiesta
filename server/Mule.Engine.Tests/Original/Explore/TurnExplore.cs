using Mule.Engine.Core;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Original.Explore;

/// <summary>Scratch: watch the original player turn.</summary>
public class TurnExplore
{
    readonly ITestOutputHelper _out;
    public TurnExplore(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Watch_turn()
    {
        if (Environment.GetEnvironmentVariable("EXPLORE") == null) return;
        var g = DifferentialTests.RandomState(1);
        for (int p = 0; p < 4; p++) g.PlayersAi[p] = (byte)(8 + p);
        g.MaxIndexCpu = 0xFF;
        var m = new MachineGame();
        StateMapper.ToRam(g, m.Ram);
        m.Ram[0xC1] = 0xFF; m.Ram[0x4E] = 0; m.Ram[0x9E] = 0xFF; m.Ram[0xC8] = 0;
        var rng = new ScriptedRandom(1);
        m.Random = rng.NextByte;
        long vbi = 0;
        string last = "", lastPc = "";
        var r = m.Ram;
        m.M.AfterStep = c =>
        {
            var pcs = new Dictionary<int,string>{{0x6645,"player"},{0x66CB,"event"},{0x66CE,"evdone"},{0x66F0,"poll"},{0x66F5,"poll2"},{0x6741,"pressed"},{0x67C4,"food"},{0x67E0,"human"},{0x67E3,"town"},{0x6800,"enter"},{0x6B14,"exit"},{0x6A82,"gamble"},{0x6C71,"timeout"},{0x6AF2,"next"},{0x6ADC,"end"},{0x6AD4,"anim"},{0x6AD7,"animdone"},{0x4A32,"msg"},{0x6992,"bldgdone"},{0x69D0,"outfit"}};
            if (pcs.TryGetValue(c.PC, out var nm) && nm != lastPc && (lastPc = nm) != null) _out.WriteLine($"{vbi} ** {nm} X={c.X} A={c.A}");
            if (c.PC != 0x5BB5) return;
            vbi++;
            int f = (int)vbi;
            for (int p = 0; p < 4; p++) { m.M.Buttons[p] = f % 300 > 290 && f < 1500; m.M.Sticks[p] = (byte)(f < 900 ? 15 : f < 1100 ? 0xB : f < 1300 ? 0xE : 0xD); }
            string s = $"cp {r[0xAC]} x {r[0x9F]:X2} y {r[0xA3]:X2} d3 {r[0xD3]} ad {r[0xAD]} c1 {r[0xC1]:X2} bc {r[0xBC]} e0 {r[0xE0]} af {r[0xAF]} c2 {r[0xC2]} e3 {r[0xE3]:X2} b2 {r[0xB2]}{r[0xB3]}{r[0xB4]}{r[0xB5]} p0pl? ";
            if (s != last) { _out.WriteLine($"{vbi} {s}"); last = s; }
        };
        m.M.CallRoutine(OriginalGame.Sym("playersTurn"), 4000);
    }
}
