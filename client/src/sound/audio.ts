// The game's sound in the page: the sound chip runs in an audio worklet, fed the registers from each
// frame message. Browsers only start audio after a click or a key, so it starts on the first one.
// M turns the sound off and on.

import workletUrl from "./worklet.ts?worker&url";

type Entry = { tick: number; regs: number[] };

export class Sound {
  private ctx: AudioContext | null = null;
  private node: AudioWorkletNode | null = null;
  private pending: Entry[] = [];
  private muted = false;

  constructor() {
    try {
      this.muted = localStorage.getItem("mule.muted") === "1";
    } catch {
      // no storage: sound on
    }
    const start = () => void this.start();
    addEventListener("pointerdown", start, { once: false });
    addEventListener("keydown", (e) => {
      if (e.key === "m" || e.key === "M") this.setMuted(!this.muted);
      start();
    });
  }

  get isMuted() {
    return this.muted;
  }

  setMuted(m: boolean) {
    this.muted = m;
    try {
      localStorage.setItem("mule.muted", m ? "1" : "0");
    } catch {
      // not kept
    }
    this.node?.port.postMessage({ type: "mute", muted: m });
  }

  /** The registers from a frame message: [tick, AUDF1, AUDC1 ... AUDCTL] each. */
  feed(pokey: number[][] | undefined, full: boolean) {
    if (!pokey?.length) return;
    const entries = pokey.map((p) => ({ tick: p[0], regs: p.slice(1) }));
    if (full) {
      this.post({ type: "reset" });
      this.pending = [];
    }
    if (!this.node) {
      this.pending.push(...entries);
      if (this.pending.length > 120) this.pending.splice(0, this.pending.length - 120);
      return;
    }
    this.post({ type: "regs", entries });
  }

  private post(m: unknown) {
    this.node?.port.postMessage(m);
  }

  private starting = false;

  private async start() {
    if (this.ctx || this.starting) {
      if (this.ctx?.state === "suspended") void this.ctx.resume();
      return;
    }
    this.starting = true;
    try {
      const ctx = new AudioContext();
      await ctx.audioWorklet.addModule(workletUrl);
      const node = new AudioWorkletNode(ctx, "pokey", { numberOfInputs: 0, outputChannelCount: [2] });
      node.connect(ctx.destination);
      this.ctx = ctx;
      this.node = node;
      node.port.postMessage({ type: "mute", muted: this.muted });
      // Only the newest registers matter from before the sound started.
      const last = this.pending.at(-1);
      if (last) node.port.postMessage({ type: "regs", entries: [last] });
      this.pending = [];
    } catch (e) {
      console.warn("no sound:", e);
    } finally {
      this.starting = false;
    }
  }
}
