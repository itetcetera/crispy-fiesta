using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Mule.Server.Hubs;
using Mule.Server.Rooms;

namespace Mule.Server.Hosting;

public sealed class TickOptions
{
    /// <summary>Game frames per second: 60, as on an NTSC Atari. 0 runs as fast as possible (tests).</summary>
    public int TicksPerSecond { get; set; } = 60;
    /// <summary>Frames per message to the browsers (2: about 30 a second).</summary>
    public int TicksPerFrame { get; set; } = 2;
    /// <summary>How long an empty or finished room is kept.</summary>
    public TimeSpan RoomIdle { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>Runs every room's game at 60 frames a second and sends the browsers what happened.</summary>
public sealed class RoomTicker(RoomRegistry rooms, IHubContext<GameHub> hub, IOptions<TickOptions> options, ILogger<RoomTicker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var o = options.Value;
        var clock = Stopwatch.StartNew();
        long ticks = 0;
        var lastSweep = clock.Elapsed;
        while (!stop.IsCancellationRequested)
        {
            if (o.TicksPerSecond > 0)
            {
                // Hold the pace of the clock; after a stall, catch up at most a few frames.
                var due = TimeSpan.FromSeconds((double)(ticks + 1) / o.TicksPerSecond);
                var wait = due - clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, stop).ContinueWith(_ => { });
                else if (-wait > TimeSpan.FromSeconds(0.1)) ticks = (long)(clock.Elapsed.TotalSeconds * o.TicksPerSecond);
            }
            else await Task.Yield();
            ticks++;
            foreach (var room in rooms.All)
            {
                bool running;
                try { running = room.Step(); }
                catch (Exception e)
                {
                    log.LogError(e, "Room {Code} failed", room.Code);
                    continue;
                }
                if (!running && room.Phase != RoomPhase.Over) continue;
                if (room.Tick % o.TicksPerFrame == 0 || room.Phase == RoomPhase.Over)
                {
                    var frame = room.TakeFrame();
                    if (frame != null) await hub.Clients.Group(room.Code).SendAsync("frame", frame, stop);
                    while (room.TakeWantFull(out var connection))
                        if (room.FullFrame() is { } full) await hub.Clients.Client(connection).SendAsync("frame", full, stop);
                    if (room.Phase == RoomPhase.Over && frame != null && frame.Events.Any(e => (string?)e["t"] == "GameOver"))
                        await hub.Clients.Group(room.Code).SendAsync("lobby", room.Lobby(), stop);
                }
            }
            if (clock.Elapsed - lastSweep > TimeSpan.FromMinutes(1))
            {
                lastSweep = clock.Elapsed;
                int n = rooms.Sweep(o.RoomIdle);
                if (n > 0) log.LogInformation("Removed {Count} idle rooms", n);
            }
        }
    }
}
