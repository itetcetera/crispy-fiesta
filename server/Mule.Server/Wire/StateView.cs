using System.Reflection;
using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;

namespace Mule.Server.Wire;

/// <summary>
/// What a browser may see of a game: the parts of the state the original shows on screen. Hidden
/// things (the crystite underground, the plots' base outputs, the event deck, the CPUs' own
/// estimates, scratch tables) never leave the server.
/// </summary>
public sealed class StateView
{
    // GameState fields sent to clients, by name.
    static readonly string[] Visible =
    {
        nameof(GameState.Level), nameof(GameState.NumRound), nameof(GameState.NbRounds), nameof(GameState.PlayersAi),
        nameof(GameState.PlayersRace), nameof(GameState.PlayersColor), nameof(GameState.PlotsPlayer), nameof(GameState.PlotsExp),
        nameof(GameState.PlotsProd), nameof(GameState.MountainsX), nameof(GameState.RiverStoreX), nameof(GameState.MapScreen),
        nameof(GameState.GoodsShortage), nameof(GameState.GoodsPlayersNb), nameof(GameState.ExpsPlayersNb),
        nameof(GameState.GoodsPlayersProd), nameof(GameState.PlayersNbLands), nameof(GameState.PlayersNbExps),
        nameof(GameState.PlayersCash), nameof(GameState.GoodsStoreNb), nameof(GameState.GoodsPrice),
        nameof(GameState.PlayersGoodReq), nameof(GameState.PlayersGoodHeld), nameof(GameState.PlayersGoodGap),
        nameof(GameState.PlayersGoodShort), nameof(GameState.FoodReq), nameof(GameState.MulePrice), nameof(GameState.OutfitsPrice),
        nameof(GameState.CurrentPlayer), nameof(GameState.LandsToSell), nameof(GameState.NbLandsToSellByPlayers),
        nameof(GameState.OrdersPlayer), nameof(GameState.TypesPlayersScore), nameof(GameState.TotalScore), nameof(GameState.SuccessScore),
    };

    // The walking player's turn, as the screen shows it.
    static readonly string[] TurnVisible =
    {
        nameof(Turn.X), nameof(Turn.Y), nameof(Turn.MuleX), nameof(Turn.MuleY), nameof(Turn.Map), nameof(Turn.HasMule),
        nameof(Turn.Outfit), nameof(Turn.Building), nameof(Turn.Time), nameof(Turn.Marked), nameof(Turn.Running),
    };

    static readonly FieldInfo[] StateFields = Visible.Select(n => typeof(GameState).GetField(n)
        ?? throw new InvalidOperationException($"GameState.{n} is missing")).ToArray();
    static readonly FieldInfo[] TurnFields = TurnVisible.Select(n => typeof(Turn).GetField(n)
        ?? throw new InvalidOperationException($"Turn.{n} is missing")).ToArray();

    /// <summary>Names a client can expect in a view (for docs and tests).</summary>
    public static IEnumerable<string> Names =>
        Visible.Concat(TurnVisible.Select(n => "turn" + n)).Concat(new[] { "turn", "wampus", "sound" });

    readonly Dictionary<string, int[]> _last = new();

    /// <summary>Every visible value now.</summary>
    public static Dictionary<string, int[]> Capture(Game game)
    {
        var d = new Dictionary<string, int[]>();
        foreach (var f in StateFields) d[f.Name] = Values(f.GetValue(game.State));
        var t = game.Turn;
        d["turn"] = new[] { t == null ? 0 : 1 };
        if (t != null)
        {
            foreach (var f in TurnFields) d["turn" + f.Name] = Values(f.GetValue(t));
            // The wampus is only there while it shows.
            d["wampus"] = t.WampusVisible != 0 ? new int[] { t.WampusX, t.WampusY } : Array.Empty<int>();
        }
        d["sound"] = new[] { (int)game.Vbi.Sound };
        return d;
    }

    /// <summary>The values that changed since the last call (all of them the first time).</summary>
    public Dictionary<string, int[]> Changes(Game game)
    {
        var now = Capture(game);
        var changed = new Dictionary<string, int[]>();
        foreach (var (k, v) in now)
            if (!_last.TryGetValue(k, out var old) || !old.AsSpan().SequenceEqual(v)) changed[k] = v;
        foreach (var (k, v) in changed) _last[k] = v;
        return changed;
    }

    static int[] Values(object? v) => v switch
    {
        byte b => new int[] { b },
        ushort u => new int[] { u },
        int i => new[] { i },
        bool b => new[] { b ? 1 : 0 },
        byte[] a => a.Select(x => (int)x).ToArray(),
        ushort[] a => a.Select(x => (int)x).ToArray(),
        int[] a => (int[])a.Clone(),
        _ => throw new NotSupportedException(v?.GetType().Name),
    };
}
