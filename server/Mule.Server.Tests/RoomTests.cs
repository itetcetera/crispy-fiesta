using System.Diagnostics;
using System.Text.Json;
using Mule.Engine.Core;
using Mule.Server.Identity;
using Mule.Server.Rooms;
using Mule.Server.Wire;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Server.Tests;

public class RoomTests(ITestOutputHelper output)
{
    static GuestIdentity Guest(string id) => new(id, id.ToUpperInvariant());

    [Fact]
    public void Humans_take_the_highest_seats_in_join_order()
    {
        var room = new Room("ABCDEF", new SystemRandomSource(1));
        room.Join(Guest("a"));
        room.Join(Guest("b"));
        room.SetRace("b", Race.Flapper);
        room.Start("a");
        var seats = room.Lobby().Members.ToDictionary(m => m.Id, m => m.Seat);
        Assert.Equal(2, seats["a"]);
        Assert.Equal(3, seats["b"]);
        var full = room.FullFrame()!;
        Assert.Equal(new[] { 0xFF, 0xFF, 10, 11 }, full.State["PlayersAi"]);       // CPUs, then joysticks 2 and 3
        Assert.Equal(new[] { 0, 0, (int)Race.Humanoid, (int)Race.Flapper }, full.State["PlayersRace"]);
        Assert.Equal(new[] { 196, 132, 52, 84 }.Select(c => (c & 0xF0) | 4), full.State["PlayersColor"].Select(c => (c & 0xF0) | 4));
    }

    [Fact]
    public void Only_the_host_starts_and_the_host_passes_on()
    {
        var room = new Room("ABCDEF");
        room.Join(Guest("a"));
        room.Join(Guest("b"));
        Assert.Throws<RoomException>(() => room.Start("b"));
        room.Leave("a", dropped: false);
        Assert.Equal("b", room.HostId);
        room.Start("b");
        Assert.Equal(RoomPhase.Playing, room.Phase);
        Assert.Throws<RoomException>(() => room.Join(Guest("c")));
    }

    [Fact]
    public void A_room_holds_four()
    {
        var room = new Room("ABCDEF");
        foreach (var id in new[] { "a", "b", "c", "d" }) room.Join(Guest(id));
        Assert.Throws<RoomException>(() => room.Join(Guest("e")));
    }

    [Fact]
    public void A_dropped_player_gets_the_same_seat_back()
    {
        var room = new Room("ABCDEF");
        room.Join(Guest("a"));
        room.Join(Guest("b"));
        room.Start("a");
        room.SetInput("b", true, 7);
        room.Leave("b", dropped: true);
        Assert.False(room.Lobby().Members.Single(m => m.Id == "b").Connected);
        var back = room.Join(Guest("b"));
        Assert.True(back.Connected);
        Assert.Equal(3, back.Seat);
    }

    [Fact]
    public void Hidden_values_are_not_sent()
    {
        foreach (var name in new[] { "ExpsPlotsCapacity", "PlotsKnownCrystite", "RoundEventsProb", "PlayersPlotsQuality", "GoodsQuality" })
            Assert.DoesNotContain(name, StateView.Names);
    }

    /// <summary>Two players; one drops at the start and never comes back. The score screens wait a
    /// minute for them each time, then go on, and the game ends.</summary>
    [Fact]
    public void A_dropped_player_holds_the_score_a_minute_then_the_game_goes_on()
    {
        var room = new Room("ABCDEF", new SystemRandomSource(3));
        room.Join(Guest("a"));
        room.Join(Guest("b"));
        room.Start("a");
        room.Leave("b", dropped: true);
        var recent = new Queue<string>();
        while (room.Step())
        {
            room.SetInput("a", room.Tick % 120 < 10, 15);
            if (room.TakeFrame() is { } f)
                foreach (var e in f.Events) { recent.Enqueue($"{room.Tick} {e["t"]}"); if (recent.Count > 12) recent.Dequeue(); }
            Assert.True(room.Tick < 600_000, "the game did not end: " + string.Join(", ", recent));
        }
        Assert.Equal(RoomPhase.Over, room.Phase);
    }

    /// <summary>A CPU-only room plays a whole game; every event goes out as JSON.</summary>
    [Fact]
    public void A_cpu_game_plays_to_the_end()
    {
        var room = new Room("ABCDEF", new SystemRandomSource(7));
        // One human (the host) who only presses the button now and then (the score waits for it);
        // CPUs take the other three seats.
        room.Join(Guest("a"));
        room.Start("a");
        var types = new HashSet<string>();
        int frames = 0;
        var clock = Stopwatch.StartNew();
        while (room.Step())
        {
            room.SetInput("a", room.Tick % 120 < 10, 15);
            if (room.Tick % 2 == 0 && room.TakeFrame() is { } f)
            {
                frames++;
                foreach (var e in f.Events) types.Add((string)e["t"]!);
                // Round trip through JSON as the hub sends it.
                JsonSerializer.Serialize(f, EventWire.Options);
            }
            Assert.True(room.Tick < 600_000, "the game did not end");
        }
        var last = room.TakeFrame();
        if (last != null) foreach (var e in last.Events) types.Add((string)e["t"]!);
        output.WriteLine($"{room.Tick} ticks ({room.Tick / 3600.0:F1} minutes of play) in {clock.Elapsed.TotalSeconds:F1} s, {frames} frames; events: {string.Join(", ", types.Order())}");
        Assert.Equal(RoomPhase.Over, room.Phase);
        Assert.Contains("GameOver", types);
        Assert.Contains("ScoreShown", types);
        Assert.Contains("GoodsTraded", types);
    }
}
