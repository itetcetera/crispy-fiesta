using Mule.Atari;

namespace Mule.Engine.Tests.Original;

/// <summary>
/// The original game in the scanline Atari 800 (display DMA, interrupts and collisions as on the real
/// machine). RANDOM reads made by game logic come from <see cref="Random"/>; reads made by animation
/// and sound code (outside the logic windows, or inside the vertical blank) come from a separate
/// generator so that display code cannot shift the logic's random sequence.
/// </summary>
public sealed class MachineGame : IRandomByteSource
{
    static readonly Lazy<byte[]> Snapshot = new(() => File.ReadAllBytes(OriginalGame.ReferencePath("MULE - Memory Snapshot.bin")));

    public readonly Atari800 M;
    public Func<byte> Random = () => 0;
    public bool Cosmetic;
    public readonly HashSet<ushort> CosmeticFrom = new(), LogicFrom = new();
    readonly System.Random _cosmetic = new(12345);

    public MachineGame()
    {
        M = new Atari800(Snapshot.Value, this);
        M.Cpu.I = false;
        M.Cpu.D = false;
        M.Ram[0x4D] = 0; // attract-mode counter
        M.AfterStep = c =>
        {
            if (CosmeticFrom.Contains(c.PC)) Cosmetic = true;
            if (LogicFrom.Contains(c.PC)) Cosmetic = false;
        };
    }

    public byte NextByte() => Cosmetic || M.InVblank ? (byte)_cosmetic.Next(256) : Random();

    public byte[] Ram => M.Ram;

    public void Call(string label, long maxFrames = 100_000)
    {
        ushort addr = OriginalGame.Sym(label);
        if (CosmeticFrom.Contains(addr)) Cosmetic = true;
        if (LogicFrom.Contains(addr)) Cosmetic = false;
        M.CallRoutine(addr, maxFrames);
    }
}
