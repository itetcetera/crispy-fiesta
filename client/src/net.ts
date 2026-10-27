import * as signalR from "@microsoft/signalr";
import type { FrameMessage, JoinResult, LobbyView, Species } from "./protocol";

const TOKEN_KEY = "mule.token";
const NAME_KEY = "mule.name";

function load(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}
function save(key: string, value: string) {
  try { localStorage.setItem(key, value); } catch { /* private window: the token only lives this session */ }
}

export const savedName = () => load(NAME_KEY) ?? "";
export const hasToken = () => load(TOKEN_KEY) !== null;

/** The connection to the server's hub, and who we are in our room. */
export class Net {
  readonly hub: signalR.HubConnection;
  playerId = "";
  code = "";
  onLobby: (l: LobbyView) => void = () => {};
  onFrame: (f: FrameMessage) => void = () => {};
  onStatus: (s: string) => void = () => {};

  constructor(url = "/hub") {
    this.hub = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
      .build();
    this.hub.on("lobby", (l: LobbyView) => this.onLobby(l));
    this.hub.on("frame", (f: FrameMessage) => this.onFrame(f));
    this.hub.onreconnecting(() => this.onStatus("Reconnecting…"));
    // A new connection is a stranger to the server: join again with our token, same seat.
    this.hub.onreconnected(async () => {
      this.onStatus("");
      if (this.code) await this.join(this.code, savedName());
    });
    this.hub.onclose(() => this.onStatus("Disconnected. Reload the page to come back."));
  }

  async start() {
    if (this.hub.state === signalR.HubConnectionState.Disconnected) await this.hub.start();
  }

  private remember(r: JoinResult, name: string) {
    save(TOKEN_KEY, r.token);
    save(NAME_KEY, name);
    this.playerId = r.playerId;
    this.code = r.room.code;
    return r;
  }

  async create(name: string) {
    await this.start();
    return this.remember(await this.hub.invoke<JoinResult>("CreateRoom", name), name);
  }

  async join(code: string, name: string) {
    await this.start();
    return this.remember(await this.hub.invoke<JoinResult>("JoinRoom", code, name, load(TOKEN_KEY)), name);
  }

  setRace = (race: Species) => this.hub.invoke("SetRace", race);
  startGame = () => this.hub.invoke("Start");
  leave = () => this.hub.invoke("Leave");

  /** The controls changed: button and Atari stick bits. */
  input(button: boolean, stick: number) {
    if (this.hub.state === signalR.HubConnectionState.Connected)
      this.hub.send("Input", button, stick).catch(() => {});
  }
}

/** The server's message for a failed call, without SignalR's wrapping. */
export function errorText(e: unknown): string {
  const m = e instanceof Error ? e.message : String(e);
  const i = m.indexOf("HubException: ");
  return i >= 0 ? m.slice(i + 14) : m;
}
