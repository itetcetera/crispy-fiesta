"""Cuts the original's own graphics out of reference shots of the original and saves them as the
client's art (client/public/assets/<key>.png), at the size the client draws them.

The shots come from the test emulator, which runs the original program from the memory snapshot
(itself loaded from the disk image). Each shot is three PNGs of one frame: the whole picture, the
playfield alone (_pf) and the players and missiles alone (_pm, transparent elsewhere):

    cd server
    SHOTS=/tmp/shots SHOT_EVERY=16 ROUND_FROM=<seed> ROUND_SEEDS=1 \
        dotnet test Mule.Engine.Tests --filter "FullyQualifiedName~Round_matches"
    SHOTS=/tmp/shots SHOT_EVERY=30 dotnet test Mule.Engine.Tests --filter "FullyQualifiedName~Town_matches"
    python3 tools/extract_assets.py /tmp/shots

The seeds below give the round events (0 pirate ship, 1 fire, 2 transport ship, 3 acid rain, 4 pests,
6 planetquake, 7 meteorite, 8 radiation, 17 sunspots). Rectangles are in canvas pixels (the
client's 640 x 480 screen; a shot pixel is a canvas pixel shifted by 64, 32).
"""
import glob
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "client", "public", "assets")
SHOTS = sys.argv[1] if len(sys.argv) > 1 else "/tmp/shots"
DX, DY = 64, 32


def shot(name, layer=""):
    """The shot whose file name starts with <name> (e.g. "s2_008000"), in one layer."""
    files = sorted(f for f in glob.glob(os.path.join(SHOTS, name + "*" + (f"_{layer}" if layer else "") + ".png"))
                   if layer or not f.endswith(("_pf.png", "_pm.png")))
    if not files:
        raise SystemExit(f"no shot {name} {layer}")
    return Image.open(files[0]).convert("RGBA")


def cut(img, x0, y0, x1, y1):
    return img.crop((x0 + DX, y0 + DY, x1 + DX, y1 + DY))


def keep(img, test):
    """Transparent wherever <test>(r, g, b, a) is false."""
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            if not test(*px[x, y]):
                px[x, y] = (0, 0, 0, 0)
    return img


def white(img):
    """Every shown pixel white, for pictures the client tints."""
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            if px[x, y][3]:
                px[x, y] = (255, 255, 255, 255)
    return img


def not_bg(bg):
    return lambda r, g, b, a: a and (r, g, b) != bg


def save(key, img):
    img.save(os.path.join(OUT, f"{key}.png"))
    print(key, img.size)


def blob(img, colour, max_h=16):
    """The bounding box of the first connected run of <colour> no taller than <max_h> (a mountain)."""
    px = img.load()
    seen = set()
    for y in range(img.height):
        for x in range(img.width):
            if (x, y) in seen or px[x, y][:3] != colour or not px[x, y][3]:
                continue
            stack, box = [(x, y)], [x, y, x, y]
            seen.add((x, y))
            while stack:
                cx, cy = stack.pop()
                box = [min(box[0], cx), min(box[1], cy), max(box[2], cx), max(box[3], cy)]
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < img.width and 0 <= ny < img.height and (nx, ny) not in seen \
                            and px[nx, ny][3] and px[nx, ny][:3] == colour:
                        seen.add((nx, ny))
                        stack.append((nx, ny))
            if box[3] - box[1] < max_h and box[2] - box[0] > 8:
                return box
    return None


GREY = (0x4B, 0x4B, 0x4B)  # the map's mountains


def main():
    os.makedirs(OUT, exist_ok=True)

    # The map: a plot is 64 x 64 from (32 + 64 col, 40 + 64 row). Plain ground is the background
    # alone; the river and the store are drawn by players 3 (the whole picture holds both).
    sales = "s2_000824"
    plot = lambda p: (32 + 64 * (p % 9), 40 + 64 * (p // 9))
    x, y = plot(0)
    save("plain", cut(shot(sales, "pf"), x, y, x + 64, y + 64))
    x, y = plot(13)
    save("river", cut(shot(sales), x, y, x + 64, y + 64))
    x, y = plot(22)
    save("store", cut(shot(sales), x, y, x + 64, y + 64))
    # A mountain: one band (8 scanlines) of player 2, centred in a cell of 8 colour clocks.
    pm = cut(shot(sales, "pm"), 0, 0, 640, 448)
    b = blob(pm, GREY)
    cx, top = (b[0] + b[2] + 1) // 2, b[1] - (b[1] - 40) % 16
    save("mountain", keep(pm.crop((cx - 16, top, cx + 16, top + 16)), lambda r, g, bl, a: a and (r, g, bl) == GREY))

    # The goods' signs, at the corners of the status screen (column 1, lines 31 to 46): the same
    # glyphs mark a plot's factory on the map (white there, tinted with the owner's colour).
    for good, phase in (("food", "goods-0+120"), ("energy", "goods-1+120"), ("smithore", "goods-2+120"), ("crystite", "goods-3+120")):
        name = os.path.basename(glob.glob(os.path.join(SHOTS, f"s2_*_{phase}.png"))[0])[:9]
        pf = shot(name, "pf")
        bg = pf.getpixel((DX + 2, DY + 2))[:3]
        sign = keep(cut(pf, 32, 14, 64, 46), not_bg(bg))
        save(f"good-{good}", sign)
        save(f"icon-{good}", white(sign.copy()))
    # The store's price houses (players on the status screen).
    status = os.path.basename(glob.glob(os.path.join(SHOTS, "s2_*_goods-2+120.png"))[0])[:9]
    save("store-booth", cut(shot(status, "pm"), 64, 244, 120, 262))

    # The ships cross the map on the store's row; cut from their left edge (x0, player 0).
    save("pirate-ship", cut(shot("s0_005944", "pm"), 124, 150, 284, 230))
    save("ship", keep(cut(shot("s2_006480", "pm"), 140, 150, 300, 230), not_bg((0, 0, 0))))
    # The fire in the store (the store itself left out).
    save("fire", keep(cut(shot("s1_005384", "pm"), 288, 164, 352, 228), not_bg((0, 0, 0))))

    # The round events' sprites.
    save("pest", keep(cut(shot("s4_010192", "pm"), 328, 98, 362, 128), lambda r, g, b, a: a and (r, g, b) == (0, 0, 0)))
    save("acid-cloud", keep(cut(shot("s3_007440", "pm"), 8, 296, 80, 360), not_bg(GREY)))
    save("meteorite", cut(shot("s7_009056", "pm"), 552, 276, 688, 360))

    # Radiation: the zigzag over the plot; then the mule walks off (map size, white: the client tints
    # it with the owner's colour).
    save("radiation", cut(shot("s8_005808", "pm"), 488, 128, 520, 144))
    save("mule", white(cut(shot("s8_005904", "pm"), 512, 120, 548, 140)))
    # Sunspots: a dot that flashes up somewhere on the map each frame.
    save("sunspot", white(cut(shot("s17_006704", "pm"), 344, 94, 348, 96)))

    # The town (turn tests' shots): the yard and each building, from the playfield alone. The yard
    # is left black where the buildings stand, so each building can be redrawn on its own.
    town = shot("turn0_00180", "pf")
    yard = cut(town, 128, 40, 512, 340)
    buildings = {
        "bldg-crystite": (144, 56, 208, 120), "bldg-smithore": (240, 56, 304, 120),
        "bldg-energy": (336, 56, 400, 120), "bldg-food": (432, 56, 496, 120),
        "bldg-assay": (144, 184, 208, 296), "bldg-land": (224, 184, 288, 280),
        "bldg-pub": (304, 184, 368, 264), "bldg-corral": (400, 184, 496, 344),
    }
    for key, (x0, y0, x1, y1) in buildings.items():
        img = cut(town, x0, y0, x1, y1)
        if key == "bldg-corral":
            # The mule price and MULES are written over its foot (lines 180 to 195): keep it plain.
            grey = img.getpixel((2, 2))
            for y in range(312 - y0, y1 - y0):
                for x in range(img.width):
                    img.putpixel((x, y), grey)
        save(key, img)
        yard.paste((0, 0, 0, 255), (x0 - 128, y0 - 40, min(x1, 512) - 128, min(y1, 340) - 40))
    save("town-ground", yard)
    # The mule on its lead in town (double-line sprites), turned to face right as the walkers do.
    save("mule-town", white(cut(shot("turn1_04530", "pm"), 300, 120, 350, 164)).transpose(Image.FLIP_LEFT_RIGHT))


main()
