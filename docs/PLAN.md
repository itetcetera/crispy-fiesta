# Online M.U.L.E. — build plan

Goal: the 1983 Atari M.U.L.E. tournament game, rule for rule and tick for tick, played online
in a browser, with a hand-drawn comic look. The C# server is authoritative: it runs every rule;
browsers only send joystick/button input and draw what the server says.

Rules source: "MULE Tournament Rules (verified from code)" doc, plus the disassembly
(`MULE - Disassembled Memory.zip`) for anything the doc does not cover (walking, town layout,
screen geometry, message text).

## Decisions (Marcus)

| Topic | Decision |
|---|---|
| Mode | Tournament only |
| Quirks | Every verified original quirk/bug is kept; unreachable ones are left out |
| Exception | A player worth exactly 0 is ranked normally (last) |
| Private-offer land sale | Included (human sellers only, as in the original) |
| Timing | NTSC, 60 ticks per second |
| CPU players | In scope (doc section 9); empty seats are CPUs |
| Joining | Invite link / room code now, no accounts; server designed so accounts and a lobby can be added later |
| Art | Marcus supplies it. The client uses plain placeholders and loads every image through one manifest |
| Repo | Local git repo `mule-online` in the project files |

## Stack

- **Server**: .NET 8, ASP.NET Core, SignalR (WebSockets).
- **Engine**: plain C# class library, no dependencies, deterministic given a random source.
- **Client**: TypeScript + Phaser 3 (a canvas 2D game engine) built with Vite.
  Phaser fits because the game is real-time sprite movement on a fixed screen, and it loads
  any art (PNG, SVG, sprite sheets) through one loader, so swapping placeholder art is a file drop.

## How "exact" is achieved

1. The engine is a structured port of the original 6502 routines: same order of steps, same
   integer arithmetic (8/16-bit wraps included), same random-number calls in the same order.
2. A small 6502 CPU emulator lives in the test project. Tests load the original memory snapshot,
   run an original routine and the C# port on the same inputs and random bytes, and require
   identical results. This is repeatable and runs with `dotnet test`.
3. The whole game advances in jiffies (1/60 s). Each player's input per jiffy is
   joystick direction + button, exactly what the original read.
4. Timed phases are iterators run once per frame after a port of the vertical blank interrupt
   (`Vbi`: jiffy clock, 4-jiffy timers, button latch, effect sound). Tests run the original phase
   on the scanline Atari 800 (`Mule.Atari`) with the same presses and compare every event frame by
   frame. Where the original's drawing code runs past a vertical blank inside a game loop, the port
   waits the same frame (measured, and checked by the tests).
5. One-off presentation pauses that depend on how long the 6502 takes to compute or draw (for
   example the CPUs' thinking before the land grant) are modelled from measurements on the
   original and match within a few frames. Gameplay timing (cursor speed, timers, who gets a
   plot for a given press) matches exactly.

## Layout

```
server/Mule.Engine          rules, game state, tick loop, CPU AI (no I/O)
server/Mule.Engine.Tests    unit tests + differential tests against the original code
server/Mule.Server          ASP.NET Core host: rooms, invite codes, SignalR hub, 60 Hz loop
client/                     Vite + TypeScript + Phaser 3
client/public/assets/       placeholder art + assets.json (the art manifest)
docs/ASSETS.md              every art asset the game needs, with sizes and notes
```

## Online model

- A player creates a room and gets a 6-character code and a link. Friends open the link, type a
  name, and take a seat. The host starts; empty seats become CPUs (CPUs take the lowest seats,
  as in the original).
- Identity goes through an `IPlayerIdentity` seam: today a guest name + random token; later an
  account. Rooms and seats only know the identity id, so accounts are an add-on, not a rewrite.
- The server runs each room at 60 ticks/s, applies the latest input of every seat each tick, and
  sends state snapshots to clients (~30 per second, compact). A dropped player's seat holds
  neutral input until they reconnect with their token.

## Milestones

1. **Engine core** — setup, map generation, production, energy shortage, round events, player
   events, prices, mule building, scoring/ranking, colony rating. Tested against the original.
2. **Round flow + timed phases** — land grant cursor, land auctions (incl. private offer), goods
   auctions (incl. collusion), the player turn (walking, town, store, pub, assay, wampus). Needs
   the walking/town geometry traced from the disassembly first.
3. **CPU players** — doc section 9.
4. **Server** — rooms, invite codes, SignalR hub, tick loop, reconnects.
5. **Client** — Phaser screens for every phase with placeholder art, keyboard/gamepad input,
   asset manifest and `docs/ASSETS.md`.
6. **End-to-end** — 4 browsers (or 1 human + 3 CPUs) play full 12-round games.
