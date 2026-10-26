using System.Text;
using Mule.Atari;
using Mule.Engine.Tests.Original;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Engine.Tests.Probes;

public class BootProbe
{
    readonly ITestOutputHelper _out;
    public BootProbe(ITestOutputHelper o) => _out = o;

    sealed class Rnd : IRandomByteSource { readonly Random r = new(7); public byte NextByte() => (byte)r.Next(256); }

    public static string ScreenText(Atari800 m)
    {
        var sb = new StringBuilder();
        ushort dl = (ushort)(m.Ram[0x230] | (m.Ram[0x231] << 8));
        ushort scan = 0;
        for (int guard = 0; guard < 200; guard++)
        {
            byte ins = m.Ram[dl++];
            int mode = ins & 15;
            if (mode == 1) { if ((ins & 0x40) != 0) break; dl = (ushort)(m.Ram[dl] | (m.Ram[(ushort)(dl + 1)] << 8)); continue; }
            if (mode == 0) continue;
            if ((ins & 0x40) != 0) { scan = (ushort)(m.Ram[dl] | (m.Ram[(ushort)(dl + 1)] << 8)); dl += 2; }
            int n = mode is 2 or 3 or 4 or 5 ? 40 : mode is 6 or 7 ? 20 : 0;
            if (n == 0) { sb.AppendLine($"[mode {mode}]"); continue; }
            var line = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                int c = m.Ram[(ushort)(scan + i)];
                if (mode >= 6) c &= 0x3F; else c &= 0x7F;
                char ch = c < 64 ? (char)(c + 32) : c < 96 ? '#' : (char)(c);
                line.Append(ch);
            }
            sb.AppendLine(line.ToString().TrimEnd());
            scan += (ushort)n;
        }
        return sb.ToString();
    }

    [Fact]
    public void Boot_new_game_from_snapshot()
    {
        var img = File.ReadAllBytes(OriginalGame.ReferencePath("MULE - Memory Snapshot.bin"));
        var m = new Atari800(img, new Rnd());
        m.Ram[0x900] = 0xFF; m.Ram[0x901] = 0xFF; m.Ram[0x902] = 0xFF; m.Ram[0x903] = 8;
        m.Ram[0x904] = 2;
        m.Cpu.PC = 0x4752; m.Cpu.I = false; m.Cpu.D = false;
        string last = "";
        for (int f = 0; f < 60 * 120; f++)
        {
            m.Buttons[0] = (f / 30) % 2 == 0;
            m.RunFrame();
            if (f % 120 == 0)
            {
                var t = ScreenText(m);
                if (t != last) { _out.WriteLine($"--- frame {f} round {m.Ram[0xB1]} PC {m.Cpu.PC:X4}\n{t}"); last = t; }
            }
        }
    }
}
