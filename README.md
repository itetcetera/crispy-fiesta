# M.U.L.E. Online

The original M.U.L.E. (Atari 800, 1983), tournament rules, for up to four players in the browser.

- `server/`: the rules ported to C# (`Mule.Engine`), checked against the original
  (`Mule.Engine.Tests`), and the game server (`Mule.Server`, ASP.NET Core 8 + SignalR).
- `client/`: the browser game (TypeScript + Phaser 3).
- `docs/`: the plan, the protocol between them, and the art list.

## Run it with Docker

One image holds the browser client and the game server, on port 8080:

```sh
docker build -t mule-online .
docker run -p 8080:8080 mule-online
```

Rooms live in memory, so restarting the container ends the games in progress.

## Play it on your computer

You need the .NET 8 SDK and Node.js 20 or newer.

```sh
cd client
npm install
npm run build                      # writes client/dist, which the server serves
cd ..
dotnet run --project server/Mule.Server --urls http://0.0.0.0:5199
```

Open http://localhost:5199, type a name and press **New game**. Send the invite link it shows to the
other players (they need to reach your computer on port 5199), pick your species, and the host
presses **Start the game**. Empty seats are played by the computer.

Controls: arrow keys or WASD to move, Space or Enter for the button. A gamepad works too.

## Art

Every picture is listed in `client/public/assets/assets.json` and described in `docs/ASSETS.md`.
Replace a file with the same name and rebuild; no code changes.

## Tests

```sh
dotnet test server                 # the rules against the original, and the server
```
