// What the screen shows: the server's visible state, merged frame by frame, and the screen the
// events have put up (the original drew each screen once; the events say which and when).

import type { FrameMessage, GameEvent } from "./protocol";
import { turnMessage } from "./texts";

export type ScreenKind = "wait" | "map" | "town" | "landAuction" | "goodsStatus" | "goodsAuction" | "score" | "over";

export interface Auction {
  plot: number;           // the plot for sale (land) or -1
  good: number;           // the good (goods auction) or -1
  priceStart: number;
  priceEnd: number;
  positions: number[];    // each player's height on the auction screen, in scanlines ($52 top to $AE bottom)
  bids: number[];         // the bid or ask shown above each player (0 = none)
  sellers: number[];      // 1 = selling
  storeSells: boolean;
  high: number;           // highest bid
  low: number;            // lowest ask
  buyer: number;
  seller: number;
  time: number;           // the auction's time bar
  traded: number;         // frames left to flash the last trade
  winner: number;         // the land auction's winner (255 none)
  units: number[];        // units each player has traded in this auction
  maxTime: number;        // the time bar's length when full
  ended: boolean;         // the land auction is over
}

export interface Score {
  round: number;
  order: number[];
  scores: number[];       // kind * 4 + player: money, land, goods, total
  colony: number;
  walk: { slot: number; x: number; y: number }[];
  firstFounder: number;
}

export interface RoundStep {
  kind: string;
  step: number;
  x: number;
  y: number;
}

const newAuction = (): Auction => ({
  plot: -1, good: -1, priceStart: 0, priceEnd: 0, positions: [0xa8, 0xa8, 0xa8, 0xa8], bids: [0, 0, 0, 0],
  sellers: [0, 0, 0, 0], storeSells: false, high: 0, low: 0, buyer: 255, seller: 255, time: 0, traded: 0, winner: 255,
  units: [0, 0, 0, 0], maxTime: 1, ended: false,
});

export class Model {
  /** The visible state values, by name (docs/PROTOCOL.md). */
  s: Record<string, number[]> = {};
  tick = 0;
  screen: ScreenKind = "wait";
  title = "";
  message = "";
  turnText = "";
  eventText = "";
  grantCursor = -1;
  production = new Map<number, number>();
  ship: { x: number; pirates: boolean; takeOff: boolean } | null = null;
  roundStep: RoundStep | null = null;
  auction = newAuction();
  goodsStatus = { good: -1, bars: new Map<number, { from: number[]; to: number[]; tick: number }>(), last: -1, required: [] as number[] };
  /** The score screen's line under the scrolling message ("PRESS ALL PLAYER BUTTONS TO GO ON."). */
  pressText = "";
  score: Score | null = null;
  over: { failed: boolean; rating: number } | null = null;
  turnPlayer = -1;
  /** Every event type seen, for the tests. */
  seen = new Set<string>();

  /** A single value (or one element of a table). */
  v(name: string, i = 0): number {
    return this.s[name]?.[i] ?? 0;
  }

  get inTurn() {
    return this.v("turn") === 1;
  }

  apply(f: FrameMessage) {
    if (f.full) this.s = {};
    for (const [k, v] of Object.entries(f.state)) this.s[k] = v;
    this.tick = f.tick;
    for (const e of f.events) this.event(e);
    if (this.auction.traded > 0) this.auction.traded--;
    if (this.auction.time > this.auction.maxTime) this.auction.maxTime = this.auction.time;
    // During a turn the walker's screen is the state's.
    if (this.inTurn && (this.screen === "map" || this.screen === "town"))
      this.screen = this.v("turnMap") === 1 ? "map" : "town";
    // Joining a game under way: show the map until an event says otherwise.
    if (this.screen === "wait" && f.full) this.screen = "map";
  }

  private event(e: GameEvent) {
    const n = (k: string) => Number(e[k] ?? 0);
    const a = (k: string) => (e[k] as number[] | undefined) ?? [];
    this.seen.add(e.t);
    switch (e.t) {
      case "Title":
        this.title = String(e.title ?? "");
        break;
      case "Message": {
        const text = String(e.text ?? "");
        if (this.screen === "score" && /PRESS ALL|OPTION/.test(text)) this.pressText = text.trim();
        else this.message = text;
        break;
      }
      case "MapShown":
        this.screen = "map";
        this.title = this.message = "";
        this.production.clear();
        this.ship = null;
        this.roundStep = null;
        this.grantCursor = -1;
        break;
      case "GrantCursor":
        this.grantCursor = n("plot");
        break;
      case "GrantCursorHidden":
        this.grantCursor = -1;
        break;
      case "AuctionStarted":
        this.screen = "landAuction";
        this.auction = { ...newAuction(), plot: n("plot"), priceStart: n("price") };
        this.message = "";
        break;
      case "AuctionPrice":
        this.auction.priceStart = n("price");
        break;
      case "AuctionState":
        Object.assign(this.auction, { positions: a("positions"), buyer: n("leader"), high: n("high"), time: n("time"), bids: a("bids") });
        break;
      case "AuctionEnded":
        Object.assign(this.auction, { winner: n("winner"), high: n("price"), ended: true });
        break;
      case "GoodsStatus":
        this.screen = "goodsStatus";
        this.goodsStatus = { good: n("good"), bars: new Map(), last: -1, required: [] };
        this.title = this.message = "";
        break;
      case "GoodsBars":
        this.goodsStatus.bars.set(n("kind"), { from: a("from"), to: a("to"), tick: this.tick });
        this.goodsStatus.last = n("kind");
        break;
      case "GoodsRequirement":
        this.goodsStatus.required = a("required");
        break;
      case "GoodsSelect":
        if (this.screen !== "goodsAuction") this.auction = newAuction();
        this.screen = "goodsAuction";
        Object.assign(this.auction, { good: n("good"), sellers: a("sellers") });
        break;
      case "GoodsAuctionStarted":
        this.screen = "goodsAuction";
        Object.assign(this.auction, {
          good: n("good"), priceStart: n("priceStart"), priceEnd: n("priceEnd"), sellers: a("sellers"), storeSells: Boolean(e.storeSells),
        });
        break;
      case "GoodsAuctionState":
        Object.assign(this.auction, {
          positions: a("positions"), buyer: n("buyer"), seller: n("seller"), high: n("high"), low: n("low"), time: n("time"), bids: a("bids"),
        });
        break;
      case "GoodsTraded": {
        this.auction.traded = 6;
        const u = [...this.auction.units];
        if (n("buyer") < 4) u[n("buyer")]++;
        if (n("seller") < 4) u[n("seller")]++;
        this.auction.units = u;
        break;
      }
      case "TurnStarted":
        this.turnPlayer = n("player");
        this.screen = "map";
        this.turnText = this.eventText = this.title = "";
        break;
      case "TurnEnded":
        this.screen = "map";
        this.turnText = this.eventText = "";
        break;
      case "TurnScreen":
        this.screen = e.map ? "map" : "town";
        this.message = "";
        break;
      case "TurnMessage":
        this.turnText = turnMessage(n("code"), String(e.text ?? ""));
        break;
      case "PlayerEventShown":
        this.eventText = String(e.text ?? "");
        break;
      case "ProductionShown":
        this.production.set(n("plot"), n("units"));
        break;
      case "Ship":
        this.ship = { x: n("x"), pirates: Boolean(e.pirates), takeOff: Boolean(e.takeOff) };
        break;
      case "RoundEventStep":
        this.roundStep = { kind: String(e.kind), step: n("step"), x: n("x"), y: n("y") };
        break;
      case "RoundStarted":
        this.roundStep = null;
        break;
      case "ScoreShown":
        this.screen = "score";
        this.title = this.message = "";
        this.pressText = "";
        this.score = { round: n("round"), order: a("order"), scores: a("scores"), colony: n("colony"), walk: [], firstFounder: -1 };
        break;
      case "ScoreWalk":
        if (this.score) {
          const w = this.score.walk.filter((x) => x.slot !== n("slot"));
          w.push({ slot: n("slot"), x: n("x"), y: n("y") });
          this.score.walk = w;
        }
        break;
      case "FirstFounder":
        if (this.score) this.score.firstFounder = n("player");
        break;
      case "GameOver":
        this.screen = "over";
        this.over = { failed: Boolean(e.colonyFailed), rating: n("rating") };
        break;
    }
  }
}
