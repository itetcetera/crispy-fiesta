namespace Mule.Engine.Core;

/// <summary>Plot jobs as stored in plotsExp ($B02D). Goods use job - 1.</summary>
public static class Exp
{
    public const byte None = 0, Food = 1, Energy = 2, Smithore = 3, Crystite = 4;
}

/// <summary>Goods indexes used by goodsPlayersNb, goodsStoreNb and goodsPrice.</summary>
public static class Good
{
    public const int Food = 0, Energy = 1, Smithore = 2, Crystite = 3, Mule = 4;
}

/// <summary>Species codes as stored in playersRace ($091D).</summary>
public enum Race : byte
{
    Mechtron = 0, Gollumer = 1, Packer = 2, Bonzoid = 3, Spheroid = 4, Flapper = 5, Leggite = 6, Humanoid = 7,
}

/// <summary>
/// The whole game state. Field names and layouts follow the original RAM tables (address in each
/// comment) so routines port one to one and can be compared with the original code byte for byte.
/// </summary>
public sealed class GameState
{
    public const int Plots = 45, Players = 4, StorePlot = 22, Columns = 9, Rows = 5;
    public const byte Nobody = 0xFF;

    public static readonly int[] RiverPlots = { 4, 13, 31, 40 };

    public byte Level = 2;                    // $B0 (2 = tournament)
    public byte NumRound;                     // $B1
    public byte NbRounds = 12;                // $B36F
    public byte MaxIndexCpu = 0xFF;           // $D2
    public byte NbPaddles;                    // $BB

    public readonly byte[] PlayersAi = new byte[Players];       // $0900: $FF CPU, 0-7 paddle, 8+ joystick
    public readonly byte[] PlayersRace = new byte[Players];     // $091D
    public readonly byte[] PlayersColor = new byte[Players];    // $0905

    // Map tables
    public readonly byte[] PlotsPlayer = new byte[Plots];       // $B000 owner or $FF
    public readonly byte[] PlotsExp = new byte[Plots];          // $B02D mule job
    public readonly byte[] PlotsProd = new byte[Plots];         // $B05A production this round
    public readonly byte[] PlotsTemp = new byte[Plots];         // $B087 scratch (mountain units at setup)
    public readonly byte[] ExpsPlotsCapacity = new byte[Plots * 4]; // $B0B4 [job-1][plot] base output; crystite $FF = no mining
    public readonly byte[] PlotsTemp3 = new byte[Plots];        // $B42D scratch
    public readonly byte[] PlotsKnownCrystite = new byte[Plots];// $B45A CPU crystite knowledge (128 = unknown)
    public readonly byte[] MountainsX = new byte[20];           // $B221 x of each mountain quarter-row
    public readonly byte[] RiverStoreX = new byte[20];          // $B235 river/store x per quarter-row

    // Per player, per job / good
    public readonly byte[] GoodsShortage = new byte[37];        // $B19D
    public readonly byte[] GoodsPlayersNb = new byte[20];       // $B1C2 [good*4+player]
    public readonly byte[] ExpsPlayersNb = new byte[20];        // $B1D6 [(job-1)*4+player]
    public readonly byte[] GoodsPlayersProd = new byte[20];     // $B1EA [(job-1)*4+player]
    public readonly byte[] PlayersNbLands = new byte[Players];  // $B1FE
    public readonly byte[] PlayersNbExps = new byte[Players];   // $B202
    public readonly ushort[] PlayersCash = new ushort[Players]; // $B2BA

    // Store and prices
    public readonly byte[] GoodsStoreNb = new byte[5];          // $B212 food, energy, smithore, crystite, mules
    public readonly ushort[] GoodsPrice = new ushort[5];        // $B2F1 food, energy, smithore, crystite, mule score value
    public readonly ushort[] GoodsPriceLimit = { 30, 25, 35 };  // $3277 (smithore slot is rewritten: 50 then 20)
    public readonly byte[] GoodsFuturNb = new byte[3];          // $B332
    public readonly byte[] GoodsFuturReq = new byte[3];         // $B335
    public readonly byte[] PlayersGoodReq = new byte[Players];  // $CC
    public byte FoodReq = 3;                                    // $B376
    public ushort MulePrice = 100;                              // $B2B9/$B367
    public readonly byte[] OutfitsPrice = new byte[5];          // $B2B1 (index = job)

    // Round flow
    public readonly byte[] OrdersPlayer = { 0, 1, 2, 3 };       // $B328 ranking, 1st first
    public readonly byte[] RoundEventsProb = new byte[8];       // $B342 cards left per round event
    public readonly byte[] TurnEventsHappened = new byte[22];   // $B34A
    public readonly byte[] PlayersTurnTimeWait = new byte[Players]; // $B370
    public byte CursorWait = 3;                                 // $B36D
    public byte LevelAuctionTimeWait = 25;                      // $B327
    public byte DoMissingEnergyMalus;                           // $B377 (0 = apply)
    public byte AiMode;                                         // $B375 (0 none, 1 turn preview, 2 land auction)
    public byte ProdMax;                                        // $94 after calcPlotsProd

    // Scores ($B24B typesPlayersScore, $B32C totalScore, $B325 successScore)
    public const int ScoreMoney = 0, ScoreLand = 1, ScoreGoods = 2, ScoreTotal = 3;
    public readonly int[] TypesPlayersScore = new int[16];      // [type*4+player]
    public int TotalScore;                                      // colony total
    public byte SuccessScore;                                   // colony rating after round 12

    public int Score(int player, int type) => TypesPlayersScore[type * 4 + player];

    public bool IsCpu(int player) => (PlayersAi[player] & 0x80) != 0;

    public byte CrystiteAt(int plot) => ExpsPlotsCapacity[3 * Plots + plot];
    public byte CapacityAt(byte exp, int plot) => ExpsPlotsCapacity[(exp - 1) * Plots + plot];

    public static int Index(int row, int col) => row * Columns + col;
    public static (int row, int col) Coord(int plot) => (plot / Columns, plot % Columns);
}
