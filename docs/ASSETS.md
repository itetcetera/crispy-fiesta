# Art

Every picture the browser client draws is listed in `client/public/assets/assets.json`, and each
file sits next to it in `client/public/assets/`. The client loads them by key, so new art drops
in with no code change: save the picture under the same name (`.svg`, `.png` or `.webp`), run
`python3 client/scripts/placeholders.py` if the extension changed, and rebuild.

Sizes are how big the picture is drawn, in pixels of the 640 x 480 game screen. Draw at 2x or 3x
for sharpness; the client scales it down. Leave the background transparent. The walkers are
tinted with each player's colour, so draw them light (white or pale grey) where the colour goes.

Most pictures are the original's own, cut out of the original program running in the test emulator
(`tools/extract_assets.py`); the rest are labelled boxes. Any of them can be replaced by new art.


## Map

| File | Size | What it shows |
|---|---|---|
| `plain` | 64 x 64 | One plot of plain ground (the map is 9 x 5 plots). From the original (tools/extract_assets.py). |
| `river` | 64 x 64 | A river plot (the middle column, above and below the store). From the original (tools/extract_assets.py). |
| `store` | 64 x 64 | The town (the store) in the middle of the map; the walker enters it here. From the original (tools/extract_assets.py). |
| `mountain` | 32 x 16 | One mountain; the map draws one on each of its 20 bands, 8 scanlines high. From the original (tools/extract_assets.py). |
| `icon-food` | 32 x 32 | The food factory's sign on an improved plot, in its right half; white, tinted with the owner's colour. From the original (tools/extract_assets.py). |
| `icon-energy` | 32 x 32 | The energy factory's sign on an improved plot, in its right half; white, tinted with the owner's colour. From the original (tools/extract_assets.py). |
| `icon-smithore` | 32 x 32 | The smithore factory's sign on an improved plot, in its right half; white, tinted with the owner's colour. From the original (tools/extract_assets.py). |
| `icon-crystite` | 32 x 32 | The crystite factory's sign on an improved plot, in its right half; white, tinted with the owner's colour. From the original (tools/extract_assets.py). |
| `assay-mark` | 28 x 28 | The assay flag on a sampled plot. |

## Players

| File | Size | What it shows |
|---|---|---|
| `walker-mechtron` | 32 x 36 | The Mechtron walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-gollumer` | 32 x 36 | The Gollumer walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-packer` | 32 x 36 | The Packer walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-bonzoid` | 32 x 36 | The Bonzoid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-spheroid` | 32 x 36 | The Spheroid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-flapper` | 32 x 36 | The Flapper walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-leggite` | 32 x 36 | The Leggite walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `walker-humanoid` | 32 x 36 | The Humanoid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. The placeholder is the original's town frame, from the disk (tools/species_frames.py). |
| `portrait-mechtron` | 96 x 96 | The Mechtron for the lobby and the score screen. |
| `portrait-gollumer` | 96 x 96 | The Gollumer for the lobby and the score screen. |
| `portrait-packer` | 96 x 96 | The Packer for the lobby and the score screen. |
| `portrait-bonzoid` | 96 x 96 | The Bonzoid for the lobby and the score screen. |
| `portrait-spheroid` | 96 x 96 | The Spheroid for the lobby and the score screen. |
| `portrait-flapper` | 96 x 96 | The Flapper for the lobby and the score screen. |
| `portrait-leggite` | 96 x 96 | The Leggite for the lobby and the score screen. |
| `portrait-humanoid` | 96 x 96 | The Humanoid for the lobby and the score screen. |
| `mule` | 36 x 20 | A M.U.L.E. on the map (facing right; white, tinted with its owner's colour). From the original (tools/extract_assets.py). |
| `mule-town` | 50 x 44 | A M.U.L.E. on its lead in town (facing right; white, tinted). From the original (tools/extract_assets.py). |
| `wampus` | 24 x 24 | The mountain wampus, when it shows. |

## Town

| File | Size | What it shows |
|---|---|---|
| `town-ground` | 384 x 300 | The town's yard behind the buildings (columns 8 to 31, scanlines 44 to 194; the street runs between the rows). From the original (tools/extract_assets.py). |
| `bldg-crystite` | 64 x 64 | The crystite outfitter (top row, first). From the original (tools/extract_assets.py). |
| `bldg-smithore` | 64 x 64 | The smithore outfitter (top row, second). From the original (tools/extract_assets.py). |
| `bldg-energy` | 64 x 64 | The energy outfitter (top row, third). From the original (tools/extract_assets.py). |
| `bldg-food` | 64 x 64 | The food outfitter (top row, last). From the original (tools/extract_assets.py). |
| `bldg-assay` | 64 x 112 | The assay office (bottom row, first). From the original (tools/extract_assets.py). |
| `bldg-land` | 64 x 96 | The land office (bottom row, second). From the original (tools/extract_assets.py). |
| `bldg-pub` | 64 x 80 | The pub (bottom row, third). From the original (tools/extract_assets.py). |
| `bldg-corral` | 96 x 160 | The M.U.L.E. corral (bottom row, right), down to the money line; the mule price and MULES are written over its foot in dark grey, so keep the bottom 32 px light and plain. From the original (tools/extract_assets.py). |

## Events

| File | Size | What it shows |
|---|---|---|
| `ship` | 160 x 80 | The transport ship, flying left along the store's row (its left edge at x). From the original (tools/extract_assets.py). |
| `pirate-ship` | 160 x 80 | The pirate ship. From the original (tools/extract_assets.py). |
| `pest` | 34 x 30 | The space pest that eats a food plot. From the original (tools/extract_assets.py). |
| `meteorite` | 136 x 84 | The meteorite's blast. From the original (tools/extract_assets.py). |
| `radiation` | 32 x 16 | The radiation zigzag over the mule's plot. From the original (tools/extract_assets.py). |
| `acid-cloud` | 72 x 64 | The acid rain cloud and its rain, crossing a row. From the original (tools/extract_assets.py). |
| `sunspot` | 4 x 2 | A sunspot flash: one dot somewhere on the map each frame (white, tinted). From the original (tools/extract_assets.py). |
| `fire` | 64 x 64 | The fire over the store's plot. From the original (tools/extract_assets.py). |

## Auctions

| File | Size | What it shows |
|---|---|---|
| `good-food` | 32 x 32 | The food sign in the top corners of the status and auction screens. From the original (tools/extract_assets.py). |
| `good-energy` | 32 x 32 | The energy sign in the top corners of the status and auction screens. From the original (tools/extract_assets.py). |
| `good-smithore` | 32 x 32 | The smithore sign in the top corners of the status and auction screens. From the original (tools/extract_assets.py). |
| `good-crystite` | 32 x 32 | The crystite sign in the top corners of the status and auction screens. From the original (tools/extract_assets.py). |
| `store-booth` | 56 x 18 | The store's house beside its prices (status and goods auctions). From the original (tools/extract_assets.py). |

## Text

| File | Size | What it shows |
|---|---|---|
| `font` | 128 x 32 | The lettering: 64 characters of 8 x 8 in four rows of 16, in the Atari's order (space ! " # $ % & ' ( ) * + , - . / 0-9 : ; < = > ? @ A-Z [ \ ] ^ _), white on transparent; tinted per text. Drawn 2x (small text) or 4x (titles). The original's own set, from the memory snapshot (tools/atari_font.py). |
