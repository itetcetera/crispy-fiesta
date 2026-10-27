// The game screen: drawn again each frame from the model, at the original's positions.

import Phaser from "phaser";
import { portraitKey, queueAssets, walkerKey, type AssetEntry } from "../assets";
import {
  COLUMNS, HEIGHT, PLOT_H, PLOT_W, PLOTS, RIVER, STORE, STREET_BOTTOM, STREET_TOP, TOWN_BOTTOM, TOWN_TOP, WIDTH,
  atariColor, css, doorX, plotX, plotY, px, py, quarterY,
} from "../geometry";
import { SPECIES } from "../protocol";
import type { Model } from "../state";
import { GOOD_KEYS, GOODS, MONTHS, SCORE_KINDS } from "../texts";

const FONT = '"Courier New", Courier, monospace';
const ROUND_EVENT_ART: Record<string, string> = {
  PestAttack: "pest", Meteorite: "meteorite", Radiation: "radiation", AcidRain: "acid-cloud", Sunspots: "sunspot", FireInStore: "fire",
};

export interface SceneData {
  model: Model;
  assets: AssetEntry[];
  names: () => string[];  // by seat
  me: () => number;       // our seat, or -1
}

/** A pool of display objects, reused frame to frame: what is not drawn this frame is hidden. */
class Pool<T extends Phaser.GameObjects.GameObject & { setVisible(v: boolean): T }> {
  private items: T[] = [];
  private used = 0;
  constructor(private make: () => T) {}
  next(): T {
    if (this.used === this.items.length) this.items.push(this.make());
    const o = this.items[this.used++];
    o.setVisible(true);
    return o;
  }
  end() {
    for (let i = this.used; i < this.items.length; i++) this.items[i].setVisible(false);
    this.used = 0;
  }
}

export class GameScene extends Phaser.Scene {
  private d!: SceneData;
  private g!: Phaser.GameObjects.Graphics;
  private top!: Phaser.GameObjects.Graphics;
  private images!: Pool<Phaser.GameObjects.Image>;
  private texts!: Pool<Phaser.GameObjects.Text>;
  private sizes = new Map<string, AssetEntry>();

  constructor() {
    super("game");
  }

  init(d: SceneData) {
    this.d = d;
    for (const a of d.assets) this.sizes.set(a.key, a);
  }

  preload() {
    queueAssets(this.load, this.d.assets);
  }

  create() {
    this.g = this.add.graphics().setDepth(0);
    this.images = new Pool(() => this.add.image(0, 0, "plain").setDepth(1));
    this.top = this.add.graphics().setDepth(2);
    this.texts = new Pool(() => this.add.text(0, 0, "", { fontFamily: FONT, fontSize: "16px", color: "#fff" }).setDepth(3));
  }

  update() {
    this.g.clear();
    this.top.clear();
    const m = this.d.model;
    switch (m.screen) {
      case "wait": this.say("WAITING FOR THE GAME...", WIDTH / 2, HEIGHT / 2, 20, "#fff", 0.5); break;
      case "map": this.map(); break;
      case "town": this.town(); break;
      case "landAuction": this.auction(true); break;
      case "goodsAuction": this.auction(false); break;
      case "goodsStatus": this.goodsStatus(); break;
      case "score": this.score(); break;
      case "over": this.score(); this.say("GAME OVER", WIDTH / 2, 16, 24, "#ff0", 0.5); break;
    }
    this.footer();
    this.images.end();
    this.texts.end();
  }

  // --- helpers ---

  private v(name: string, i = 0) {
    return this.d.model.v(name, i);
  }

  private color(p: number) {
    return atariColor(this.v("PlayersColor", p) || [196, 132, 52, 84][p]);
  }

  private race(p: number) {
    return SPECIES[this.v("PlayersRace", p)] ?? "Humanoid";
  }

  private name(p: number) {
    if (this.v("PlayersAi", p) === 255) return "CPU";
    return this.d.names()[p] ?? `PLAYER ${p + 1}`;
  }

  /** A picture at (x, y) on the canvas, its top left corner unless an origin is given, at its listed size times scale. */
  private img(key: string, x: number, y: number, scale = 1, opts: { ox?: number; oy?: number; tint?: number; flip?: boolean; alpha?: number } = {}) {
    const size = this.sizes.get(key);
    if (!size) return;
    const o = this.images.next();
    o.setTexture(key).setOrigin(opts.ox ?? 0, opts.oy ?? 0).setPosition(x, y)
      .setDisplaySize(size.width * scale, size.height * scale).setFlipX(opts.flip ?? false).setAlpha(opts.alpha ?? 1);
    if (opts.tint !== undefined) o.setTint(opts.tint); else o.clearTint();
  }

  private say(text: string, x: number, y: number, size = 16, color = "#fff", ox = 0, oy = 0) {
    if (!text) return;
    const t = this.texts.next();
    t.setText(text).setPosition(x, y).setOrigin(ox, oy).setFontSize(size).setColor(color).setWordWrapWidth(null);
    return t;
  }

  private wrap(text: string, x: number, y: number, width: number, size = 16, color = "#fff") {
    const t = this.say(text, x, y, size, color, 0.5, 0);
    t?.setWordWrapWidth(width).setAlign("center");
  }

  /** The walker (and the mule he leads), facing the way he last moved. */
  private facing = new Map<string, { x: number; left: boolean }>();
  private faces(id: string, x: number) {
    const f = this.facing.get(id) ?? { x, left: false };
    if (x !== f.x) f.left = x < f.x;
    f.x = x;
    this.facing.set(id, f);
    return f.left;
  }

  // --- screens ---

  private map() {
    const m = this.d.model;
    this.g.fillStyle(0x000000).fillRect(0, 0, WIDTH, HEIGHT);
    for (let p = 0; p < PLOTS; p++) {
      const x = px(plotX(p)), y = py(plotY(p));
      this.img(p === STORE ? "store" : RIVER.includes(p) ? "river" : "plain", x, y);
    }
    // One mountain per band (MountainsX; 0 = none, as in the town).
    for (let k = 0; k < 20; k++) {
      const mx = this.v("MountainsX", k);
      if (mx) this.img("mountain", px(mx), py(quarterY(k)));
    }
    for (let p = 0; p < PLOTS; p++) {
      const owner = this.v("PlotsPlayer", p);
      const x = px(plotX(p)), y = py(plotY(p));
      if (owner < 4) {
        this.top.lineStyle(4, this.color(owner)).strokeRect(x + 3, y + 3, PLOT_W - 6, PLOT_H - 6);
        const exp = this.v("PlotsExp", p);
        if (exp >= 1 && exp <= 4) this.img(`icon-${GOOD_KEYS[exp - 1]}`, x + 8, y + 8);
      }
      const units = m.production.get(p);
      if (units !== undefined) this.say(String(units), x + PLOT_W - 10, y + PLOT_H - 8, 18, "#fff", 1, 1);
    }
    if (m.grantCursor >= 0) {
      const x = px(plotX(m.grantCursor)), y = py(plotY(m.grantCursor));
      this.top.lineStyle(5, 0xffffff).strokeRect(x, y, PLOT_W, PLOT_H);
    }
    if (m.ship) this.img(m.ship.pirates ? "pirate-ship" : "ship", px(m.ship.x), py(plotY(STORE)) + 8);
    const step = m.roundStep;
    if (step && ROUND_EVENT_ART[step.kind]) {
      const key = ROUND_EVENT_ART[step.kind];
      if (step.kind === "AcidRain") this.img(key, px(step.y || 56), py(plotY(step.x * COLUMNS)));
      else if (step.kind === "Sunspots") this.img(key, WIDTH - 110, 4);
      else if (step.kind === "FireInStore") this.img(key, px(plotX(STORE)) + 8, py(plotY(STORE)) + 8);
      else if (step.x) this.img(key, px(step.x), py(step.y || plotY(STORE)));
    }
    if (m.inTurn && this.v("turnMap") === 1) this.walker(1);
    const w = m.s["wampus"];
    if (w && w.length === 2) this.img("wampus", px(w[0]), py(w[1]));
    this.say(m.title, WIDTH / 2, 6, 22, "#fff", 0.5);
  }

  private walker(scale: number) {
    const cp = this.v("CurrentPlayer");
    if (cp > 3) return;
    const x = this.v("turnX"), y = this.v("turnY");
    const tint = this.color(cp);
    if (this.v("turnHasMule")) {
      const mx = this.v("turnMuleX"), my = this.v("turnMuleY");
      this.img("mule", px(mx) + 16 * scale, py(my) + 16 * scale, scale, { ox: 0.5, oy: 1, flip: this.faces("mule", mx) });
    }
    this.img(walkerKey(this.race(cp)), px(x) + 16 * scale, py(y) + 16 * scale, scale, { ox: 0.5, oy: 1, tint, flip: this.faces("walker", x) });
  }

  private town() {
    const m = this.d.model;
    this.img("town-ground", 0, 0);
    this.img("town-street", 0, py(STREET_TOP));
    for (const b of TOWN_TOP) {
      const c = px(doorX((b.from + b.to) / 2)) + 8;
      this.img(b.key, c, py(STREET_TOP), 1, { ox: 0.5, oy: 1 });
    }
    for (const b of TOWN_BOTTOM) {
      const c = px(doorX((b.from + b.to) / 2)) + 8;
      this.img(b.key, c, py(STREET_BOTTOM), 1, { ox: 0.5, oy: 0 });
    }
    if (m.inTurn) this.walker(2);
  }

  private auction(land: boolean) {
    const m = this.d.model, a = m.auction;
    this.g.fillStyle(0x101830).fillRect(0, 0, WIDTH, HEIGHT);
    const good = land ? "good-land" : `good-${GOOD_KEYS[a.good] ?? "food"}`;
    this.img(good, 16, 8);
    this.say(land ? `LAND AUCTION  PLOT ${a.plot >= 0 ? a.plot + 1 : ""}` : `${GOODS[a.good] ?? ""} AUCTION`, 76, 14, 20);
    // The bidding zone: buyers below the top line sell nothing; the store's prices at the edges.
    const topLine = py(0x54), startLine = py(0x9b);
    this.g.lineStyle(2, 0x8080ff).lineBetween(0, topLine, WIDTH, topLine).lineBetween(0, startLine, WIDTH, startLine);
    if (!land) {
      this.say(`STORE SELLS $${a.priceEnd}`, WIDTH - 10, topLine - 22, 14, "#ccc", 1);
      this.say(`STORE BUYS $${a.priceStart}`, WIDTH - 10, startLine + 4, 14, "#ccc", 1);
      if (a.storeSells) this.img("store-booth", WIDTH / 2, topLine - 4, 1, { ox: 0.5, oy: 1 });
    } else this.say(`START $${a.priceStart}`, WIDTH - 10, startLine + 4, 14, "#ccc", 1);
    for (let p = 0; p < 4; p++) {
      const x = 100 + 146 * p;
      const y = py(a.positions[p] ?? 0xa8);
      const selling = a.sellers[p] === 1;
      const lead = land ? a.buyer === p : a.buyer === p || a.seller === p;
      this.img(walkerKey(this.race(p)), x, y + 32, 1.6, { ox: 0.5, oy: 1, tint: this.color(p) });
      this.say(this.name(p), x, HEIGHT - 128, 14, css(this.color(p)), 0.5);
      this.say(selling ? "SELLER" : "BUYER", x, HEIGHT - 112, 12, "#aaa", 0.5);
      if (!land) this.say(`${this.v("GoodsPlayersNb", a.good * 4 + p)} UNITS`, x, HEIGHT - 98, 12, "#aaa", 0.5);
      const bid = a.bids[p] ?? 0;
      if (bid) this.say(`$${bid}`, x + 24, y - 8, 16, lead ? "#ff0" : "#fff");
    }
    if (a.traded > 0) this.top.lineStyle(3, 0xffff00).lineBetween(0, py(a.positions[a.buyer] ?? 0), WIDTH, py(a.positions[a.buyer] ?? 0));
    // The time bar.
    this.g.fillStyle(0x40c040).fillRect(16, HEIGHT - 84, Math.min(a.time, 255) * 2.4, 8);
  }

  private goodsStatus() {
    const m = this.d.model, s = m.goodsStatus;
    this.g.fillStyle(0x102010).fillRect(0, 0, WIDTH, HEIGHT);
    this.img(`good-${GOOD_KEYS[s.good] ?? "food"}`, 16, 8);
    this.say(`${GOODS[s.good] ?? ""} STATUS   ${MONTHS[this.v("NumRound")] ?? ""} MONTH`, 76, 14, 20);
    const base = 330;
    for (let p = 0; p < 4; p++) {
      const x = 90 + 146 * p;
      this.img(walkerKey(this.race(p)), x, base + 60, 1.4, { ox: 0.5, oy: 1, tint: this.color(p) });
      this.say(this.name(p), x, base + 64, 14, css(this.color(p)), 0.5);
      let i = 0;
      for (const [kind, bars] of s.bars) {
        const h = Math.min(bars.to[p] ?? 0, 250);
        const c = [0x60a0ff, 0xffc040, 0xff6060, 0x60ff60][kind % 4];
        this.g.fillStyle(c).fillRect(x - 40 + i * 16, base - h, 12, h);
        i++;
      }
      const req = s.required[p];
      if (req !== undefined) this.g.lineStyle(2, 0xffffff).lineBetween(x - 44, base - req, x + 44, base - req);
      this.say(`${this.v("GoodsPlayersNb", s.good * 4 + p)}`, x, base + 82, 14, "#fff", 0.5);
    }
    this.say(`STORE: ${this.v("GoodsStoreNb", s.good)}   PRICE $${this.v("GoodsPrice", s.good)}`, WIDTH / 2, 56, 16, "#ccc", 0.5);
  }

  private score() {
    const m = this.d.model, sc = m.score;
    this.g.fillStyle(0x201010).fillRect(0, 0, WIDTH, HEIGHT);
    if (!sc) return;
    this.say(sc.round ? `STATUS AFTER THE ${MONTHS[sc.round]} MONTH` : "THE COLONISTS", WIDTH / 2, 40, 20, "#fff", 0.5);
    const cols = [230, 330, 430, 540];
    SCORE_KINDS.forEach((k, i) => this.say(k, cols[i], 80, 14, "#aaa", 1));
    sc.order.forEach((p, row) => {
      if (p > 3) return;
      const y = 104 + row * 60;
      this.img(portraitKey(this.race(p)), 20, y, 0.55, { tint: this.color(p) });
      this.say(this.name(p), 80, y + 16, 16, css(this.color(p)));
      for (let k = 0; k < 4; k++) this.say(`$${sc.scores[k * 4 + p] ?? 0}`, cols[k], y + 16, 16, "#fff", 1);
    });
    this.say(`COLONY $${sc.colony}`, cols[3], 350, 18, "#ff0", 1);
  }

  private footer() {
    const m = this.d.model;
    if (m.screen === "wait") return;
    const y = 368;
    this.g.fillStyle(0x000000, 0.85).fillRect(0, y, WIDTH, HEIGHT - y);
    const lines = [m.eventText, m.turnText, m.message].filter((t) => t);
    let ty = y + 8;
    for (const l of lines) {
      this.wrap(l, WIDTH / 2, ty, WIDTH - 32, 16, "#fff");
      ty += l.length > 60 ? 40 : 22;
    }
    if (m.inTurn) {
      const cp = this.v("CurrentPlayer");
      this.g.fillStyle(this.color(cp)).fillRect(16, HEIGHT - 20, this.v("turnTime") * 4, 10);
    }
    // Each player's money, in their colour.
    for (let p = 0; p < 4; p++)
      this.say(`${this.name(p)} $${this.v("PlayersCash", p)}`, 16 + 156 * p, HEIGHT - 42, 13, css(this.color(p)));
  }
}

export const gameConfig = (parent: HTMLElement): Phaser.Types.Core.GameConfig => ({
  type: Phaser.AUTO,
  parent,
  width: WIDTH,
  height: HEIGHT,
  backgroundColor: "#000000",
  pixelArt: false,
  scale: { mode: Phaser.Scale.FIT, autoCenter: Phaser.Scale.CENTER_HORIZONTALLY },
  scene: [],
});
