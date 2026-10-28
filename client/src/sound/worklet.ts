// The sound chip in the audio thread: plays the register writes the server sends, each from its
// game frame (a 60th of a second apart), a few frames behind so a late message still plays in time.

import { Pokey } from "./pokey";

declare const sampleRate: number;
declare class AudioWorkletProcessor {
  readonly port: MessagePort;
}
declare function registerProcessor(name: string, ctor: unknown): void;

/** Frames the playback runs behind the newest message. */
const LATENCY = 6;

interface Entry {
  tick: number;
  regs: number[];
}

class PokeyProcessor extends AudioWorkletProcessor {
  private chip = new Pokey(sampleRate);
  private queue: Entry[] = [];
  private now = -1; // the game frame playing, fractional
  private readonly perSample = 60 / sampleRate;
  private muted = false;

  constructor() {
    super();
    this.port.onmessage = (e: MessageEvent) => {
      const m = e.data as { type: string; entries?: Entry[]; muted?: boolean };
      if (m.type === "mute") this.muted = !!m.muted;
      if (m.type === "reset") {
        this.queue = [];
        this.now = -1;
        this.chip.write(new Array(9).fill(0));
      }
      if (m.type !== "regs" || !m.entries?.length) return;
      for (const x of m.entries) this.queue.push(x);
      const last = m.entries[m.entries.length - 1].tick;
      // Start (or start again, after a stall or a jump) a few frames behind the newest.
      if (this.now < 0 || last - this.now > 60 || last < this.now - 60) this.now = last - LATENCY;
    };
  }

  process(_in: Float32Array[][], outputs: Float32Array[][]): boolean {
    const out = outputs[0][0];
    if (!out) return true;
    let i = 0;
    while (i < out.length) {
      // Apply every write that is due, then play up to the next one.
      while (this.queue.length && this.queue[0].tick <= this.now) this.chip.write(this.queue.shift()!.regs);
      let n = out.length - i;
      if (this.queue.length) {
        const until = Math.ceil((this.queue[0].tick - this.now) / this.perSample);
        n = Math.max(1, Math.min(n, until));
      }
      this.chip.render(out, i, i + n);
      if (this.now >= 0) this.now += n * this.perSample;
      i += n;
    }
    if (this.muted) out.fill(0);
    for (let c = 1; c < outputs[0].length; c++) outputs[0][c].set(out);
    return true;
  }
}

registerProcessor("pokey", PokeyProcessor);
