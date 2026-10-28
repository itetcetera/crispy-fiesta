// The Atari's sound chip (POKEY), enough of it to play what the game writes: four voices, each a
// divider of a 64 kHz or 15 kHz clock (or of the 1.79 MHz machine clock), its output a square wave or
// a noise taken from the chip's polynomial counters, at one of 16 volumes. The game sets the
// registers once a frame; the server sends them (docs/PROTOCOL.md, `pokey`).

/** NTSC machine clock, in cycles a second. */
export const CLOCK = 1789790;

// The polynomial counters run off the machine clock, one step a cycle; a voice samples one when its
// divider runs out.
function poly(bits: number, tap: number): Uint8Array {
  const n = (1 << bits) - 1;
  const t = new Uint8Array(n);
  let x = 1;
  for (let i = 0; i < n; i++) {
    t[i] = x & 1;
    const b = ((x >> 0) ^ (x >> (bits - tap))) & 1;
    x = (x >> 1) | (b << (bits - 1));
  }
  return t;
}
const POLY4 = poly(4, 3);
const POLY5 = poly(5, 3);
const POLY9 = poly(9, 5);
const POLY17 = poly(17, 14);

/** Register order, as the game writes them and the server sends them. */
export const AUDF = (v: number) => 2 * v;
export const AUDC = (v: number) => 2 * v + 1;
export const AUDCTL = 8;

export class Pokey {
  readonly regs = new Uint8Array(9);
  private cycle = 0; // machine cycles played so far (a float: cycles per sample are not whole)
  private next = [0, 0, 0, 0]; // the cycle each voice's divider next runs out
  private out = [0, 0, 0, 0]; // each voice's output bit
  private hp = [0, 0]; // high-pass latches of voices 1 and 2
  private dc = 0; // the output's running average, taken off so a held volume does not click
  readonly cyclesPerSample: number;

  constructor(readonly sampleRate: number, readonly gain = 0.5) {
    this.cyclesPerSample = CLOCK / sampleRate;
  }

  /** New register values: a voice whose period changes starts its new one from now. */
  write(regs: ArrayLike<number>) {
    const old = this.regs.slice();
    for (let i = 0; i < 9; i++) this.regs[i] = regs[i] & 0xff;
    for (let v = 0; v < 4; v++) {
      const changed = old[AUDF(v)] !== this.regs[AUDF(v)] || old[AUDCTL] !== this.regs[AUDCTL];
      if (changed || this.next[v] < this.cycle) this.next[v] = this.cycle + this.period(v);
    }
  }

  /** A voice's divider period, in machine cycles. */
  period(v: number): number {
    const ctl = this.regs[AUDCTL];
    const base = ctl & 1 ? 114 : 28;
    const f = (i: number) => this.regs[AUDF(i)];
    const fast = (v === 0 && ctl & 0x40) || (v === 2 && ctl & 0x20);
    // Voices 2 and 4 can be joined with 1 and 3 into a 16-bit divider.
    if ((v === 1 && ctl & 0x10) || (v === 3 && ctl & 0x08)) {
      const lowFast = (v === 1 && ctl & 0x40) || (v === 3 && ctl & 0x20);
      const n = f(v) * 256 + f(v - 1);
      return lowFast ? n + 7 : (n + 1) * base;
    }
    return fast ? f(v) + 4 : (f(v) + 1) * base;
  }

  /** Runs voice <v>'s divider out once, at machine cycle <c>. */
  private tick(v: number, c: number) {
    const ac = this.regs[AUDC(v)];
    const at = Math.floor(c);
    if ((ac & 0x80) === 0 && !POLY5[at % 31]) return; // the 5-bit counter lets only some through
    if (ac & 0x20) this.out[v] ^= 1;
    else if (ac & 0x40) this.out[v] = POLY4[at % 15];
    else this.out[v] = this.regs[AUDCTL] & 0x80 ? POLY9[at % 511] : POLY17[at % 131071];
    // Voice 3 clocks voice 1's high-pass latch, voice 4 voice 2's.
    if (v === 2 && this.regs[AUDCTL] & 0x04) this.hp[0] = this.out[0];
    if (v === 3 && this.regs[AUDCTL] & 0x02) this.hp[1] = this.out[1];
  }

  private level(v: number): number {
    const ac = this.regs[AUDC(v)];
    const vol = ac & 0x0f;
    if (vol === 0) return 0;
    if (ac & 0x10) return vol; // volume only: the level itself
    // A voice joined to the next one is its divider's low half, silent.
    const ctl = this.regs[AUDCTL];
    if ((v === 0 && ctl & 0x10) || (v === 2 && ctl & 0x08)) return 0;
    let bit = this.out[v];
    if (v === 0 && ctl & 0x04) bit ^= this.hp[0];
    if (v === 1 && ctl & 0x02) bit ^= this.hp[1];
    return bit ? vol : 0;
  }

  /** Fills <out> with samples (-1..1), each the average level over its stretch of cycles. */
  render(out: Float32Array, from = 0, to = out.length) {
    const periods = [0, 1, 2, 3].map((v) => this.period(v));
    for (let i = from; i < to; i++) {
      const start = this.cycle;
      const end = start + this.cyclesPerSample;
      let sum = 0;
      for (let v = 0; v < 4; v++) {
        let t = start;
        let acc = 0;
        while (this.next[v] < end) {
          const c = Math.max(this.next[v], t);
          acc += this.level(v) * (c - t);
          this.tick(v, c);
          t = c;
          this.next[v] += periods[v];
        }
        acc += this.level(v) * (end - t);
        sum += acc / this.cyclesPerSample;
      }
      this.cycle = end;
      // Four voices at 15 make 60; a slow average taken off keeps the wave centred.
      const s = sum / 60;
      this.dc += (s - this.dc) * 0.0005;
      out[i] = (s - this.dc) * this.gain * 2;
    }
    // Keep the cycle count small so the float stays exact; the counters' place only matters modulo
    // their lengths (all of which divide this).
    const wrap = 465 * 511 * 131071; // a common multiple of every counter's length
    if (this.cycle > wrap) {
      this.cycle -= wrap;
      for (let v = 0; v < 4; v++) this.next[v] -= wrap;
    }
  }
}
