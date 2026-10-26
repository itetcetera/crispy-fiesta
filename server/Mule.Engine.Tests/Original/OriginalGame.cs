using System.Text.RegularExpressions;

namespace Mule.Engine.Tests.Original;

/// <summary>
/// The original game loaded from the memory snapshot into a 6502 emulator, with labels from the
/// disassembly listing and a scripted POKEY random register.
/// </summary>
public sealed class OriginalGame
{
    public const ushort RandomRegister = 0xD20A;

    static readonly Lazy<byte[]> Snapshot = new(() => File.ReadAllBytes(ReferencePath("MULE - Memory Snapshot.bin")));
    static readonly Lazy<Dictionary<string, ushort>> SymbolTable = new(LoadSymbols);

    public readonly Cpu6502 Cpu = new();

    /// <summary>Source of random bytes; defaults to a seeded generator.</summary>
    public Func<byte> Random;

    public int RandomReads { get; private set; }

    /// <summary>While true, RANDOM reads return 0 and do not consume the scripted source (display-only code).</summary>
    public bool Cosmetic;

    /// <summary>When true, RANDOM reads made inside the vertical blank interrupt are treated as display-only.</summary>
    public bool CosmeticInVblank = true;

    /// <summary>Instruction addresses that switch <see cref="Cosmetic"/> on (the logic is done past them).</summary>
    public readonly HashSet<ushort> CosmeticFrom = new();

    /// <summary>Instruction addresses that switch <see cref="Cosmetic"/> off again.</summary>
    public readonly HashSet<ushort> LogicFrom = new();

    readonly System.Random _cosmeticRng = new(12345);

    /// <summary>Joystick trigger registers TRIG0-3: 0 = pressed, 1 = released.</summary>
    public readonly byte[] Triggers = { 1, 1, 1, 1 };

    /// <summary>Joystick directions (4-bit, bit clear = pushed: 1 up, 2 down, 4 left, 8 right). 15 = centered.</summary>
    public readonly byte[] Sticks = { 15, 15, 15, 15 };

    /// <summary>Turns on the 60 Hz vertical blank interrupt.</summary>
    public void EnableVblank() => Cpu.VblankEnabled = true;

    public OriginalGame(int seed = 1)
    {
        Snapshot.Value.CopyTo(Cpu.Mem, 0);
        var rng = new System.Random(seed);
        Random = () => (byte)rng.Next(256);
        Cpu.ReadHook = a =>
        {
            if (a == RandomRegister)
            {
                if (Cosmetic || (CosmeticInVblank && Cpu.InNmi)) return (byte)_cosmeticRng.Next(256);
                RandomReads++;
                return Random();
            }
            if (a == 0xD40F) return 0x5F; // NMIST: vertical blank
            if (a == 0xD40B) return (byte)(Cpu.Cycles % Cpu6502.CyclesPerFrame / 114 / 2); // VCOUNT
            if (a == 0xD010 || a == 0xD011 || a == 0xD012 || a == 0xD013) return Triggers[a - 0xD010];
            if (a == 0xD300) return (byte)(Sticks[0] | (Sticks[1] << 4));
            if (a == 0xD301) return (byte)(Sticks[2] | (Sticks[3] << 4));
            return null;
        };
        Cpu.BeforeStep = c =>
        {
            if (CosmeticFrom.Contains(c.PC)) Cosmetic = true;
            if (LogicFrom.Contains(c.PC)) Cosmetic = false;
        };
    }

    public byte this[ushort addr] { get => Cpu.Mem[addr]; set => Cpu.Mem[addr] = value; }
    public byte this[string label, int offset = 0]
    {
        get => Cpu.Mem[(ushort)(Sym(label) + offset)];
        set => Cpu.Mem[(ushort)(Sym(label) + offset)] = value;
    }

    public static ushort Sym(string label) =>
        SymbolTable.Value.TryGetValue(label, out var a) ? a : throw new KeyNotFoundException(label);

    public void Call(string label, ushort? stopAt = null, long maxSteps = 50_000_000) => Cpu.Call(Sym(label), maxSteps, stopAt);

    /// <summary>Reads a 6-byte Atari BCD floating point number (exponent byte, base 100).</summary>
    public long ReadFloat(ushort addr)
    {
        byte e = Cpu.Mem[addr];
        if (e == 0) return 0;
        if ((e & 0x80) != 0) throw new InvalidOperationException("negative float");
        long v = 0;
        for (int i = 1; i <= 5; i++)
        {
            byte b = Cpu.Mem[addr + i];
            v = v * 100 + (b >> 4) * 10 + (b & 15);
        }
        int shift = (e - 64) - 4; // mantissa holds 5 base-100 digits, first at 100^(e-64)
        for (; shift < 0; shift++) v /= 100;
        for (; shift > 0; shift--) v *= 100;
        return v;
    }
    public void Call(ushort addr) => Cpu.Call(addr);

    static Dictionary<string, ushort> LoadSymbols()
    {
        var re = new Regex(@"^([0-9A-F]{4}) ([A-Za-z_][A-Za-z0-9_]*):");
        var d = new Dictionary<string, ushort>();
        foreach (var line in File.ReadLines(ReferencePath("MULE - Disassembled Memory.asm")))
        {
            var m = re.Match(line.TrimStart('﻿'));
            if (m.Success) d.TryAdd(m.Groups[2].Value, Convert.ToUInt16(m.Groups[1].Value, 16));
        }
        return d;
    }

    public static string ReferencePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "reference")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("reference/ folder not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName, "reference", file);
    }
}
