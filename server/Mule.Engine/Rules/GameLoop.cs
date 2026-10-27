using System.Collections;
using Mule.Engine.Core;
using Mule.Engine.Flow;

namespace Mule.Engine.Rules;

/// <summary>A plot's production count goes up during the production animation.</summary>
public sealed record ProductionShownEvent(int Plot, int Units) : GameEvent;

/// <summary>A round starts.</summary>
public sealed record RoundStartedEvent(int Round) : GameEvent;

/// <summary>A round event happened (or the deck had none to give).</summary>
public sealed record RoundEventShownEvent(RoundEventResult? Result) : GameEvent;

/// <summary>The rounds of the game, as the original's loop at $478A runs them.</summary>
public static class GameLoop
{
    static readonly byte[] LevelsAuctionTimeWait = { 50, 30, 25 }; // $471E

    // Vertical blanks the drawing of the map runs past (drawMap starts with a VCOUNT sync).
    const int DrawMapFrames = 2;

    // Frames calcPlotsProdWithMissingEnergyMalus runs (measured on the original).
    const int ProductionFrames = 1;

    // Frames initVarsFromMaps and calcGoodsPriceFromNow run (measured on the original).
    const int PricesFrames = 1;

    /// <summary>The rounds ($478A-$484C): until the last one, or until the colony fails (addMessageFailure
    /// $8ACC, which goes back to the start once the score has been shown).</summary>
    public static IEnumerable Rounds(Game game, ScoreScreen.Outcome score)
    {
        var g = game.State;
        do
        {
            yield return Round(game, score);
            if (score.Failed) yield break;
        } while (g.NumRound < g.NbRounds);
    }

    /// <summary>round ($478A): one round, from the land grant to the score.</summary>
    public static IEnumerable Round(Game game, ScoreScreen.Outcome score)
    {
        var g = game.State;
        g.NumRound++;
        game.Trace?.Invoke("round");
        game.Emit(new RoundStartedEvent(g.NumRound));
        PlayerTurn.ExpandKnownCrystite(g);
        bool grant = (Prices.RandomFreePlot(g, game.Rng, out _) & 0x80) == 0;
        // The working out above now and then runs past a vertical blank.
        yield return game.Frames(game.Think?.Invoke("round start") ?? 0);
        if (grant)
        {
            game.Trace?.Invoke("grant");
            yield return LandGrant.Run(game);
        }
        game.Trace?.Invoke("sales");
        yield return LandAuction.LandsAuctionByPlayers(game);
        if (g.Level != 0)
        {
            byte plot = Prices.RandomFreePlot(g, game.Rng, out int free);
            int count = 0;
            if ((plot & 0x80) == 0)
            {
                count = (sbyte)game.Rng.Binomial(1, g.Level);
                if (count >= free) count = free;
            }
            if (count > 0)
            {
                game.Trace?.Invoke("auction");
                yield return LandAuction.LandsAuction(game, count);
            }
            else
            {
                // NoLandsAuction ($47A5).
                game.Trace?.Invoke("no auction");
                yield return DrawMap(game, "no auction map");
                game.Emit(new MessageEvent("NO NEW PLOTS FOR SALE."));
                yield return game.WaitJiffyLevel();
            }
        }
        yield return DrawMap(game, "map done");
        if (g.Level == 0)
        {
            g.GoodsStoreNb[Good.Smithore] = 0;
            g.GoodsStoreNb[Good.Mule] = 25;
        }
        game.Trace?.Invoke("turns");
        yield return PlayerTurn.Run(game);
        game.Trace?.Invoke("production");
        yield return DrawMap(game, "prod calc");
        Production.CalcPlotsProdWithMissingEnergyMalus(g, game.Rng);
        yield return game.Frames(game.Think?.Invoke("production") ?? ProductionFrames);
        game.Trace?.Invoke("prod done");
        if (g.NumRound >= g.NbRounds)
        {
            game.Trace?.Invoke("ship back");
            game.Emit(new MessageEvent("THE SHIP IS BACK!"));
            yield return RoundEventScreens.TransportShipLand(game);
        }
        else
        {
            game.Trace?.Invoke("round event");
            yield return RoundEventScreens.Run(game);
        }
        game.Trace?.Invoke("buttons");
        yield return PrintAndWaitPlayersButtons(game);
        Production.InitVarsFromMaps(g);
        Prices.CalcGoodsPriceFromNow(g, game.Rng);
        yield return game.Frames(game.Think?.Invoke("prices") ?? PricesFrames);
        foreach (byte good in new byte[] { Good.Smithore, Good.Crystite, Good.Food, Good.Energy })
        {
            if (good == Good.Food) g.GoodsStoreNb[Good.Crystite] = 0;
            game.Trace?.Invoke($"goods {good}");
            yield return GoodsAuction.Run(game, good);
        }
        g.LevelAuctionTimeWait = LevelsAuctionTimeWait[g.Level];
        Prices.BuildAndCalcPriceMule(g);
        game.Trace?.Invoke("score");
        yield return ScoreScreen.CalcAndDraw(game, score);
    }

    /// <summary>printAndWaitPlayersButtons ($307D) and waitPlayersButtons ($308A): until every human has
    /// pressed or 255 ticks have gone; with only CPUs, 510 frames.</summary>
    public static IEnumerable PrintAndWaitPlayersButtons(Game game)
    {
        game.Emit(new MessageEvent(" PRESS ALL PLAYER BUTTONS TO GO ON."));
        game.Vbi.InitButtons();
        yield return WaitPlayersButtons(game);
    }

    static IEnumerable WaitPlayersButtons(Game game)
    {
        var v = game.Vbi;
        v.SoundOnPress = 1;
        if (game.State.MaxIndexCpu == 3)
        {
            // Left on: the sound of a press stays on into the goods auction.
            v.Presses = 4;
            yield return game.WaitJiffy(255);
            yield return game.WaitJiffy(255);
            yield break;
        }
        yield return LandAuction.WaitButtonsTick(game, 0xFF);
    }

    static IEnumerable DrawMap(Game game, string done)
    {
        game.Emit(new MapShownEvent());
        yield return game.Frames(game.Think?.Invoke(done) ?? DrawMapFrames);
        game.Trace?.Invoke(done);
    }

    /// <summary>The random picks that miss a plot cost about a fifth of a scan line each: after a few
    /// hundred the search for the last units runs past a vertical blank (measured on the original).</summary>
    static int MissFrames(int reads)
    {
        // Lines from the end of the wait (line ~254) to the next wait, which starts past the next
        // vertical blank once they reach ~256 (fitted over 3,900 waits; within ±6 lines for 90%).
        double lines = 3 + reads * 0.212;
        return lines < 256 ? 0 : 1 + (int)((lines - 256) / 262);
    }

    sealed class Counting : IRandomSource
    {
        readonly IRandomSource _r;
        public int Reads;
        public Counting(IRandomSource r) => _r = r;
        public byte NextByte() { Reads++; return _r.NextByte(); }
    }

    /// <summary>animMapProd ($4A9D): the units appear one at a time on random plots, a random 1-16
    /// frames apart, until every plot shows its production.</summary>
    public static IEnumerable AnimMapProd(Game game)
    {
        var g = game.State;
        game.Trace?.Invoke("prod anim");
        if (g.NumRound == 0) yield break;
        game.Emit(new TitleEvent("PRODUCTION"));
        yield return game.WaitJiffy(6);
        var shown = g.PlotsTemp;
        Array.Clear(shown);
        bool remaining = false;  // byte_89
        bool any = false;        // byte_8D
        var rng = new Counting(game.Rng);
        while (true)
        {
            int x = rng.Below(45);
            if (g.PlotsExp[x] != 0 && shown[x] < g.PlotsProd[x])
            {
                // The search for this unit ran this many lines (its last read is the wait's).
                yield return game.Frames(MissFrames(rng.Reads + 1));
                remaining = false;
                shown[x]++;
                game.Emit(new ProductionShownEvent(x, shown[x]));
                any = true;
                game.Vbi.Sound = 6;
                byte n = (byte)((rng.NextByte() & 0x0F) + 1);
                rng.Reads = 0;
                yield return game.WaitJiffy(n);
            }
            if (remaining) continue;
            for (int p = 44; p >= 0 && !remaining; p--)
                if (g.PlotsExp[p] != 0 && shown[p] < g.PlotsProd[p]) remaining = true;
            if (!remaining) break;
        }
        yield return game.Frames(MissFrames(rng.Reads));
        if (any) yield return game.WaitSoundThenBeep();
    }
}
