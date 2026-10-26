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

    public OriginalGame(int seed = 1)
    {
        Snapshot.Value.CopyTo(Cpu.Mem, 0);
        var rng = new System.Random(seed);
        Random = () => (byte)rng.Next(256);
        Cpu.ReadHook = a =>
        {
            if (a == RandomRegister) { RandomReads++; return Random(); }
            return null;
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

    public void Call(string label) => Cpu.Call(Sym(label));
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
