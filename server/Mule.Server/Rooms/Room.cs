using System.Text.Json.Nodes;
using Mule.Engine.Core;
using Mule.Engine.Flow;
using Mule.Engine.Rules;
using Mule.Server.Identity;
using Mule.Server.Wire;

namespace Mule.Server.Rooms;

public enum RoomPhase { Lobby, Playing, Over }

/// <summary>A player in a room, in the order they joined.</summary>
public sealed class Member
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public Race Race { get; set; } = Race.Humanoid;
    public bool Connected { get; set; } = true;
    /// <summary>The game frame the player dropped at.</summary>
    public long DroppedAt { get; set; }
    /// <summary>The seat in the game (0-3), or -1 before it starts.</summary>
    public int Seat { get; set; } = -1;
}

/// <summary>What everyone in the room sees before (and around) the game.</summary>
public sealed record LobbyView(string Code, RoomPhase Phase, string HostId, IReadOnlyList<MemberView> Members);
public sealed record MemberView(string Id, string Name, string Race, bool Connected, int Seat);

/// <summary>One tick's worth (or a few) of game for the browsers: the events in order and the visible
/// values that changed.</summary>
public sealed record FrameMessage(long Tick, IReadOnlyList<JsonObject> Events, IReadOnlyDictionary<string, int[]> State, bool Full);

public sealed class RoomException(string message) : Exception(message);

/// <summary>
/// A room: up to four players join with the code, pick a species, and the host starts. Humans take
/// the highest seats and CPUs the lowest, as in the original. All methods lock the room, so the
/// hub and the ticker can call them from any thread.
/// </summary>
public sealed class Room
{
    public const int MaxPlayers = GameState.Players;

    /// <summary>A dropped player's seat counts as pressed at the "press all buttons" waits after a
    /// minute of game time (Marcus, 2026-10-27).</summary>
    public const int AwayAfter = 60 * 60;

    // playersColor ($0905) as the snapshot holds it: each seat's colour is fixed.
    static readonly byte[] SeatColors = { 196, 132, 52, 84 };

    readonly object _sync = new();
    readonly List<Member> _members = new();
    readonly PlayerInput[] _inputs = new PlayerInput[MaxPlayers];
    readonly List<JsonObject> _pending = new();
    readonly IRandomSource _rng;
    readonly System.Collections.Concurrent.ConcurrentQueue<string> _wantFull = new();
    Game? _game;
    StateView _view = new();
    int _seen;

    public string Code { get; }
    public RoomPhase Phase { get; private set; }
    public string HostId { get; private set; } = "";
    public long Tick { get; private set; }
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;
    public ScoreScreen.Outcome Score { get; } = new();

    public Room(string code, IRandomSource? rng = null)
    {
        Code = code;
        _rng = rng ?? new SystemRandomSource();
        for (int i = 0; i < MaxPlayers; i++) _inputs[i] = new PlayerInput(false);
    }

    public bool AnyoneConnected { get { lock (_sync) return _members.Any(m => m.Connected); } }

    /// <summary>Takes a place in the room, or takes it back after a drop.</summary>
    public Member Join(IPlayerIdentity who)
    {
        lock (_sync)
        {
            LastActivity = DateTime.UtcNow;
            var m = _members.FirstOrDefault(x => x.Id == who.Id);
            if (m != null) { m.Connected = true; return m; }
            if (Phase != RoomPhase.Lobby) throw new RoomException("The game has already started.");
            if (_members.Count >= MaxPlayers) throw new RoomException("The room is full.");
            m = new Member { Id = who.Id, Name = who.Name };
            _members.Add(m);
            if (_members.Count == 1) HostId = who.Id;
            return m;
        }
    }

    /// <summary>Leaves for good (in the lobby) or drops out (in a game: the seat waits, holding no input).</summary>
    public void Leave(string id, bool dropped)
    {
        lock (_sync)
        {
            LastActivity = DateTime.UtcNow;
            var m = _members.FirstOrDefault(x => x.Id == id);
            if (m == null) return;
            if (Phase == RoomPhase.Lobby && !dropped)
            {
                _members.Remove(m);
                if (HostId == id) HostId = _members.FirstOrDefault()?.Id ?? "";
                return;
            }
            m.Connected = false;
            m.DroppedAt = Tick;
            if (m.Seat >= 0) _inputs[m.Seat] = new PlayerInput(false);
            if (Phase == RoomPhase.Lobby && HostId == id)
                HostId = _members.FirstOrDefault(x => x.Connected)?.Id ?? HostId;
        }
    }

    public void SetRace(string id, Race race)
    {
        lock (_sync)
        {
            if (Phase != RoomPhase.Lobby) throw new RoomException("The game has already started.");
            Get(id).Race = race;
        }
    }

    /// <summary>The host starts: humans in the highest seats in the order they joined, CPUs below.</summary>
    public void Start(string id)
    {
        lock (_sync)
        {
            if (Phase != RoomPhase.Lobby) throw new RoomException("The game has already started.");
            if (id != HostId) throw new RoomException("Only the host can start the game.");
            int humans = _members.Count;
            int cpus = MaxPlayers - humans;
            for (int i = 0; i < humans; i++) _members[i].Seat = cpus + i;
            var seats = Enumerable.Range(0, MaxPlayers).Select(s => s < cpus
                ? new SeatSetup(true, Race.Mechtron, SeatColors[s])
                : new SeatSetup(false, _members[s - cpus].Race, SeatColors[s])).ToList();
            var state = Setup.NewGame(_rng, seats);
            _game = new Game(state, _rng);
            _view = new StateView();
            _game.StartNow(GameLoop.Play(_game, Score));
            Phase = RoomPhase.Playing;
            Collect();
        }
    }

    /// <summary>The latest controls of a player: they hold until the next change.</summary>
    public void SetInput(string id, bool button, byte stick)
    {
        lock (_sync)
        {
            var m = Get(id);
            if (m.Seat >= 0) _inputs[m.Seat] = new PlayerInput(button, (byte)(stick & 0x0F));
        }
    }

    /// <summary>Runs one frame of the game (a 60th of a second). True while the game goes on.</summary>
    public bool Step()
    {
        lock (_sync)
        {
            if (Phase != RoomPhase.Playing || _game == null) return false;
            foreach (var m in _members)
                if (m.Seat >= 0) _game.Vbi.Away[m.Seat] = !m.Connected && Tick - m.DroppedAt >= AwayAfter;
            _game.RunFrame(_inputs);
            Tick++;
            Collect();
            if (_game.Finished)
            {
                Phase = RoomPhase.Over;
                LastActivity = DateTime.UtcNow;
            }
            return true;
        }
    }

    /// <summary>The events since the last frame and what changed, or null with nothing to send.</summary>
    public FrameMessage? TakeFrame()
    {
        lock (_sync)
        {
            if (_game == null) return null;
            var changes = _view.Changes(_game);
            if (_pending.Count == 0 && changes.Count == 0) return null;
            var msg = new FrameMessage(Tick, _pending.ToArray(), changes, false);
            _pending.Clear();
            return msg;
        }
    }

    /// <summary>Everything visible now, for a player who (re)joins a game under way.</summary>
    public FrameMessage? FullFrame()
    {
        lock (_sync)
            return _game == null ? null : new FrameMessage(Tick, Array.Empty<JsonObject>(), StateView.Capture(_game), true);
    }

    /// <summary>A connection that joined a game under way: the ticker sends it a full frame right after
    /// the next frame, from the same loop, so nothing newer can reach it first.</summary>
    public void WantFull(string connectionId) => _wantFull.Enqueue(connectionId);

    public bool TakeWantFull(out string connectionId) => _wantFull.TryDequeue(out connectionId!);

    public LobbyView Lobby()
    {
        lock (_sync)
            return new LobbyView(Code, Phase, HostId,
                _members.Select(m => new MemberView(m.Id, m.Name, m.Race.ToString(), m.Connected, m.Seat)).ToList());
    }

    Member Get(string id) => _members.FirstOrDefault(x => x.Id == id) ?? throw new RoomException("You are not in this room.");

    void Collect()
    {
        var events = _game!.Events;
        for (; _seen < events.Count; _seen++) _pending.Add(EventWire.ToJson(events[_seen]));
        // The engine only adds to its list: empty it now and then.
        if (events.Count > 4096) { events.Clear(); _seen = 0; }
    }
}
