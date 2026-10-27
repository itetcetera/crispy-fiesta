// Texts the original draws itself (the server sends only their codes).

/** addStaticTurnMessage2 ($4A2B): the turn messages by code. */
export const TURN_MESSAGES: Record<number, string> = {
  1: "NEED A MULE",
  2: "NO MULES ALLOWED",
  3: "NOT ENOUGH MONEY",
  4: "NO MULES LEFT",
  5: "MULE SET FOR:",
  6: "NOT YOUR PLOT",
  7: "ALREADY GOT MULE",
  8: "NEED SOIL SAMPLE",
  9: "NO PLOT MARKED",
  10: "TIME HAS RUN OUT",
  11: "NO MULE STEALING",
  12: "GO TO BLDG FIRST",
  13: "MARK PLOT TO SELL",
  14: "GET SOIL SAMPLE",
  15: "THEN COME BACK.",
  16: "MARKED FOR SALE.",
  17: "SAMPLE READY TO",
  18: "RETURN TO OFFICE",
  19: "TRANSFERING MULE",
  20: "OUTFITTING MULE",
  21: "GO TO JAIL!",
  22: "FOOD SHORTAGE",
  23: "DECREASES TIME.",
  24: "YOU OWN IT NOW!",
  25: "1 PLOT PER TRIP",
  26: "MULE INSTALLED.",
};

/** A turn message: its static text, then any text sent with it (or that text alone, for codes like $8D). */
export function turnMessage(code: number, text: string): string {
  if (code === 0) return "";
  const fixed = TURN_MESSAGES[code] ?? "";
  return [fixed, text].filter((s) => s).join(" ");
}

/** Goods by index: food, energy, smithore, crystite (and the mules in the store's tables). */
export const GOODS = ["FOOD", "ENERGY", "SMITHORE", "CRYSTITE", "MULES"];
export const GOOD_KEYS = ["food", "energy", "smithore", "crystite"];

export const SCORE_KINDS = ["MONEY", "LAND", "GOODS", "TOTAL"];

export const MONTHS = ["", "1ST", "2ND", "3RD", "4TH", "5TH", "6TH", "7TH", "8TH", "9TH", "10TH", "11TH", "12TH"];
