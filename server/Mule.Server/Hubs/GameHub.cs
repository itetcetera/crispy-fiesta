using Microsoft.AspNetCore.SignalR;
using Mule.Engine.Core;
using Mule.Server.Identity;
using Mule.Server.Rooms;

namespace Mule.Server.Hubs;

/// <summary>What a browser gets back on joining: who it is, the token to come back with, and the room.</summary>
public sealed record JoinResult(string Token, string PlayerId, LobbyView Room);

/// <summary>
/// The browsers' connection. Calls: CreateRoom, JoinRoom, SetRace, Start, Input, Leave. The server
/// sends "lobby" (a <see cref="LobbyView"/>) when the room changes and "frame" (a
/// <see cref="FrameMessage"/>) about 30 times a second during the game.
/// </summary>
public sealed class GameHub(RoomRegistry rooms, IIdentityService identities) : Hub
{
    const string RoomKey = "room", PlayerKey = "player";

    public async Task<JoinResult> CreateRoom(string name)
    {
        var room = rooms.Create();
        return await Enter(room, name, null);
    }

    /// <summary>Joins with the code. With the token from an earlier join it takes the same place back.</summary>
    public async Task<JoinResult> JoinRoom(string code, string name, string? token)
    {
        var room = rooms.Find(code) ?? throw new HubException("There is no room with that code.");
        return await Enter(room, name, token);
    }

    public async Task SetRace(string race)
    {
        if (!Enum.TryParse<Race>(race, true, out var r) || !Enum.IsDefined(r)) throw new HubException("Unknown species.");
        var (room, id) = Current();
        Run(() => room.SetRace(id, r));
        await Broadcast(room);
    }

    public async Task Start()
    {
        var (room, id) = Current();
        Run(() => room.Start(id));
        await Broadcast(room);
    }

    /// <summary>The player's controls: the button, and the stick as the Atari reads it (4 bits, a clear
    /// bit pushed: 1 up, 2 down, 4 left, 8 right; 15 = centred).</summary>
    public Task Input(bool button, int stick)
    {
        var (room, id) = Current();
        Run(() => room.SetInput(id, button, (byte)stick));
        return Task.CompletedTask;
    }

    public async Task Leave()
    {
        var (room, id) = Current();
        room.Leave(id, dropped: false);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, room.Code);
        Context.Items.Remove(RoomKey);
        await Broadcast(room);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(RoomKey, out var r) && r is Room room && Context.Items[PlayerKey] is string id)
        {
            room.Leave(id, dropped: true);
            await Broadcast(room);
        }
        await base.OnDisconnectedAsync(exception);
    }

    async Task<JoinResult> Enter(Room room, string name, string? token)
    {
        var who = identities.Resolve(token);
        if (who == null) (who, token) = identities.CreateGuest(name);
        Run(() => room.Join(who));
        Context.Items[RoomKey] = room;
        Context.Items[PlayerKey] = who.Id;
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
        await Broadcast(room);
        // A game under way: everything on screen, then the frames as they come.
        if (room.Phase != RoomPhase.Lobby) room.WantFull(Context.ConnectionId);
        return new JoinResult(token!, who.Id, room.Lobby());
    }

    (Room Room, string Id) Current() =>
        Context.Items.TryGetValue(RoomKey, out var r) && r is Room room && Context.Items[PlayerKey] is string id
            ? (room, id)
            : throw new HubException("Join a room first.");

    Task Broadcast(Room room) => Clients.Group(room.Code).SendAsync("lobby", room.Lobby());

    static void Run(Action a)
    {
        try { a(); }
        catch (RoomException e) { throw new HubException(e.Message); }
    }
}
