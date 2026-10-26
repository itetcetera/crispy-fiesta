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
        new("playersGoodHeld", 0xB319, g => g.PlayersGoodHeld),
        new("playersGoodGap", 0xB1B1, g => g.PlayersGoodGap),
        new("playersGoodShort", 0xB1AD, g => g.PlayersGoodShort),
        new("ordersPlayer", 0xB328, g => g.OrdersPlayer),
        new("roundEventsProb", 0xB342, g => g.RoundEventsProb),
        new("playersAi", 0x0900, g => g.PlayersAi),
        new("playersRace", 0x091D, g => g.PlayersRace),
        new("playersTurnTimeWait", 0xB370, g => g.PlayersTurnTimeWait),
        new("outfitsPriceL", 0xB2B1, g => g.OutfitsPrice),
        new("turnEventsHappened", 0xB34A, g => g.TurnEventsHappened),
        new("landsToSell", 0xB2C2, g => g.LandsToSell),
        new("goodsShortage", 0xB19D, g => g.GoodsShortage),
        new("goodsQuality", 0xB20C, g => g.GoodsQuality),
        new("playersPlotsQuality", 0xB379, g => g.PlayersPlotsQuality),
    };

    static readonly WordField[] Words =
    {
        new("playersCash", 0xB2BA, g => g.PlayersCash),
        new("goodsPrice", 0xB2F1, g => g.GoodsPrice),
        new("goodsPriceLimit", 0x3277, g => g.GoodsPriceLimit),
        new("goodsPriceTemp", 0xB206, g => g.GoodsPriceTemp),
        new("lastPriceStart", 0xB2E3, g => g.LastPriceStart),
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
        new("currentPlayer", 0x00AC, g => g.CurrentPlayer, (g, v) => g.CurrentPlayer = v),
        new("nbLandsToSellByPlayers", 0x00CA, g => g.NbLandsToSellByPlayers, (g, v) => g.NbLandsToSellByPlayers = v),
        new("lastBidStartLaL", 0xB2FB, g => (byte)g.LastBidStartLa, (g, v) => g.LastBidStartLa = (ushort)((g.LastBidStartLa & 0xFF00) | v)),
        new("lastBidStartLaH", 0xB2FC, g => (byte)(g.LastBidStartLa >> 8), (g, v) => g.LastBidStartLa = (ushort)((g.LastBidStartLa & 0xFF) | (v << 8))),
        new("levelAuctionTimeWait", 0xB327, g => g.LevelAuctionTimeWait, (g, v) => g.LevelAuctionTimeWait = v),
        new("auctionTimeReload", 0x9CEF, g => g.AuctionTimeReload, (g, v) => g.AuctionTimeReload = v),
        new("goodsTimeReload", 0x9B8B, g => g.GoodsTimeReload, (g, v) => g.GoodsTimeReload = v),
        new("oldRiverStoreX", 0xB249, g => g.OldRiverStoreX, (g, v) => g.OldRiverStoreX = v),
        new("oldMountainX", 0xB24A, g => g.OldMountainX, (g, v) => g.OldMountainX = v),
        new("nbRounds", 0xB36F, g => g.NbRounds, (g, v) => g.NbRounds = v),
        new("cursorWait", 0xB36D, g => g.CursorWait, (g, v) => g.CursorWait = v),
        new("maxIndexCpu", 0x00D2, g => g.MaxIndexCpu, (g, v) => g.MaxIndexCpu = v),
    };

    public static void ToRam(GameState g, Cpu6502 cpu) => ToRam(g, cpu.Mem);
    public static GameState FromRam(Cpu6502 cpu) => FromRam(cpu.Mem);

    public static void ToRam(GameState g, byte[] mem)
    {
        foreach (var f in Bytes) f.Get(g).CopyTo(mem, f.Addr);
        foreach (var f in Words)
        {
            var a = f.Get(g);
            for (int i = 0; i < a.Length; i++) { mem[f.Addr + 2 * i] = (byte)a[i]; mem[f.Addr + 2 * i + 1] = (byte)(a[i] >> 8); }
        }
        foreach (var f in Scalars) mem[f.Addr] = f.Get(g);
    }

    public static GameState FromRam(byte[] mem)
    {
        var g = new GameState();
        foreach (var f in Bytes) Array.Copy(mem, f.Addr, f.Get(g), 0, f.Get(g).Length);
        foreach (var f in Words)
        {
            var a = f.Get(g);
            for (int i = 0; i < a.Length; i++) a[i] = (ushort)(mem[f.Addr + 2 * i] | (mem[f.Addr + 2 * i + 1] << 8));
        }
        foreach (var f in Scalars) f.Set(g, mem[f.Addr]);
        return g;
    }

    /// <summary>Lists every field that differs between two states (empty when equal).</summary>
    public static List<string> Diff(GameState expected, GameState actual, params string[] ignore)
    {
        var d = new List<string>();
        foreach (var f in Bytes)
        {
            if (ignore.Contains(f.Name)) continue;
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
