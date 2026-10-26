using Mule.Engine.Core;

namespace Mule.Engine.Tests.Original;

/// <summary>Copies a <see cref="GameState"/> to and from the original game's RAM.</summary>
public static class StateMapper
{
    sealed record ByteField(string Name, ushort Addr, Func<GameState, byte[]> Get);
    sealed record WordField(string Name, ushort Addr, Func<GameState, ushort[]> Get);
    sealed record ScalarField(string Name, ushort Addr, Func<GameState, byte> Get, Action<GameState, byte> Set);

    static readonly ByteField[] Bytes =
    {
        new("plotsPlayer", 0xB000, g => g.PlotsPlayer),
        new("plotsExp", 0xB02D, g => g.PlotsExp),
        new("plotsProd", 0xB05A, g => g.PlotsProd),
        new("plotsTemp", 0xB087, g => g.PlotsTemp),
        new("expsPlotsCapacity", 0xB0B4, g => g.ExpsPlotsCapacity),
        new("plotsTemp3", 0xB42D, g => g.PlotsTemp3),
        new("plotsKnownCrystite", 0xB45A, g => g.PlotsKnownCrystite),
        new("mountainsX", 0xB221, g => g.MountainsX),
        new("riverStoreX", 0xB235, g => g.RiverStoreX),
        new("goodsPlayersNb", 0xB1C2, g => g.GoodsPlayersNb),
        new("expsPlayersNb", 0xB1D6, g => g.ExpsPlayersNb),
        new("goodsPlayersProd", 0xB1EA, g => g.GoodsPlayersProd),
        new("playersNbLands", 0xB1FE, g => g.PlayersNbLands),
        new("playersNbExps", 0xB202, g => g.PlayersNbExps),
        new("goodsStoreNb", 0xB212, g => g.GoodsStoreNb),
        new("goodsFuturNb", 0xB332, g => g.GoodsFuturNb),
        new("goodsFuturReq", 0xB335, g => g.GoodsFuturReq),
        new("playersGoodReq", 0x00CC, g => g.PlayersGoodReq),
        new("ordersPlayer", 0xB328, g => g.OrdersPlayer),
        new("roundEventsProb", 0xB342, g => g.RoundEventsProb),
        new("playersAi", 0x0900, g => g.PlayersAi),
        new("playersRace", 0x091D, g => g.PlayersRace),
        new("playersTurnTimeWait", 0xB370, g => g.PlayersTurnTimeWait),
        new("outfitsPriceL", 0xB2B1, g => g.OutfitsPrice),
    };

    static readonly WordField[] Words =
    {
        new("playersCash", 0xB2BA, g => g.PlayersCash),
        new("goodsPrice", 0xB2F1, g => g.GoodsPrice),
        new("goodsPriceLimit", 0x3277, g => g.GoodsPriceLimit),
    };

    static readonly ScalarField[] Scalars =
    {
        new("level", 0x00B0, g => g.Level, (g, v) => g.Level = v),
        new("numRound", 0x00B1, g => g.NumRound, (g, v) => g.NumRound = v),
        new("foodReq", 0xB376, g => g.FoodReq, (g, v) => g.FoodReq = v),
        new("doMissingEnergyMalus", 0xB377, g => g.DoMissingEnergyMalus, (g, v) => g.DoMissingEnergyMalus = v),
        new("aiMode", 0xB375, g => g.AiMode, (g, v) => g.AiMode = v),
        new("mulePriceL", 0xB2B9, g => (byte)g.MulePrice, (g, v) => g.MulePrice = (ushort)((g.MulePrice & 0xFF00) | v)),
        new("mulePriceH", 0xB367, g => (byte)(g.MulePrice >> 8), (g, v) => g.MulePrice = (ushort)((g.MulePrice & 0xFF) | (v << 8))),
        new("prodMax", 0x0094, g => g.ProdMax, (g, v) => g.ProdMax = v),
    };

    public static void ToRam(GameState g, Cpu6502 cpu)
    {
        foreach (var f in Bytes) f.Get(g).CopyTo(cpu.Mem, f.Addr);
        foreach (var f in Words)
        {
            var a = f.Get(g);
            for (int i = 0; i < a.Length; i++) { cpu.Mem[f.Addr + 2 * i] = (byte)a[i]; cpu.Mem[f.Addr + 2 * i + 1] = (byte)(a[i] >> 8); }
        }
        foreach (var f in Scalars) cpu.Mem[f.Addr] = f.Get(g);
    }

    public static GameState FromRam(Cpu6502 cpu)
    {
        var g = new GameState();
        foreach (var f in Bytes) Array.Copy(cpu.Mem, f.Addr, f.Get(g), 0, f.Get(g).Length);
        foreach (var f in Words)
        {
            var a = f.Get(g);
            for (int i = 0; i < a.Length; i++) a[i] = (ushort)(cpu.Mem[f.Addr + 2 * i] | (cpu.Mem[f.Addr + 2 * i + 1] << 8));
        }
        foreach (var f in Scalars) f.Set(g, cpu.Mem[f.Addr]);
        return g;
    }

    /// <summary>Lists every field that differs between two states (empty when equal).</summary>
    public static List<string> Diff(GameState expected, GameState actual)
    {
        var d = new List<string>();
        foreach (var f in Bytes)
        {
            var a = f.Get(expected); var b = f.Get(actual);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d.Add($"{f.Name}[{i}]: original {a[i]}, port {b[i]}");
        }
        foreach (var f in Words)
        {
            var a = f.Get(expected); var b = f.Get(actual);
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) d.Add($"{f.Name}[{i}]: original {a[i]}, port {b[i]}");
        }
        foreach (var f in Scalars)
            if (f.Get(expected) != f.Get(actual)) d.Add($"{f.Name}: original {f.Get(expected)}, port {f.Get(actual)}");
        return d;
    }
}

/// <summary>A fixed byte sequence that both the original and the port read from.</summary>
public sealed class ScriptedRandom : IRandomSource
{
    readonly byte[] _bytes;
    public int Position { get; private set; }
    public ScriptedRandom(int seed, int length = 1 << 16)
    {
        _bytes = new byte[length];
        new Random(seed).NextBytes(_bytes);
    }
    public byte NextByte() => _bytes[Position++ % _bytes.Length];
    public ScriptedRandom Clone() { var c = (ScriptedRandom)MemberwiseClone(); return c; }
    public void Reset() => Position = 0;
}
