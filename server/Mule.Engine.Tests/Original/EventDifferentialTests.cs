using Mule.Engine.Core;
using Mule.Engine.Rules;
using Xunit;

namespace Mule.Engine.Tests.Original;

/// <summary>Round and player events against the original, with display/sound/wait routines patched to RTS.</summary>
public class EventDifferentialTests
{
    static readonly string[] Cosmetic =
    {
        "moveWampus", "animMapProd", "printTitle", "prepareStaticMessage", "addMessage", "waitJiffy", "waitJiffyLevel",
        "drawCellProd", "addMessageStart", "addMessageEnd", "soundPlayerEvent",
        "animShipLand", "anipShipTakeOff", "sub_768B", "sub_4C61", "sub_136D", "sub_1363", "C1_255", "sub_7E87",
        "sub_48A6", "sub_3974", "sub_7C71", "sub_7FC6_display", "loc_6C9B", "addMessageGood",
        "addNumberToMessage", "sub_4B89", "sub_5E1B", "loc_4C96", "wait6",
    };

    internal static OriginalGame Patched()
    {
        var o = new OriginalGame();
        foreach (var name in Cosmetic)
            if (TryAddr(name, out var a)) o[a] = 0x60;
        return o;
    }

    static bool TryAddr(string name, out ushort a)
    {
        try { a = OriginalGame.Sym(name); return true; } catch (KeyNotFoundException) { a = 0; return false; }
    }

    [Fact]
    public void PlayerEvent_matches()
    {
        int happened = 0;
        for (int seed = 0; seed < 4000; seed++)
        {
            var g = DifferentialTests.RandomState(seed);
            var r = new Random(seed);
            for (int i = 0; i < 22; i++) g.TurnEventsHappened[i] = (byte)(r.Next(3) == 0 ? 1 : 0);
            r.Shuffle(g.OrdersPlayer);
            int player = r.Next(4);
            if (r.Next(4) == 0) g.GoodsPlayersNb[player] = 0;

            g.CurrentPlayer = (byte)player;
            var orig = Patched();
            StateMapper.ToRam(g, orig.Cpu);
            var rngO = new ScriptedRandom(seed);
            // Force the 27.5% roll to hit most of the time so events are exercised.
            orig.Random = rngO.NextByte;
            orig.Call("playerEvent");
            var after = StateMapper.FromRam(orig.Cpu);

            var port = StateMapper.FromRam(Clone(g));
            var rngP = new ScriptedRandom(seed);
            var res = PlayerEvents.Run(port, rngP, player);
            if (res != null) happened++;
            Assert.Equal(rngO.Position, rngP.Position);
            var diff = StateMapper.Diff(after, port);
            Assert.True(diff.Count == 0, $"seed {seed} player {player}: " + string.Join("; ", diff.Take(10)));
        }
        Assert.True(happened > 500, $"only {happened} events");
    }

    [Theory]
    [InlineData("roundEventPestAttack", 0)]
    [InlineData("roundEventPirateShip", 1)]
    [InlineData("roundEventAcidRainStorm", 2)]
    [InlineData("roundEventPlanetquake", 3)]
    [InlineData("roundEventSunspotActivity", 4)]
    [InlineData("roundEventMeteoriteStrike", 5)]
    [InlineData("roundEventRadiation", 6)]
    [InlineData("roundEventFireInStore", 7)]
    public void RoundEvent_matches(string routine, int kind)
    {
        for (int seed = 0; seed < 25; seed++)
        {
            var g = DifferentialTests.RandomState(seed);
            new Random(seed).Shuffle(g.OrdersPlayer);
            var orig = new MachineGame();
            // Only the logic's RANDOM reads use the shared script; animation reads are display-only.
            orig.Cosmetic = true;
            var (logicOn, logicOff) = LogicWindows[kind];
            foreach (var pc in logicOn) orig.LogicFrom.Add(pc);
            foreach (var pc in logicOff) orig.CosmeticFrom.Add(pc);
            StateMapper.ToRam(g, orig.Ram);
            orig.Ram[0xC1] = 0xFF; // no per-phase VBI handler (the snapshot's ship handler would clobber $AA)
            var rngO = new ScriptedRandom(seed);
            orig.Random = rngO.NextByte;
            if (kind == 0 && !PestTerminates(g, rngO.Clone())) continue;
            orig.Call(routine);
            var after = StateMapper.FromRam(orig.Ram);

            var port = StateMapper.FromRam(Clone(g));
            var rngP = new ScriptedRandom(seed);
            Func<GameState, IRandomSource, RoundEventResult> f = kind switch
            {
                0 => RoundEvents.PestAttack,
                1 => (s, _) => RoundEvents.PirateShip(s),
                2 => RoundEvents.AcidRain,
                3 => RoundEvents.Planetquake,
                4 => (s, _) => RoundEvents.Sunspots(s),
                5 => RoundEvents.Meteorite,
                6 => RoundEvents.Radiation,
                _ => (s, _) => RoundEvents.FireInStore(s),
            };
            f(port, rngP);
            Assert.Equal(rngO.Position, rngP.Position);
            var diff = StateMapper.Diff(after, port, "plotsTemp", "plotsTemp3", "mapScreen"); // TODO: the event text is printed over the map
            Assert.True(diff.Count == 0, $"{routine} seed {seed}: " + string.Join("; ", diff.Take(10)));
        }
    }

    /// <summary>Per event: addresses where the logic's random reads start, and where they end.</summary>
    static readonly (ushort[] on, ushort[] off)[] LogicWindows =
    {
        (new ushort[] { 0x78CE }, new ushort[] { 0x78FC }),          // pest: random start plot
        (new ushort[0], new ushort[0]),                               // pirates: no random
        (new ushort[] { 0x7A83 }, new ushort[] { 0x7A8A }),          // acid rain: row
        (new ushort[] { 0x7244 }, new ushort[] { 0x729A }),          // quake: coin, quarter, direction
        (new ushort[0], new ushort[0]),                               // sunspots: no random
        (new ushort[] { 0x7CB4 }, new ushort[] { 0x7CC3 }),          // meteorite: plot
        (new ushort[] { 0x7EA6 }, new ushort[] { 0x7EC9, 0x7ECC }),  // radiation: start plot
        (new ushort[0], new ushort[0]),                               // fire: no random
    };

    /// <summary>Skips the original's hang case (start 44 with no target).</summary>
    static bool PestTerminates(GameState g, ScriptedRandom rng)
    {
        int start = rng.Below(45);
        if (start != 44) return true;
        for (int x = 0; x < 45; x++)
            if (g.PlotsExp[x] == Exp.Food && g.PlotsProd[x] != 0 && g.PlotsPlayer[x] != g.OrdersPlayer[3] && g.PlotsPlayer[x] != g.OrdersPlayer[2])
                return true;
        return false;
    }

    static Cpu6502 Clone(GameState g) { var c = new Cpu6502(); StateMapper.ToRam(g, c); return c; }
}
