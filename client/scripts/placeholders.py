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
ENTRIES = [
    ("plain", 64, 64, "Map", "One plot of plain ground (the map is 9 x 5 plots).", "#d9b36c"),
    ("river", 64, 64, "Map", "A river plot (the middle column, above and below the store).", "#5aa0d8"),
    ("store", 64, 64, "Map", "The town (the store) in the middle of the map; the walker enters it here.", "#b07a4a"),
    ("mountain", 32, 16, "Map", "One mountain; the map draws one on each of its 20 bands, 8 scanlines high.", "#8a7a6a"),
    *[(f"icon-{g}", 28, 28, "Map", f"The {g} factory on an improved plot (the output number is drawn beside it).", c)
      for g, c in zip(GOODS, ["#7cc35a", "#f2d24b", "#a0a0b0", "#5fd0d0"])],
    ("assay-mark", 28, 28, "Map", "The assay flag on a sampled plot.", "#ffffff"),
    *[(f"walker-{s}", 32, 32, "Players", f"The {s.capitalize()} walking (facing right; the game mirrors it to face left). "
       "Drawn this size on the map and twice the size in town; tinted to the player's colour.", "#ffffff")
      for s in SPECIES],
    *[(f"portrait-{s}", 96, 96, "Players", f"The {s.capitalize()} for the lobby and the score screen.", "#ffffff")
      for s in SPECIES],
    ("mule", 32, 24, "Players", "A M.U.L.E. (twice the size in town).", "#c8c8c8"),
    ("wampus", 24, 24, "Players", "The mountain wampus, when it shows.", "#e05aa0"),
    ("town-street", 640, 40, "Town", "The town's street, between the two rows of buildings.", "#c9a46a"),
    ("town-ground", 640, 480, "Town", "The town screen behind everything.", "#3a2a1a"),
    ("bldg-crystite", 88, 120, "Town", "The crystite outfitter (top row, first).", "#5fd0d0"),
    ("bldg-smithore", 88, 120, "Town", "The smithore outfitter (top row, second).", "#a0a0b0"),
    ("bldg-energy", 88, 120, "Town", "The energy outfitter (top row, third).", "#f2d24b"),
    ("bldg-food", 88, 120, "Town", "The food outfitter (top row, last).", "#7cc35a"),
    ("bldg-assay", 88, 112, "Town", "The assay office (bottom row, first).", "#d08050"),
    ("bldg-land", 80, 112, "Town", "The land office (bottom row, second).", "#c0a0e0"),
    ("bldg-pub", 80, 112, "Town", "The pub (bottom row, third).", "#e07070"),
    ("bldg-corral", 120, 112, "Town", "The M.U.L.E. corral (bottom row, right).", "#a07040"),
    ("ship", 64, 48, "Events", "The colony ship, landing and taking off.", "#e0e0f0"),
    ("pirate-ship", 64, 48, "Events", "The pirate ship.", "#303030"),
    ("pest", 24, 24, "Events", "The space pest that eats a food plot.", "#60c040"),
    ("meteorite", 32, 32, "Events", "The meteorite falling.", "#ff8030"),
    ("radiation", 32, 32, "Events", "The radiation cloud.", "#f0f060"),
    ("acid-cloud", 64, 32, "Events", "The acid rain cloud, crossing a row.", "#80a060"),
    ("sunspot", 96, 96, "Events", "The sun during sunspot activity.", "#ffd040"),
    ("fire", 48, 48, "Events", "The fire in the store.", "#ff5020"),
    *[(f"good-{g}", 48, 48, "Auctions", f"The {g} sign at the top of the status and auction screens.", c)
      for g, c in zip(GOODS, ["#7cc35a", "#f2d24b", "#a0a0b0", "#5fd0d0"])],
    ("good-land", 48, 48, "Auctions", "The land auction sign.", "#d9b36c"),
    ("store-booth", 64, 48, "Auctions", "The store's booth at the top of a goods auction.", "#b07a4a"),
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
        "Placeholders are labelled boxes; this file lists what each one stands for.",
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
