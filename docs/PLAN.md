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
| Screen saver | Left out (2026-10-27): the original freezes the game after ~9 minutes without a button press ($5C98, TV burn-in guard); online games never freeze |
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
6. Four timing effects of the original are left out because they depend on the exact 6502 cycle
   at which a vertical blank lands, not on the rules:
   - The "press all buttons" wait (`loc_30A2`) can miss a timer tick when the blank falls
     between its timer check and its timer reset, so the original sometimes waits one more
     4-jiffy timer period before an auction. The port always takes the first tick.
   - In an auction, the main loop's drawing now and then ends just after a blank, so the
     original finishes an auction step one jiffy late. Steps (prices, heights, leader, time
     bar) are the same; the land auction tests allow this one-jiffy lag and count it (about
     0.3% of steps).
   - The buy/sell choice screen's drawing (text, numbers, the CPUs' decisions) takes a
     different number of scanlines each time, so its time bar starts 2 to 4 jiffies after the
     status screen ends. The port always takes 2; the select tests allow up to 3 jiffies, and the
     trading tests ask (`Game.Race`, "select late") when the original started it.
   - In goods trading, the main loop polls the movement tick, the sticks and the trade timer in
     turn, and its drawing (prices, unit amounts) sometimes runs past one more blank. So when
     two of these fall due on the same jiffy, the order the original handles them in depends on
     the cycle. The port takes the usual order (`Game.Race` names each point: "tick first",
     "slow tick", "slow unit", "quit late", "unit first"); the goods tests answer those questions with what the
     original did, so every other rule is checked exactly. The rare order comes up about 2-3
     times per auction, and shifts the trading by one jiffy.
   - In the development turns, the money and message drawing now and then runs past a blank
     ("title quick", "player late", "map late", "message first"); the turn tests answer these.
   - A CPU player's turn (playerTurnAi) works out each mule over several frames: calcGoodsQuality
     simulates two rounds of prices, then every plot is rated. The port can't count the 6502's
     cycles, so it waits an estimate (`ThinkFrames`: 2 + 0.4 frames per plot owned, right in about
     half of 49 measured cases and otherwise one frame off); the turn tests ask the original
     (`Game.Think`). Around these the drawing can also run past a blank: before the time cost
     shows ("time late"), at the screen sync before the cursor (sub_5E1B, "sync late") and before
     the pub tune ("gamble late"). The CPU's mule choices, assays and money are exact either way;
     only when things happen shifts, by a frame or a few.
7. Map collisions. On the map, mountains and the river slow the walker, and touching the river
   or store near the middle takes him into town, only where the sprites' pixels overlap, as the
   GTIA latches them. The port keeps what decides them as state: the mountain and river sprite
   rows made with the map (`SprMount`, `SprRiver`), the map's characters (`MapScreen`, the plot
   frames) and the walker's sprite memory, and computes the overlaps scanline by scanline
   (`MapCollisions`). The turn tests compare the result with the emulator's collision registers
   every frame. The walker's frames come from each player's buffer ($0E00, $09C0 in town), which
   the disk loads with the species. Open points: the snapshot holds the frames of only the
   Mechtron and the Bonzoid (the other six fall back to the Mechtron's until the disk image
   provides them), and the
   round-event text printed over the map is not modelled yet.

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
4. **Server** — rooms, invite codes, SignalR hub, tick loop, reconnects. Done 2026-10-27: see
   `docs/PROTOCOL.md`; tests in `server/Mule.Server.Tests` (a whole game through the real hub).
5. **Client** — Phaser screens for every phase with placeholder art, keyboard/gamepad input,
   asset manifest and `docs/ASSETS.md`.
6. **End-to-end** — 4 browsers (or 1 human + 3 CPUs) play full 12-round games.
