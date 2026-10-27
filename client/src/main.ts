import Phaser from "phaser";
import { loadManifest } from "./assets";
import { watchControls } from "./input";
import { Lobby } from "./lobby";
import { hasToken, Net, savedName } from "./net";
import type { LobbyView } from "./protocol";
import { GameScene, gameConfig } from "./scenes/GameScene";
import { Model } from "./state";
import "./style.css";

const net = new Net();
const model = new Model();
const lobbyRoot = document.getElementById("lobby")!;
const gameRoot = document.getElementById("game")!;
const status = document.body.appendChild(Object.assign(document.createElement("div"), { id: "status" }));
const lobby = new Lobby(lobbyRoot, net);
let room: LobbyView | null = null;
let game: Phaser.Game | null = null;

// For the browser tests and for looking around in the console.
(window as unknown as { mule: unknown }).mule = { net, model, room: () => room };

const seatNames = () => {
  const names: string[] = [];
  for (const m of room?.members ?? []) if (m.seat >= 0) names[m.seat] = m.name;
  return names;
};
const mySeat = () => room?.members.find((m) => m.id === net.playerId)?.seat ?? -1;

async function startGame() {
  if (game) return;
  const assets = await loadManifest();
  game = new Phaser.Game(gameConfig(gameRoot));
  game.scene.add("game", GameScene, true, { model, assets, names: seatNames, me: mySeat });
}

net.onLobby = (l) => {
  room = l;
  lobby.show(l);
  const playing = l.phase !== "Lobby";
  lobbyRoot.hidden = playing;
  if (playing) void startGame();
};
net.onFrame = (f) => model.apply(f);
net.onStatus = (s) => (status.textContent = s);

watchControls((button, stick) => net.input(button, stick), () => room?.phase === "Playing");

// Coming back to an invite link we already joined (a reload): take our seat again.
const code = location.pathname.match(/^\/r\/([A-Za-z0-9]{6})/)?.[1];
if (code && hasToken() && savedName())
  net.join(code.toUpperCase(), savedName()).then((r) => net.onLobby(r.room)).catch(() => {});
