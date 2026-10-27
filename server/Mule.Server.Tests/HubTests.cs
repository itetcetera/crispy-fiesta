using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Mule.Server.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Mule.Server.Tests;

/// <summary>Players in browsers, simulated: they talk to the real server through its hub.</summary>
public class HubTests(ITestOutputHelper output, HubTests.FastServer server) : IClassFixture<HubTests.FastServer>
{
    /// <summary>The server with the game clock running as fast as it can.</summary>
    public sealed class FastServer : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureServices(s => s.Configure<TickOptions>(o => { o.TicksPerSecond = 0; o.TicksPerFrame = 100; }));
    }

    sealed record Join(string Token, string PlayerId, Lobby Room);
    sealed record Lobby(string Code, string Phase, string HostId, List<MemberInfo> Members);
    sealed record MemberInfo(string Id, string Name, string Race, bool Connected, int Seat);
    sealed record Frame(long Tick, List<JsonElement> Events, Dictionary<string, int[]> State, bool Full);

    /// <summary>A browser: its connection and everything the server sent it.</summary>
    sealed class Player : IAsyncDisposable
    {
        public HubConnection Hub = null!;
        public readonly ConcurrentQueue<Lobby> Lobbies = new();
        public readonly ConcurrentQueue<Frame> Frames = new();
        public readonly TaskCompletionSource GameOver = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Lobby? Last => Lobbies.LastOrDefault();

        public static async Task<Player> Connect(FastServer server)
        {
            var p = new Player();
            p.Hub = new HubConnectionBuilder()
                .WithUrl(new Uri(server.Server.BaseAddress, "hub"), o => o.HttpMessageHandlerFactory = _ => server.Server.CreateHandler())
                .Build();
            p.Hub.On<Lobby>("lobby", l => p.Lobbies.Enqueue(l));
            p.Hub.On<Frame>("frame", f =>
            {
                p.Frames.Enqueue(f);
                if (f.Events.Any(e => e.GetProperty("t").GetString() == "GameOver")) p.GameOver.TrySetResult();
            });
            await p.Hub.StartAsync();
            return p;
        }

        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    readonly FastServer _server = server;

    static async Task Until(Func<bool> done, string what)
    {
        var end = DateTime.UtcNow.AddSeconds(30);
        while (!done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException(what);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Three_friends_play_a_whole_game_with_a_drop_and_a_rejoin()
    {
        await using var ann = await Player.Connect(_server);
        await using var bob = await Player.Connect(_server);
        var cat = await Player.Connect(_server);

        var a = await ann.Hub.InvokeAsync<Join>("CreateRoom", "Ann");
        string code = a.Room.Code;
        Assert.Equal(6, code.Length);
        var b = await bob.Hub.InvokeAsync<Join>("JoinRoom", code.ToLowerInvariant(), "Bob", null);
        var c = await cat.Hub.InvokeAsync<Join>("JoinRoom", code, "Cat", null);
        await bob.Hub.InvokeAsync("SetRace", "Flapper");
        await Until(() => ann.Last?.Members.Count == 3 && ann.Last.Members.Any(m => m.Race == "Flapper"), "lobby updates");
        Assert.Equal(a.PlayerId, ann.Last!.HostId);

        // Bob is not the host.
        await Assert.ThrowsAnyAsync<HubException>(() => bob.Hub.InvokeAsync("Start"));
        await Assert.ThrowsAnyAsync<HubException>(() => bob.Hub.InvokeAsync<Join>("JoinRoom", "ZZZZZZ", "Bob", null));

        await ann.Hub.InvokeAsync("Start");
        await Until(() => ann.Frames.Count > 0 && bob.Frames.Count > 0 && cat.Frames.Count > 0, "frames");
        var seats = ann.Last!.Members.ToDictionary(m => m.Name, m => m.Seat);
        Assert.Equal(new[] { 1, 2, 3 }, new[] { seats["ANN"], seats["BOB"], seats["CAT"] });

        // Cat drops mid-game and comes back with the token: same seat, and the whole screen again.
        await cat.DisposeAsync();
        await Until(() => ann.Last!.Members.Any(m => m.Name == "CAT" && !m.Connected), "drop seen");
        cat = await Player.Connect(_server);
        var c2 = await cat.Hub.InvokeAsync<Join>("JoinRoom", code, "Cat", c.Token);
        Assert.Equal(c.PlayerId, c2.PlayerId);
        Assert.Equal(3, c2.Room.Members.Single(m => m.Id == c.PlayerId).Seat);
        await Until(() => cat.Frames.Any(f => f.Full), "full frame");
        var first = cat.Frames.First(f => f.Full);
        Assert.Contains("PlotsPlayer", first.State.Keys);
        Assert.DoesNotContain("ExpsPlotsCapacity", first.State.Keys);

        // Everyone presses the button now and then (the score screens wait for it) until the end.
        var players = new[] { ann, bob, cat };
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var presses = Task.Run(async () =>
        {
            bool down = false;
            while (!stop.IsCancellationRequested && !players.All(p => p.GameOver.Task.IsCompleted))
            {
                down = !down;
                foreach (var p in players) await p.Hub.InvokeAsync("Input", down, 15);
                await Task.Delay(30);
            }
        });
        await Task.WhenAll(players.Select(p => p.GameOver.Task)).WaitAsync(stop.Token);
        await presses;
        var last = ann.Frames.Last();
        output.WriteLine($"game over at tick {last.Tick}; Ann got {ann.Frames.Count} frames, Cat {cat.Frames.Count}");
        await Until(() => ann.Last!.Phase == "Over", "room over");
        await cat.DisposeAsync();
    }
}
