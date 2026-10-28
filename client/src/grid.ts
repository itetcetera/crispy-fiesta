// The original's text screens: ANTIC text rows on the 40-column playfield (mode 2, 8 x 8 cells drawn
// 16 x 16 here), the 20-column rows (mode 6, 32 x 16; mode 7, 32 x 32), each at the scanline the
// original's display list puts it on. Colours are the original's register values (geometry.atariColor).

import { py } from "./geometry";

/** A text row's size: "n" mode 2 (40 columns), "w" mode 6 (20 columns), "b" mode 7 (20 columns, tall). */
export type Cell = "n" | "w" | "b";

export const CELL: Record<Cell, { w: number; h: number }> = {
  n: { w: 16, h: 16 },
  w: { w: 32, h: 16 },
  b: { w: 32, h: 32 },
};

/** The canvas position of a text cell: column on its row's grid, the row's first scanline. */
export const cellX = (cell: Cell, col: number) => col * CELL[cell].w;
export const cellY = (line: number) => py(line);

/** Mode 2 text shows playfield 1's luminance on playfield 2's colour. */
export const hiresInk = (pf1: number, pf2: number) => (pf2 & 0xf0) | (pf1 & 0x0e);

/** A number right-aligned so that its last digit is in column <end>: the column it starts in. */
export const rightAt = (text: string, end: number) => end - text.length + 1;

/** The column that centres <text> on a row of <cols> columns (the original rounds down). */
export const centre = (text: string, cols: number) => Math.floor((cols - text.length) / 2);

/** Splits a message into lines of at most <width> characters, at spaces. */
export function wrapLines(text: string, width: number): string[] {
  const lines: string[] = [];
  let line = "";
  for (const word of text.trim().split(/\s+/)) {
    if (!word) continue;
    if (line && line.length + 1 + word.length > width) {
      lines.push(line);
      line = word;
    } else line = line ? line + " " + word : word;
  }
  if (line) lines.push(line);
  return lines;
}
