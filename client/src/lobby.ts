// The lobby: a name, a room (new, or joined by code or invite link), a species, and the host's start.

import { errorText, savedName, type Net } from "./net";
import { SPECIES, type LobbyView, type Species } from "./protocol";

function el<K extends keyof HTMLElementTagNameMap>(tag: K, props: Record<string, unknown> = {}, ...kids: (Node | string)[]): HTMLElementTagNameMap[K] {
  const e = Object.assign(document.createElement(tag), props);
  e.append(...kids);
  return e;
}

/** The room code in an invite link (/r/CODE), if this page is one. */
export function inviteCode(): string {
  const m = location.pathname.match(/^\/r\/([A-Za-z0-9]{6})\/?$/);
  return m ? m[1].toUpperCase() : "";
}

export class Lobby {
  private room: LobbyView | null = null;
  private error = "";
  private busy = false;

  constructor(private root: HTMLElement, private net: Net) {
    this.render();
  }

  show(room: LobbyView) {
    this.room = room;
    this.render();
  }

  private fail(e: unknown) {
    this.error = errorText(e);
    this.busy = false;
    this.render();
  }

  private async act(f: () => Promise<unknown>) {
    if (this.busy) return;
    this.busy = true;
    this.error = "";
    this.render();
    try {
      await f();
      this.busy = false;
      this.render();
    } catch (e) {
      this.fail(e);
    }
  }

  private render() {
    this.root.replaceChildren(el("h1", {}, "M.U.L.E."), this.room ? this.roomView(this.room) : this.joinView());
    if (this.error) this.root.append(el("p", { className: "error", role: "alert" }, this.error));
  }

  private joinView() {
    const name = el("input", { id: "name", maxLength: 12, value: savedName(), placeholder: "Your name" });
    const code = el("input", { id: "code", maxLength: 6, value: inviteCode(), placeholder: "Room code" });
    const create = el("button", { id: "create", disabled: this.busy }, "New game");
    const join = el("button", { id: "join", disabled: this.busy }, "Join");
    const named = () => {
      const n = name.value.trim();
      if (!n) throw new Error("Type your name first.");
      return n;
    };
    create.onclick = () => this.act(async () => this.show((await this.net.create(named())).room));
    join.onclick = () => this.act(async () => {
      if (code.value.trim().length !== 6) throw new Error("A room code has 6 letters.");
      this.show((await this.net.join(code.value.trim().toUpperCase(), named())).room);
    });
    code.onkeydown = (e) => { if (e.key === "Enter") join.click(); };
    return el("div", { className: "panel" },
      el("label", {}, "Name ", name),
      inviteCode() ? el("div", { className: "row" }, el("label", {}, "Room ", code), join)
        : el("div", { className: "row" }, create, el("span", {}, " or "), code, join));
  }

  private roomView(room: LobbyView) {
    const me = room.members.find((m) => m.id === this.net.playerId);
    const host = room.hostId === this.net.playerId;
    const link = `${location.origin}/r/${room.code}`;
    const copy = el("button", { id: "copy" }, "Copy link");
    copy.onclick = () => navigator.clipboard?.writeText(link).then(() => (copy.textContent = "Copied"), () => {});
    const list = el("ul", { id: "members" });
    for (const m of room.members) {
      const li = el("li", {}, el("b", {}, m.name), ` ${m.race}`, m.id === room.hostId ? " (host)" : "", m.connected ? "" : " (away)");
      list.append(li);
    }
    const cpus = 4 - room.members.length;
    if (cpus > 0) list.append(el("li", { className: "cpu" }, `${cpus} computer player${cpus > 1 ? "s" : ""} (Mechtron)`));

    const species = el("select", { id: "species", disabled: room.phase !== "Lobby" });
    for (const s of SPECIES) species.append(el("option", { value: s, selected: me?.race === s }, s));
    species.onchange = () => this.act(() => this.net.setRace(species.value as Species));

    const parts: Node[] = [
      el("p", {}, "Room ", el("b", { id: "room-code" }, room.code), " ", copy),
      el("p", { className: "link" }, link),
      list,
      el("label", {}, "Your species ", species),
    ];
    if (room.phase === "Lobby") {
      if (host) {
        const start = el("button", { id: "start", disabled: this.busy }, "Start the game");
        start.onclick = () => this.act(() => this.net.startGame());
        parts.push(el("p", {}, start));
      } else parts.push(el("p", {}, "Waiting for the host to start."));
    }
    return el("div", { className: "panel" }, ...parts);
  }
}
