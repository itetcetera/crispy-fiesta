# Art

Every picture the browser client draws is listed in `client/public/assets/assets.json`, and each
file sits next to it in `client/public/assets/`. The client loads them by key, so new art drops
in with no code change: save the picture under the same name (`.svg`, `.png` or `.webp`), run
`python3 client/scripts/placeholders.py` if the extension changed, and rebuild.

Sizes are how big the picture is drawn, in pixels of the 640 x 480 game screen. Draw at 2x or 3x
for sharpness; the client scales it down. Leave the background transparent. The walkers are
tinted with each player's colour, so draw them light (white or pale grey) where the colour goes.

Placeholders are labelled boxes; this file lists what each one stands for.


## Map

| File | Size | What it shows |
|---|---|---|
| `plain` | 64 x 64 | One plot of plain ground (the map is 9 x 5 plots). |
| `river` | 64 x 64 | A river plot (the middle column, above and below the store). |
| `store` | 64 x 64 | The town (the store) in the middle of the map; the walker enters it here. |
| `mountain` | 32 x 16 | One mountain; the map draws one on each of its 20 bands, 8 scanlines high. |
| `icon-food` | 28 x 28 | The food factory on an improved plot (the output number is drawn beside it). |
| `icon-energy` | 28 x 28 | The energy factory on an improved plot (the output number is drawn beside it). |
| `icon-smithore` | 28 x 28 | The smithore factory on an improved plot (the output number is drawn beside it). |
| `icon-crystite` | 28 x 28 | The crystite factory on an improved plot (the output number is drawn beside it). |
| `assay-mark` | 28 x 28 | The assay flag on a sampled plot. |

## Players

| File | Size | What it shows |
|---|---|---|
| `walker-mechtron` | 32 x 32 | The Mechtron walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-gollumer` | 32 x 32 | The Gollumer walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-packer` | 32 x 32 | The Packer walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-bonzoid` | 32 x 32 | The Bonzoid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-spheroid` | 32 x 32 | The Spheroid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-flapper` | 32 x 32 | The Flapper walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-leggite` | 32 x 32 | The Leggite walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `walker-humanoid` | 32 x 32 | The Humanoid walking (facing right; the game mirrors it to face left). Drawn this size on the map and twice the size in town; tinted to the player's colour. |
| `portrait-mechtron` | 96 x 96 | The Mechtron for the lobby and the score screen. |
| `portrait-gollumer` | 96 x 96 | The Gollumer for the lobby and the score screen. |
| `portrait-packer` | 96 x 96 | The Packer for the lobby and the score screen. |
| `portrait-bonzoid` | 96 x 96 | The Bonzoid for the lobby and the score screen. |
| `portrait-spheroid` | 96 x 96 | The Spheroid for the lobby and the score screen. |
| `portrait-flapper` | 96 x 96 | The Flapper for the lobby and the score screen. |
| `portrait-leggite` | 96 x 96 | The Leggite for the lobby and the score screen. |
| `portrait-humanoid` | 96 x 96 | The Humanoid for the lobby and the score screen. |
| `mule` | 32 x 24 | A M.U.L.E. (twice the size in town). |
| `wampus` | 24 x 24 | The mountain wampus, when it shows. |

## Town

| File | Size | What it shows |
|---|---|---|
| `town-street` | 640 x 40 | The town's street, between the two rows of buildings. |
| `town-ground` | 640 x 480 | The town screen behind everything. |
| `bldg-crystite` | 88 x 120 | The crystite outfitter (top row, first). |
| `bldg-smithore` | 88 x 120 | The smithore outfitter (top row, second). |
| `bldg-energy` | 88 x 120 | The energy outfitter (top row, third). |
| `bldg-food` | 88 x 120 | The food outfitter (top row, last). |
| `bldg-assay` | 88 x 112 | The assay office (bottom row, first). |
| `bldg-land` | 80 x 112 | The land office (bottom row, second). |
| `bldg-pub` | 80 x 112 | The pub (bottom row, third). |
| `bldg-corral` | 120 x 112 | The M.U.L.E. corral (bottom row, right). |

## Events

| File | Size | What it shows |
|---|---|---|
| `ship` | 64 x 48 | The colony ship, landing and taking off. |
| `pirate-ship` | 64 x 48 | The pirate ship. |
| `pest` | 24 x 24 | The space pest that eats a food plot. |
| `meteorite` | 32 x 32 | The meteorite falling. |
| `radiation` | 32 x 32 | The radiation cloud. |
| `acid-cloud` | 64 x 32 | The acid rain cloud, crossing a row. |
| `sunspot` | 96 x 96 | The sun during sunspot activity. |
| `fire` | 48 x 48 | The fire in the store. |

## Auctions

| File | Size | What it shows |
|---|---|---|
| `good-food` | 48 x 48 | The food sign at the top of the status and auction screens. |
| `good-energy` | 48 x 48 | The energy sign at the top of the status and auction screens. |
| `good-smithore` | 48 x 48 | The smithore sign at the top of the status and auction screens. |
| `good-crystite` | 48 x 48 | The crystite sign at the top of the status and auction screens. |
| `good-land` | 48 x 48 | The land auction sign. |
| `store-booth` | 64 x 48 | The store's booth at the top of a goods auction. |
