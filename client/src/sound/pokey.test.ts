import { describe, expect, it } from "vitest";
import { AUDC, AUDCTL, AUDF, CLOCK, Pokey } from "./pokey";

/** Zero crossings a second of a rendered stretch, for a square wave twice its frequency. */
function crossings(chip: Pokey, seconds: number): number {
  const out = new Float32Array(Math.round(chip.sampleRate * seconds));
  chip.render(out);
  let n = 0;
  for (let i = 1; i < out.length; i++) if (out[i - 1] < 0 !== out[i] < 0) n++;
  return n / seconds;
}

describe("the sound chip", () => {
  it("is silent with every register 0", () => {
    const chip = new Pokey(48000);
    const out = new Float32Array(4800);
    chip.render(out);
    expect(Math.max(...out.map(Math.abs))).toBe(0);
  });

  it("plays a pure tone at the 64 kHz clock divided by AUDF + 1, halved", () => {
    const chip = new Pokey(48000);
    const regs = new Array(9).fill(0);
    regs[AUDF(0)] = 0x3c; // the good-news tune's highest note
    regs[AUDC(0)] = 0xa8; // pure tone, volume 8
    chip.write(regs);
    crossings(chip, 0.5); // let the centring settle
    const expected = CLOCK / 28 / (0x3c + 1) / 2;
    expect(crossings(chip, 1) / 2).toBeCloseTo(expected, -1);
  });

  it("runs the dividers at 15 kHz with AUDCTL bit 0", () => {
    const chip = new Pokey(48000);
    const regs = new Array(9).fill(0);
    regs[AUDF(2)] = 0x10;
    regs[AUDC(2)] = 0xa6;
    regs[AUDCTL] = 1;
    chip.write(regs);
    crossings(chip, 0.5);
    const expected = CLOCK / 114 / 0x11 / 2;
    expect(crossings(chip, 1) / 2).toBeCloseTo(expected, -1);
  });

  it("makes noise from the polynomial counters", () => {
    const chip = new Pokey(48000);
    const regs = new Array(9).fill(0);
    regs[AUDF(3)] = 0x30;
    regs[AUDC(3)] = 0x08; // 5-bit then 17-bit noise
    chip.write(regs);
    const out = new Float32Array(48000);
    chip.render(out);
    const loud = out.filter((x) => Math.abs(x) > 0.01).length;
    expect(loud).toBeGreaterThan(1000);
  });
});
