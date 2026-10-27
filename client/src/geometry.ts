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

/** The town's buildings and their door columns (unk_1E00 and unk_1E28). */
export const TOWN_TOP = [
  { key: "bldg-crystite", from: 10, to: 10 },
  { key: "bldg-smithore", from: 16, to: 16 },
  { key: "bldg-energy", from: 22, to: 22 },
  { key: "bldg-food", from: 28, to: 28 },
];
export const TOWN_BOTTOM = [
  { key: "bldg-assay", from: 10, to: 10 },
  { key: "bldg-land", from: 15, to: 15 },
  { key: "bldg-pub", from: 20, to: 20 },
  { key: "bldg-corral", from: 25, to: 29 },
];

/** An Atari colour byte (hue in the high nibble, luminance in the low) as an RGB number, NTSC. */
export function atariColor(c: number): number {
  const hue = c >> 4, lum = (c & 15) >> 1;
  const y = 0.08 + (lum / 7) * 0.86;
  let i = 0, q = 0;
  if (hue) {
    const a = ((-33 + (hue - 1) * 25.7) * Math.PI) / 180;
    i = 0.22 * Math.cos(a);
    q = 0.22 * Math.sin(a);
  }
  const ch = (v: number) => Math.round(Math.max(0, Math.min(1, v)) * 255);
  return (ch(y + 0.956 * i + 0.621 * q) << 16) | (ch(y - 0.272 * i - 0.647 * q) << 8) | ch(y - 1.106 * i + 1.703 * q);
}

export const css = (rgb: number) => "#" + rgb.toString(16).padStart(6, "0");
