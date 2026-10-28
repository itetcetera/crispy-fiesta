// The original's screen geometry and colours, so that everything sits where the Atari put it.
// Positions in the game state are player/missile coordinates: x in colour clocks, y in scanlines.

export const WIDTH = 640;
export const HEIGHT = 480;

/** Canvas pixels per colour clock and per scanline. */
export const SX = 4;
export const SY = 2;

/** A player/missile position on the canvas. */
export const px = (x: number) => (x - 48) * SX;
export const py = (y: number) => (y - 24) * SY;

export const COLUMNS = 9;
export const ROWS = 5;
export const PLOTS = COLUMNS * ROWS;
export const STORE = 22;
export const RIVER = [4, 13, 22, 31, 40];

/** The top left of a plot, in player/missile coordinates (16 colour clocks by 32 scanlines). */
export const plotX = (plot: number) => 56 + 16 * (plot % COLUMNS);
export const plotY = (plot: number) => 44 + 32 * Math.floor(plot / COLUMNS);
export const PLOT_W = 16 * SX;
export const PLOT_H = 32 * SY;

/** The plot under a point, or -1 (pixelToPlot in the original). */
export function plotAt(x: number, y: number): number {
  const c = Math.floor((x - 56) / 16), r = Math.floor((y - 44) / 32);
  return c < 0 || c >= COLUMNS || r < 0 || r >= ROWS ? -1 : r * COLUMNS + c;
}

/** The mountain quarter-rows: 20 bands of 8 scanlines from y 44, each with one mountain at MountainsX. */
export const quarterY = (k: number) => 44 + 8 * k;

// The town. The street runs from y $54 to $5E; a door is entered from the column (x - $30) / 4.
export const STREET_TOP = 0x54;
export const STREET_BOTTOM = 0x5e;
export const TOWN_LEFT = 0x49;
export const TOWN_RIGHT = 0xad;
export const doorX = (column: number) => 0x30 + 4 * column;
/** The town screen's height for a walker's y: the town's sprites are double-line, two scanlines a
 * unit, so the street ($54 to $5E) lies between the shops (to line 91) and the offices (from 116). */
export const townY = (y: number) => py(2 * (y - 0x26));

/** The town's buildings and their door columns (unk_1E00 and unk_1E28). */
export const TOWN_TOP = [
  { key: "bldg-crystite", from: 9, to: 9 },
  { key: "bldg-smithore", from: 15, to: 15 },
  { key: "bldg-energy", from: 21, to: 21 },
  { key: "bldg-food", from: 27, to: 27 },
];
const LUMA = [0.6941, 0.7091, 0.7241, 0.7401, 0.756, 0.7741, 0.7931, 0.8121, 0.826, 0.847, 0.87, 0.893, 0.916, 0.942, 0.969, 1];

/** An Atari colour byte (hue in the high nibble, luminance in the low) as an RGB number, by the Atari800
 * emulator's default NTSC palette (as the test emulator's pictures of the original). */
export function atariColor(c: number): number {
  const hue = c >> 4, lum = c & 15;
  const a = ((303 + (hue - 1) * 26.8) * Math.PI) / 180, sat = hue ? 0.175 : 0;
  const i = Math.cos(a) * sat, q = Math.sin(a) * sat;
  const y = (LUMA[lum] - LUMA[0]) / (LUMA[15] - LUMA[0]);
  const ch = (v: number) => {
    v = Math.pow(Math.max(0, Math.min(1, v)), 2.35);
    v = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.pow(v, 1 / 2.4) - 0.055;
    return Math.floor(Math.max(0, Math.min(1, v)) * 255);
  };
  return (ch(y + 0.9563 * i + 0.621 * q) << 16) | (ch(y - 0.2721 * i - 0.6474 * q) << 8) | ch(y - 1.107 * i + 1.7046 * q);
}

export const css = (rgb: number) => "#" + rgb.toString(16).padStart(6, "0");

/** A seat's colour made light enough for text. */
export const textColor = (c: number) => css(atariColor((c & 0xf0) | 0x0c));
