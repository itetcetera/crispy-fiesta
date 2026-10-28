// The game screen: drawn again each frame from the model, at the original's positions.

import Phaser from "phaser";
import { queueAssets, walkerKey, type AssetEntry } from "../assets";
import {
  COLUMNS, HEIGHT, PLOT_H, PLOT_W, PLOTS, RIVER, STORE, TOWN_TOP, WIDTH,
  atariColor, townY, plotX, plotY, px, py, quarterY,
} from "../geometry";
import { SPECIES } from "../protocol";
import type { Model } from "../state";
import { CELL, type Cell, cellX, cellY, centre, hiresInk, rightAt, wrapLines } from "../grid";
import { GOOD_KEYS, GOODS, SCORE_KINDS } from "../texts";

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
  private letters!: Pool<Phaser.GameObjects.BitmapText>;
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
    // The lettering: the font picture's 64 characters in the Atari's order (docs/ASSETS.md, "font").
    const font = this.textures.get("font").getSourceImage() as { width: number };
    const cw = font.width / 16;
    this.cache.bitmapFont.add("atari", Phaser.GameObjects.RetroFont.Parse(this, {
      image: "font", width: cw, height: cw, chars: Phaser.GameObjects.RetroFont.TEXT_SET1.slice(0, 64), charsPerRow: 16,
      "spacing.x": 0, "spacing.y": 0, "offset.x": 0, "offset.y": 0, lineSpacing: 0,
    }));
    this.textures.get("font").setFilter(Phaser.Textures.FilterMode.NEAREST);
    this.letters = new Pool(() => this.add.bitmapText(0, 0, "atari", "").setDepth(3));
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
    this.letters.end();
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

  /** Text on the original's grid: a row of <cell> size from scanline <line>, from column <col>, in an
   * Atari colour. */
  private at(text: string, cell: Cell, col: number, line: number, color: number) {
    if (!text) return;
    const size = CELL[cell];
    const font = this.textures.get("font").getSourceImage() as { width: number };
    const cw = font.width / 16;
    const o = this.letters.next();
    o.setText(text.toUpperCase()).setPosition(cellX(cell, col), cellY(line)).setScale(size.w / cw, size.h / cw).setTint(atariColor(color));
  }

  /** A row of text centred on its grid. */
  private atCentre(text: string, cell: Cell, line: number, color: number) {
    this.at(text, cell, centre(text, cell === "n" ? 40 : 20), line, color);
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
    this.g.fillStyle(atariColor(0x08)).fillRect(0, 0, WIDTH, HEIGHT);
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
        // The factory's sign in the plot's right half, rows 2 and 3, in the owner's colour.
        if (exp >= 1 && exp <= 4) this.img(`icon-${GOOD_KEYS[exp - 1]}`, x + 32, y + 16, 1, { tint: this.color(owner) });
      }
      const units = m.production.get(p);
      if (units !== undefined) this.say(String(units), x + PLOT_W - 10, y + PLOT_H - 8, 18, "#fff", 1, 1);
    }
    if (m.grantCursor >= 0) {
      const x = px(plotX(m.grantCursor)), y = py(plotY(m.grantCursor));
      this.top.lineStyle(5, 0xffffff).strokeRect(x, y, PLOT_W, PLOT_H);
    }
    // The ships fly along the store's row, their left edge at player 0.
    if (m.ship) this.img(m.ship.pirates ? "pirate-ship" : "ship", px(m.ship.x), py(99));
    const step = m.roundStep;
    if (step && ROUND_EVENT_ART[step.kind]) {
      const key = ROUND_EVENT_ART[step.kind];
      if (step.kind === "AcidRain") this.img(key, px(step.x), py(plotY(step.step * COLUMNS)));
      else if (step.kind === "Sunspots") {
        // A dot flashes up somewhere on the map each frame, in a random colour.
        const h = Math.imul(m.tick, 2654435761) >>> 0;
        this.img(key, 32 + (h % 576), 40 + ((h >>> 10) % 320), 1, { tint: atariColor((h >>> 20) & 0xff) });
      }
      else if (step.kind === "FireInStore") this.img(key, px(plotX(STORE)), py(plotY(STORE)) - 4);
      else if (step.x) this.img(key, px(step.x), py(step.y || plotY(STORE)));
    }
    if (m.inTurn && this.v("turnMap") === 1) this.walker(1, py);
    const w = m.s["wampus"];
    if (w && w.length === 2) this.img("wampus", px(w[0]), py(w[1]));
    // printTitle ($4A0B) writes from column 12; the titles carry their own leading spaces.
    this.at(m.title, "n", 12, 32, hiresInk(0x04, 0x08));
  }

  private walker(scale: number, toY: (y: number) => number) {
    const cp = this.v("CurrentPlayer");
    if (cp > 3) return;
    const x = this.v("turnX"), y = this.v("turnY");
    const tint = this.color(cp);
    if (this.v("turnHasMule")) {
      const mx = this.v("turnMuleX"), my = this.v("turnMuleY");
      const town = toY !== py;
      this.img(town ? "mule-town" : "mule", px(mx) + 16, toY(my) + (town ? 32 : 16), 1, { ox: 0.5, oy: 1, tint, flip: this.faces("mule", mx) });
    }
    this.img(walkerKey(this.race(cp)), px(x) + 16 * scale, toY(y) + 16 * scale, scale, { ox: 0.5, oy: 1, tint, flip: this.faces("walker", x) });
  }

  /** The town ($2000, mode 4 rows): a black yard from column 8 to 31, the four outfitters along the top
   * (columns 9, 15, 21, 27), the assay office, land office, pub and the corral along the
   * bottom, the mule price, and the money line and messages under it. */
  private town() {
    const m = this.d.model;
    this.g.fillStyle(atariColor(0x08)).fillRect(0, 0, WIDTH, HEIGHT);
    this.g.fillStyle(0x000000).fillRect(cellX("n", 8), py(44), cellX("n", 24), py(194) - py(44));
    const box = (key: string, col: number, cols: number, from: number, to: number) => {
      const size = this.sizes.get(key);
      if (!size) return;
      const o = this.images.next();
      o.setTexture(key).setOrigin(0, 0).setPosition(cellX("n", col), py(from))
        .setDisplaySize(cellX("n", cols), py(to) - py(from)).setFlipX(false).setAlpha(1).clearTint();
    };
    box("town-ground", 8, 24, 44, 194);
    TOWN_TOP.forEach((b) => box(b.key, b.from, 4, 52, 84));
    box("bldg-assay", 9, 4, 116, 172);
    box("bldg-land", 14, 4, 116, 164);
    box("bldg-pub", 19, 4, 116, 156);
    box("bldg-corral", 25, 6, 116, 196);
    this.g.fillStyle(atariColor(0x0c)).fillRect(cellX("n", 8), py(180), cellX("n", 24), 2);
    const ink = hiresInk(0x04, 0x08);
    this.at(m.title, "n", 12, 32, ink);
    const price = `$${this.v("MulePrice")}`;
    this.at(price, "n", 26, 180, ink);
    this.at("MULES", "n", 25, 188, ink);
    if (m.inTurn) {
      const cp = this.v("CurrentPlayer");
      if (cp < 4) this.at(`YOUR MONEY EQUALS $${this.v("PlayersCash", cp)}`, "n", 8, 198, ink);
      this.walker(1, townY);
    }
  }


  // The auction screens (land and goods) and the goods status screen share the original's layout:
  // two title rows, the players' columns (sprite positions $57 + $18 each), the store's prices at the
  // sides, money and units rows, and a message row (the display list at $B500).
  private static readonly COL_END = [11, 17, 23, 29]; // the last column of each player's numbers
  private static readonly BK = 0x08;                 // the auction screens' background (COLBK)

  private auctionTop(title: string, second: string, good: number, iconColour: boolean) {
    this.g.fillStyle(atariColor(GameScene.BK)).fillRect(0, 0, WIDTH, HEIGHT);
    this.atCentre(title, "w", 31, 0x32);
    this.atCentre(second, "w", 39, 0x72);
    // The good's sign in both top corners (the land auction has none).
    if (good >= 0 && good < 4 && iconColour)
      for (const col of [1, 18]) this.img(`good-${GOOD_KEYS[good]}`, cellX("w", col), cellY(31));
  }

  /** The auction screens' message: one 20-column row at line 219, or two from 211 when longer. */
  private messageRow(text: string, colour: number) {
    const lines = wrapLines(text, 20).slice(0, 2);
    lines.forEach((l, i) => this.atCentre(l, "w", 219 - 8 * (lines.length - 1) + 8 * i, colour));
  }

  /** The two rows under the players: money, and units (held, or traded in an auction). */
  private auctionRows(units: number[] | null, rightLabel: string, unitsLabel: string) {
    const ink = hiresInk(0x02, 0x08), light = hiresInk(0x0c, 0x08);
    this.at("MONEY", "n", 2, 191, ink);
    this.at("MONEY", "n", 32, 191, ink);
    for (let p = 0; p < 4; p++) {
      const cash = String(this.v("PlayersCash", p));
      this.at(cash, "n", rightAt(cash, GameScene.COL_END[p]), 191, ink);
      if (units) {
        const u = String(units[p] ?? 0);
        this.at(u, "n", rightAt(u, GameScene.COL_END[p]), 199, light);
      }
    }
    if (units) {
      this.at(unitsLabel, "n", 2, 199, light);
      this.at(rightLabel, "n", 32, 199, light);
    }
  }

  /** A player's walker in its auction column: the sprite is drawn from 20 scanlines above its height
   * ($964A). */
  private auctionWalker(p: number, line: number) {
    const x = px(0x57 + 0x18 * p) + 16;
    this.img(walkerKey(this.race(p)), x, py(line - 20), 1, { ox: 0.5, oy: 0, tint: this.color(p) });
  }

  private auction(land: boolean) {
    const m = this.d.model, a = m.auction;
    const round = this.v("NumRound");
    if (land) {
      const second = a.ended ? (a.winner < 4 ? "PLOT IS SOLD!" : "PLOT DIDN'T SELL!")
        : a.buyer < 4 && a.high ? `HIGH BID IS $${a.high}` : "NO BIDS TENDERED";
      this.auctionTop(`LAND AUCTION #${round}`, second, 5, false);
    }
    else this.auctionTop(`AUCTION #${round}`, GOODS[a.good] ?? "", a.good, true);
    const ink = hiresInk(0x02, 0x08);
    // The store's prices: it sells at the top of the scale and buys at the bottom.
    // The land office's house sits in the middle of the land auction, on the starting price's line.
    if (land) this.img("store-booth", 288, py(136));
    if (!land && a.priceEnd) {
      if (a.storeSells) {
        this.at(String(a.priceEnd), "n", 32, 59, ink);
        this.img("store-booth", cellX("n", 32), py(76));
      }
      this.at(String(a.priceStart), "n", 4, 163, ink);
      this.img("store-booth", cellX("n", 4), py(146));
    }
    // The bidding lines: the best buyer's and the best seller's, dotted across the columns.
    const dots = (line: number, colour: number) => {
      this.top.fillStyle(atariColor(colour));
      for (let x = cellX("n", 9); x < cellX("n", 31); x += 8) this.top.fillRect(x, py(line), 4, 2);
    };
    if (a.buyer < 4) dots(a.positions[a.buyer] ?? 0x9b, 0x02);
    if (!land && a.seller < 4) dots(a.positions[a.seller] ?? 0x54, 0x02);
    // The time bar (column 37), shrinking from 40 scanlines.
    const h = Math.round((Math.min(a.time, a.maxTime) / Math.max(1, a.maxTime)) * 40);
    if (h > 0) this.g.fillStyle(atariColor(0x02)).fillRect(cellX("n", 37) + 4, py(155 - h), 8, h * 2);
    for (let p = 0; p < 4; p++) {
      const selling = a.sellers[p] === 1;
      if (selling) this.at("SELL", "n", 8 + 6 * p, 51, ink);
      else this.at("BUY", "n", 9 + 6 * p, 179, ink);
      const line = a.positions[p] ?? 0xa8;
      this.auctionWalker(p, line);
      const bid = a.bids[p] ?? 0;
      if (bid) this.at(String(bid), "n", 12 + 6 * p, line, a.buyer === p || a.seller === p ? 0x1e : ink);
    }
    if (a.traded > 0 && a.buyer < 4) dots(a.positions[a.buyer] ?? 0, 0x1e);
    this.auctionRows(land ? null : a.units, "TRADED", "UNITS");
    if (land) this.messageRow(m.message.trim() || `BIDS START AT $${a.priceStart}`, 0xc4);
    else if (!a.priceEnd) this.messageRow("PUSH STICK TO DECLARE AS BUYER OR SELLER", 0xc4);
    else this.messageRow(m.message.trim(), 0xc4);
  }

  private goodsStatus() {
    const m = this.d.model, s = m.goodsStatus;
    const good = s.good;
    this.auctionTop(`STATUS #${this.v("NumRound")}`, GOODS[good] ?? "", good, true);
    const ink = hiresInk(0x02, 0x08);
    // The store's prices.
    const buys = this.v("GoodsPrice", good);
    if (buys) {
      this.at(String(buys + 35 * (good === 3 ? 4 : 1)), "n", 32, 51, ink);
      this.img("store-booth", cellX("n", 32), py(76));
      this.at(String(buys), "n", 4, 163, ink);
      this.img("store-booth", cellX("n", 4), py(146));
    }
    // Each player's bar: a scanline per quarter unit, up to 96, from line 155; the newest bars move a
    // quarter unit a frame from where they were.
    const bars = s.bars.get(s.last);
    for (let p = 0; p < 4; p++) {
      let h = this.v("GoodsPlayersNb", good * 4 + p) * 4;
      if (bars) {
        const from = bars.from[p] ?? 0, to = bars.to[p] ?? 0;
        const steps = Math.floor((m.tick - bars.tick) / 2);
        h = from + Math.sign(to - from) * Math.min(Math.abs(to - from), steps);
      }
      h = Math.max(0, Math.min(96, h));
      const x = px(0x57 + 0x18 * p) + 8;
      if (h > 0) this.g.fillStyle(this.color(p)).fillRect(x, py(155 - h), 20, h * 2);
      const req = s.required[p];
      if (req !== undefined) this.top.lineStyle(2, atariColor(0x0e)).lineBetween(x - 8, py(155 - Math.min(96, req)), x + 28, py(155 - Math.min(96, req)));
    }
    this.auctionRows(Array.from({ length: 4 }, (_, p) => this.v("GoodsPlayersNb", good * 4 + p)), "UNITS", "UNITS");
    const what = m.message.trim() || ["PREVIOUS AMOUNT", "USAGE", "SPOILAGE", "PRODUCTION"][Math.max(0, s.last)] || "";
    this.messageRow(what, 0x94);
  }

  /** The status summary ($6D7A): four rows per player in rank order, in the player's colour, the
   * colony's total, the scrolling message and the button line. */
  private score() {
    const m = this.d.model, sc = m.score;
    this.g.fillStyle(0x000000).fillRect(0, 0, WIDTH, HEIGHT);
    if (!sc) return;
    if (sc.walk.length && !m.message) {
      // The players walk in to their places first (the screen is still empty).
      for (const w of sc.walk) {
        const p = sc.order[w.slot];
        if (p !== undefined && p < 4) this.img(walkerKey(this.race(p)), px(w.x) + 16, py(w.y) + 36, 1, { ox: 0.5, oy: 1, tint: this.color(p) });
      }
      return;
    }
    this.at(sc.round ? `STATUS SUMMARY #${sc.round}` : "THE COLONISTS", "w", 1, 32, 0x26);
    sc.order.forEach((p, rank) => {
      if (p > 3) return;
      const colour = this.v("PlayersColor", p) || [196, 132, 52, 84][p];
      SCORE_KINDS.forEach((k, i) => {
        const line = 44 + 36 * rank + 8 * i;
        this.at(k, "w", 3, line, colour);
        const n = String(sc.scores[i * 4 + p] ?? 0);
        this.at(n, "w", rightAt(n, 17), line, colour);
      });
    });
    this.at("COLONY", "w", 2, 188, 0x26);
    const colony = String(sc.colony);
    this.at(colony, "w", rightAt(colony, 17), 188, 0x26);
    // The message scrolls across the tall row.
    const msg = m.message.trim();
    if (msg) {
      const text = "   " + msg + "   ";
      const offset = Math.floor(m.tick / 8) % (text.length + 20);
      this.at(text.slice(Math.max(0, offset - 20), offset), "b", Math.max(0, 20 - offset), 200, 0x08);
    }
    if (m.pressText) this.at(m.pressText, "n", centre(m.pressText, 40), 216, hiresInk(0x08, 0x00));
  }

  /** The map's and the town's message rows: two 20-column rows at the bottom (lines 216 and 224), or
   * three 40-column rows when the text is longer. */
  private footer() {
    const m = this.d.model;
    if (m.screen !== "map" && m.screen !== "town") return;
    // The turn's start prompt is a 40-column row at line 224 on the map ($B6CC).
    const start = m.screen === "map" && /TO START|TAKING HIS TURN/.test(m.message);
    if (start) this.at(m.message, "n", 4, 224, 0xf4);
    const text = [m.eventText, m.turnText, start ? "" : m.message].filter((t) => t.trim()).join("  ");
    const top = m.screen === "town" ? 208 : 216;
    if (text) {
      const wide = wrapLines(text, 20);
      if (wide.length <= 2) wide.forEach((l, i) => this.atCentre(l, "w", top + 8 * i, 0xf4));
      else wrapLines(text, 40).slice(0, 3).forEach((l, i) => this.atCentre(l, "n", top + 8 * i, 0xf4));
    }
    // The turn's time: a bar at the right edge, shrinking down to line 194.
    if (m.inTurn) {
      const cp = this.v("CurrentPlayer");
      const h = Math.min(96, this.v("turnTime"));
      this.g.fillStyle(this.color(cp)).fillRect(cellX("n", 38), py(194 - h), 8, h * 2);
    }
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
