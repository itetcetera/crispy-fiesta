# Browser ↔ server protocol

The server is `server/Mule.Server` (ASP.NET Core 8). Browsers talk to it over SignalR at `/hub`,
JSON protocol, camelCase names. Run it with `dotnet run --project server/Mule.Server`.

## Joining

| Call | Arguments | Returns |
|---|---|---|
| `CreateRoom` | `name` | `JoinResult` — the caller is the host |
| `JoinRoom` | `code`, `name`, `token` (or null) | `JoinResult` |
| `SetRace` | `race`: `Mechtron`, `Gollumer`, `Packer`, `Bonzoid`, `Spheroid`, `Flapper`, `Leggite`, `Humanoid` | — |
| `Start` | — (host only) | — |
| `Input` | `button` (bool), `stick` (0-15, Atari bits: a clear bit is pushed; 1 up, 2 down, 4 left, 8 right; 15 centred) | — |
| `Leave` | — | — |

`JoinResult` is `{ token, playerId, room }`. Keep `token` (for example in `localStorage`): calling
`JoinRoom` with it again, after a reload or a dropped connection, gives back the same place and
seat. Codes are 6 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` and are not case-sensitive.
An invite link is `/r/<code>`; the server serves the client's `index.html` for it.

Errors (bad code, room full, game started, not the host) come back as a hub error with a message
that can be shown as it is.

Send `Input` whenever the controls change; the server keeps the last one until the next.

## Rooms

Up to 4 players. Humans take the highest seats in the order they joined and CPUs the lowest
(as in the original); CPUs are Mechtrons. Seat colours are fixed: 196, 132, 52, 84 (Atari colour
bytes: hue in the high nibble). A dropped player's seat holds no buttons until they come back;
the game does not wait for them, except at the "press all buttons" screens (the score after each round), which wait up to a minute of game time for a dropped player, then go on as if they had pressed. The start of a dropped player's own turn waits the same minute; the turn then runs out with no input.

## Server → browser

`lobby` — `{ code, phase: Lobby | Playing | Over, hostId, members: [{ id, name, race, connected, seat }] }`,
sent whenever anything in the room changes.

`frame` — `{ tick, events, state, full, pokey }`, about 30 a second while a game runs (one per 2
game frames; the game runs at 60 frames a second).

- `events`: what happened, in order, each an object with `t` (the type, below) and its fields.
- `state`: the visible values that changed since the last frame, each an array of numbers (a
  single value is a one-element array). With `full` true (sent right after joining a game under
  way) it holds every value.
- `pokey`: the sound, as the original's sound chip (POKEY) registers, one entry for each game frame
  they changed in: `[tick, AUDF1, AUDC1, AUDF2, AUDC2, AUDF3, AUDC3, AUDF4, AUDC4, AUDCTL]`. Play
  each entry from its tick, a 60th of a second apart, and hold the registers until the next. A full
  frame holds one entry, the registers now.

### State values

From the game state (names and layouts as in `GameState.cs`, which follows the original's RAM):
`Level`, `NumRound`, `NbRounds`, `PlayersAi` (255 = CPU), `PlayersRace`, `PlayersColor`,
`PlotsPlayer` (45 plots, 255 = nobody), `PlotsExp` (0 none, 1 food, 2 energy, 3 smithore,
4 crystite), `PlotsProd`, `MountainsX`, `RiverStoreX`, `MapScreen` (the map's 400 characters),
`GoodsShortage`, `GoodsPlayersNb` (good × 4 + player), `ExpsPlayersNb`, `GoodsPlayersProd`,
`PlayersNbLands`, `PlayersNbExps`, `PlayersCash`, `GoodsStoreNb`, `GoodsPrice`, `PlayersGoodReq`,
`PlayersGoodHeld`, `PlayersGoodGap`, `PlayersGoodShort`, `FoodReq`, `MulePrice`, `OutfitsPrice`,
`CurrentPlayer`, `LandsToSell`, `NbLandsToSellByPlayers`, `OrdersPlayer`, `TypesPlayersScore`
(type × 4 + player: money, land, goods, total), `TotalScore`, `SuccessScore`.

During a player's turn (`turn` = [1]): `turnX`, `turnY`, `turnMuleX`, `turnMuleY`, `turnMap`
(0 town, 1 map), `turnHasMule`, `turnOutfit`, `turnBuilding`, `turnTime`, `turnMarked`,
`turnRunning`, and `wampus` ([x, y] while it shows, else empty).

`sound`: the sound effect playing (255 none).

Hidden on purpose, never sent: the crystite underground and the plots' base outputs, the event
deck, the CPUs' estimates, scratch tables.

### Event types

`Title`, `Message`, `MapShown`, `Sound`, `GrantCursor`, `GrantCursorHidden`, `PlotGranted`,
`AuctionStarted`, `AuctionPrice`, `AuctionState`, `AuctionEnded`, `GoodsStatus`, `GoodsBars`,
`GoodsRequirement`, `GoodsSelect`, `GoodsAuctionStarted`, `GoodsAuctionState`, `GoodsTraded`,
`GoodsAuctionEnded`, `TurnStarted`, `TurnEnded`, `PlayerEventShown`, `TurnMessage`, `TurnScreen`,
`MuleBought`, `MuleSoldBack`, `MuleOutfitted`, `MuleRanAway`, `PlotChanged`, `Assay`, `Gambling`,
`CpuAction`, `Wampus`, `WampusCaught`, `ProductionShown`, `RoundStarted`, `RoundEventShown`,
`RoundEventStep`, `Ship`, `ScoreShown`, `ScoreWalk`, `FirstFounder`, `ColonyFailed`, `GameOver`.
Their fields are the record fields in `server/Mule.Engine` (search `: GameEvent`).
