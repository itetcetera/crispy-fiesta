// The art: every picture is named in public/assets/assets.json (docs/ASSETS.md), so new art drops in
// with no code change.

export interface AssetEntry {
  key: string;
  file: string;
  width: number;
  height: number;
  group: string;
  what: string;
}

export async function loadManifest(base = "/assets/"): Promise<AssetEntry[]> {
  const r = await fetch(base + "assets.json");
  if (!r.ok) throw new Error(`The art list is missing (${r.status}).`);
  return ((await r.json()) as { assets: AssetEntry[] }).assets;
}

/** Queues every picture on a Phaser loader (SVGs at twice their size, so they stay sharp when scaled). */
export function queueAssets(load: Phaser.Loader.LoaderPlugin, entries: AssetEntry[], base = "/assets/") {
  for (const e of entries) {
    if (e.file.endsWith(".svg")) load.svg(e.key, base + e.file, { width: e.width * 2, height: e.height * 2 });
    else load.image(e.key, base + e.file);
  }
}

export const walkerKey = (race: string) => "walker-" + race.toLowerCase();
export const portraitKey = (race: string) => "portrait-" + race.toLowerCase();
