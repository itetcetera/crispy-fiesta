"""Writes the art manifest (public/assets/assets.json), a placeholder picture for every entry that
has no file yet, and docs/ASSETS.md. Run it again after adding entries; it never overwrites art.

    python3 client/scripts/placeholders.py
"""
import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "public", "assets")
DOC = os.path.join(os.path.dirname(ROOT), "docs", "ASSETS.md")

SPECIES = ["mechtron", "gollumer", "packer", "bonzoid", "spheroid", "flapper", "leggite", "humanoid"]
GOODS = ["food", "energy", "smithore", "crystite"]

# key, size on screen (canvas pixels, the canvas is 640 x 480), group, what it shows, placeholder colour
O = " From the original (tools/extract_assets.py)."
ENTRIES = [
    ("plain", 64, 64, "Map", "One plot of plain ground (the map is 9 x 5 plots). From the original (tools/extract_assets.py).", "#d9b36c"),
    ("river", 64, 64, "Map", "A river plot (the middle column, above and below the store). From the original (tools/extract_assets.py).", "#5aa0d8"),
    ("store", 64, 64, "Map", "The town (the store) in the middle of the map; the walker enters it here. From the original (tools/extract_assets.py).", "#b07a4a"),
    ("mountain", 32, 16, "Map", "One mountain; the map draws one on each of its 20 bands, 8 scanlines high. From the original (tools/extract_assets.py).", "#8a7a6a"),
    *[(f"icon-{g}", 32, 32, "Map", f"The {g} factory's sign on an improved plot, in its right half; white, tinted with the owner's colour.{O}", c)
      for g, c in zip(GOODS, ["#7cc35a", "#f2d24b", "#a0a0b0", "#5fd0d0"])],
    ("assay-mark", 28, 28, "Map", "The assay flag on a sampled plot.", "#ffffff"),
    *[(f"walker-{s}", 32, 36, "Players", f"The {s.capitalize()} walking (facing right; the game mirrors it to face left). "
       "Drawn this size on the map and twice the size in town; tinted to the player's colour. "
       "The placeholder is the original's town frame, from the disk (tools/species_frames.py).", "#ffffff")
      for s in SPECIES],
    *[(f"portrait-{s}", 96, 96, "Players", f"The {s.capitalize()} for the lobby and the score screen.", "#ffffff")
      for s in SPECIES],
    ("mule", 36, 20, "Players", f"A M.U.L.E. on the map (facing right; white, tinted with its owner's colour).{O}", "#c8c8c8"),
    ("mule-town", 50, 44, "Players", f"A M.U.L.E. on its lead in town (facing right; white, tinted).{O}", "#c8c8c8"),
    ("wampus", 24, 24, "Players", "The mountain wampus, when it shows.", "#e05aa0"),
    ("town-ground", 384, 300, "Town", f"The town's yard behind the buildings (columns 8 to 31, scanlines 44 to 194; the street runs between the rows).{O}", "#3a2a1a"),
    ("bldg-crystite", 64, 64, "Town", f"The crystite outfitter (top row, first).{O}", "#5fd0d0"),
    ("bldg-smithore", 64, 64, "Town", f"The smithore outfitter (top row, second).{O}", "#a0a0b0"),
    ("bldg-energy", 64, 64, "Town", f"The energy outfitter (top row, third).{O}", "#f2d24b"),
    ("bldg-food", 64, 64, "Town", f"The food outfitter (top row, last).{O}", "#7cc35a"),
    ("bldg-assay", 64, 112, "Town", f"The assay office (bottom row, first).{O}", "#d08050"),
    ("bldg-land", 64, 96, "Town", f"The land office (bottom row, second).{O}", "#c0a0e0"),
    ("bldg-pub", 64, 80, "Town", f"The pub (bottom row, third).{O}", "#e07070"),
    ("bldg-corral", 96, 160, "Town", f"The M.U.L.E. corral (bottom row, right), down to the money line; the mule price and MULES are written over its foot in dark grey, so keep the bottom 32 px light and plain.{O}", "#a07040"),
    ("ship", 160, 80, "Events", f"The transport ship, flying left along the store's row (its left edge at x).{O}", "#e0e0f0"),
    ("pirate-ship", 160, 80, "Events", f"The pirate ship.{O}", "#303030"),
    ("pest", 34, 30, "Events", f"The space pest that eats a food plot.{O}", "#60c040"),
    ("meteorite", 136, 84, "Events", f"The meteorite's blast.{O}", "#ff8030"),
    ("radiation", 32, 16, "Events", f"The radiation zigzag over the mule's plot.{O}", "#f0f060"),
    ("acid-cloud", 72, 64, "Events", f"The acid rain cloud and its rain, crossing a row.{O}", "#80a060"),
    ("sunspot", 4, 2, "Events", f"A sunspot flash: one dot somewhere on the map each frame (white, tinted).{O}", "#ffd040"),
    ("fire", 64, 64, "Events", f"The fire over the store's plot.{O}", "#ff5020"),
    *[(f"good-{g}", 32, 32, "Auctions", f"The {g} sign in the top corners of the status and auction screens.{O}", c)
      for g, c in zip(GOODS, ["#7cc35a", "#f2d24b", "#a0a0b0", "#5fd0d0"])],
    ("store-booth", 56, 18, "Auctions", f"The store's house beside its prices (status and goods auctions).{O}", "#b07a4a"),
    ("font", 128, 32, "Text", "The lettering: 64 characters of 8 x 8 in four rows of 16, in the Atari's order (space "
     "! \" # $ % & ' ( ) * + , - . / 0-9 : ; < = > ? @ A-Z [ \\ ] ^ _), white on transparent; tinted per text. "
     "Drawn 2x (small text) or 4x (titles). The original's own set, from the memory snapshot (tools/atari_font.py).", "#ffffff"),
]


def svg(key, w, h, colour):
    label = key.replace("walker-", "").replace("portrait-", "").replace("icon-", "").replace("bldg-", "").replace("good-", "")
    size = max(6, min(14, int(w / max(4, len(label)) * 1.6), int(h * 0.4)))
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">'
            f'<rect x="1.5" y="1.5" width="{w - 3}" height="{h - 3}" rx="{min(12, min(w, h) / 6):.1f}" fill="{colour}" '
            f'stroke="#111" stroke-width="3"/>'
            f'<text x="{w / 2}" y="{h / 2}" font-family="Comic Sans MS, Chalkboard, sans-serif" font-size="{size}" '
            f'font-weight="bold" text-anchor="middle" dominant-baseline="central" fill="#111">{label}</text></svg>\n')


def main():
    os.makedirs(ASSETS, exist_ok=True)
    manifest = []
    for key, w, h, group, what, colour in ENTRIES:
        file = f"{key}.svg"
        for ext in ("png", "webp"):  # real art may be a bitmap: keep the manifest pointing at it
            if os.path.exists(os.path.join(ASSETS, f"{key}.{ext}")):
                file = f"{key}.{ext}"
        path = os.path.join(ASSETS, file)
        if not os.path.exists(path):
            with open(path, "w") as f:
                f.write(svg(key, w, h, colour))
        manifest.append({"key": key, "file": file, "width": w, "height": h, "group": group, "what": what})
    with open(os.path.join(ASSETS, "assets.json"), "w") as f:
        json.dump({"assets": manifest}, f, indent=1)
        f.write("\n")

    lines = [
        "# Art",
        "",
        "Every picture the browser client draws is listed in `client/public/assets/assets.json`, and each",
        "file sits next to it in `client/public/assets/`. The client loads them by key, so new art drops",
        "in with no code change: save the picture under the same name (`.svg`, `.png` or `.webp`), run",
        "`python3 client/scripts/placeholders.py` if the extension changed, and rebuild.",
        "",
        "Sizes are how big the picture is drawn, in pixels of the 640 x 480 game screen. Draw at 2x or 3x",
        "for sharpness; the client scales it down. Leave the background transparent. The walkers are",
        "tinted with each player's colour, so draw them light (white or pale grey) where the colour goes.",
        "",
        "Most pictures are the original's own, cut out of the original program running in the test emulator",
        "(`tools/extract_assets.py`); the rest are labelled boxes. Any of them can be replaced by new art.",
        "",
    ]
    group = None
    for key, w, h, g, what, _ in ENTRIES:
        if g != group:
            lines += ["", f"## {g}", "", "| File | Size | What it shows |", "|---|---|---|"]
            group = g
        lines.append(f"| `{key}` | {w} x {h} | {what} |")
    with open(DOC, "w") as f:
        f.write("\n".join(lines) + "\n")


main()
