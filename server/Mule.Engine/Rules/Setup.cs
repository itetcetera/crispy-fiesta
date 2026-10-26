using Mule.Engine.Core;

namespace Mule.Engine.Rules;

/// <summary>Seat configuration for a new game.</summary>
public sealed record SeatSetup(bool Cpu, Race Race, byte Color);

/// <summary>initGame ($4293): map generation and starting values (tournament).</summary>
public static class Setup
{
    /// <summary>Creates a tournament game. CPUs must take the lowest seat numbers, as in the original.</summary>
    public static GameState NewGame(IRandomSource rng, IReadOnlyList<SeatSetup> seats)
    {
        if (seats.Count != GameState.Players) throw new ArgumentException("4 seats required");
        bool seenHuman = false;
        foreach (var s in seats)
        {
            if (!s.Cpu) seenHuman = true;
            else if (seenHuman) throw new ArgumentException("CPU seats must come before human seats");
        }

        var g = new GameState();
        for (int p = 0; p < GameState.Players; p++)
        {
            g.PlayersAi[p] = seats[p].Cpu ? (byte)0xFF : (byte)8;
            g.PlayersRace[p] = (byte)seats[p].Race;
            g.PlayersColor[p] = seats[p].Color;
        }
        GenerateMap(g, rng);
        InitValues(g);
        return g;
    }

    /// <summary>$4330-$4508: crystite seeds, mountains, mountain/river x positions, base outputs.</summary>
    public static void GenerateMap(GameState g, IRandomSource rng)
    {
        Array.Fill(g.PlotsPlayer, GameState.Nobody);
        Array.Clear(g.PlotsTemp);
        Array.Clear(g.PlotsTemp3);

        // Crystite: 3 seeds of value 3 at distinct plots.
        for (int y = 3; y != 0;)
        {
            int x = rng.Below(45);
            if (g.PlotsTemp[x] == 0) { g.PlotsTemp[x] = 3; y--; }
        }
        ExpandCrystite(g);
        for (int x = 44; x >= 0; x--)
        {
            g.PlotsTemp[x] = 0;
            g.ExpsPlotsCapacity[3 * GameState.Plots + x] = g.PlotsTemp3[x];
        }

        // Mountains: per row, one plot left of the river (columns 0-3), one right (5-8),
        // 4 units split between them, never all on one side.
        for (int row = 0; row <= 4; row++)
        {
            byte colL = (byte)(rng.NextByte() & 3);
            int left = GameState.Index(row, colL);
            byte colR = (byte)((rng.NextByte() & 3) + 5);
            int right = GameState.Index(row, colR);
            int onRight;
            do
            {
                onRight = 0;
                g.PlotsTemp[left] = 0;
                g.PlotsTemp[right] = 0;
                for (int quarter = 0; quarter <= 3; quarter++)
                {
                    byte col = colL;
                    int idx = left;
                    if (rng.Negative()) { col = colR; idx = right; onRight++; }
                    g.PlotsTemp[idx]++;
                    // x = col*16 + random(0..7) + 57 (the original ADC keeps a clear carry here).
                    byte x = (byte)((col << 4) + (rng.NextByte() & 7) + 57);
                    g.MountainsX[row * 4 + quarter] = x;
                }
            } while (onRight == 0 || onRight == 4);
        }

        // Mountain glyph styles (cosmetic) read 20 random bytes ($43D9); river glyph rows read 116 ($4405).
        for (int i = 0; i < 20; i++) rng.NextByte();
        for (int y = 241; y >= 10; y -= 2) rng.NextByte();

        GenerateRiverStoreX(g, rng);
        g.OldRiverStoreX = g.RiverStoreX[0];
        g.OldMountainX = g.MountainsX[0];

        // Base output table ($44BC).
        for (int x = 44; x >= 0; x--)
        {
            g.ExpsPlotsCapacity[x] = 2;                    // food
            g.ExpsPlotsCapacity[GameState.Plots + x] = 3;  // energy
            g.PlotsKnownCrystite[x] = 128;
            if (x == 4 || x == 13 || x == 31 || x == 22 || x == 40)
            {
                g.ExpsPlotsCapacity[x] += 2;
                g.ExpsPlotsCapacity[GameState.Plots + x]--;
                g.ExpsPlotsCapacity[2 * GameState.Plots + x] = 0xFF;
                g.ExpsPlotsCapacity[3 * GameState.Plots + x] = 0xFF;
            }
            byte m = g.PlotsTemp[x];
            if (m != 0)
            {
                g.ExpsPlotsCapacity[x] = 1;
                g.ExpsPlotsCapacity[GameState.Plots + x] = 1;
            }
            g.ExpsPlotsCapacity[2 * GameState.Plots + x] = (byte)(m + 1);
        }
    }

    static void GenerateRiverStoreX(GameState g, IRandomSource rng)
    {
        var r = g.RiverStoreX;
        for (int y = 8; y < 12; y++) r[y] = 0x78;
        r[0] = 0x7C;
        r[19] = (byte)(rng.Below(7) - 3 + r[0]);
        for (int i = 1; i < 8; i++)
        {
            byte a = (byte)(rng.Below(5) - 2 + r[i - 1]);
            if (a < 119) a = 119;
            if (a >= 130) a = 129;
            r[i] = a;
        }
        for (int i = 18; i >= 12; i--)
        {
            byte a = (byte)(rng.Below(5) - 2 + r[i + 1]);
            if (a < 119) a = 119;
            if (a >= 130) a = 129;
            r[i] = a;
        }
    }

    /// <summary>expandCrystite ($48C9): plotsTemp3 = max over seeds of (seed - Manhattan distance), within a box.</summary>
    public static void ExpandCrystite(GameState g)
    {
        for (int row = 0; row <= 4; row++)
            for (int col = 0; col <= 8; col++)
            {
                byte v = g.PlotsTemp[GameState.Index(row, col)];
                if (v == 0) continue;
                int r = v - 1;
                int left = Math.Max(col - r, 0), right = Math.Min(col + r, 8);
                int top = Math.Max(row - r, 0), bottom = Math.Min(row + r, 4);
                int strength = r + 1;
                for (int c = left; c <= right; c++)
                    for (int rr = top; rr <= bottom; rr++)
                    {
                        int d = strength - (Math.Abs(col - c) + Math.Abs(row - rr));
                        if (d <= 0) continue;
                        int idx = GameState.Index(rr, c);
                        if ((g.PlotsTemp3[idx] & 0x0F) <= d) g.PlotsTemp3[idx] = (byte)d;
                    }
            }
    }

    /// <summary>$450A-$4714: starting cash, goods, store, prices, timers, race and CPU adjustments.</summary>
    public static void InitValues(GameState g)
    {
        g.NbPaddles = 0;
        g.MaxIndexCpu = 0xFF;
        for (int x = 3; x >= 0; x--)
        {
            if ((g.PlayersAi[x] & 0x80) != 0) g.MaxIndexCpu++;
            if (g.PlayersAi[x] < 8) g.NbPaddles++;
        }
        g.Level = 2;
        g.GoodsPrice[Good.Food] = 25;      // initialFoodPrice ($4268), checked in the snapshot
        g.GoodsPrice[Good.Energy] = 25;    // initialEnergyPrice ($426A)
        g.GoodsPrice[Good.Mule] = 35;      // initialMulePrice ($4270): the fixed mule score value
        g.GoodsPrice[Good.Smithore] = 50;
        for (int p = 0; p < 4; p++)
        {
            g.GoodsPlayersNb[Good.Food * 4 + p] = 4;
            g.GoodsPlayersNb[Good.Energy * 4 + p] = 2;
            g.PlayersCash[p] = 1000;
        }
        g.GoodsStoreNb[Good.Food] = 8;
        g.GoodsStoreNb[Good.Energy] = 8;
        g.GoodsStoreNb[Good.Smithore] = 8;
        g.GoodsStoreNb[Good.Mule] = 14;
        g.FoodReq = 3;
        for (int x = 1; x < 5; x++) g.OutfitsPrice[x] = (byte)(25 * x);
        g.MulePrice = 100;
        RoundEventsProbInit.CopyTo(g.RoundEventsProb, 0);
        g.CursorWait = 3;             // levelsCursorWait[2]
        g.LevelAuctionTimeWait = 25;  // levelsAuctionTimeWait[2]
        for (int p = 0; p < 4; p++) g.PlayersTurnTimeWait[p] = 7;
        g.NbRounds = 12;

        for (int x = 3; x >= 0; x--)
        {
            var race = (Race)g.PlayersRace[x];
            if (race == Race.Humanoid) { g.PlayersCash[x] = 600; g.PlayersTurnTimeWait[x] -= 2; }
            else if (race == Race.Flapper) { g.PlayersCash[x] = 1600; g.PlayersTurnTimeWait[x] += 2; }
            if (g.IsCpu(x)) g.PlayersCash[x] = 1200;
        }
    }

    /// <summary>roundEventsProbInit ($7B7D): cards per round event, checked in the snapshot.</summary>
    public static readonly byte[] RoundEventsProbInit = { 3, 2, 3, 3, 3, 2, 2, 2 };
}
