# Player turn ($6628 playersTurn) — reading notes

Working notes while porting; the rules doc stays the reference for rules. Addresses are the original's.

## Zero-page / variables
- 9F,A3: walker X,Y (player 0). A0,A4: mule X,Y (player 1; the wampus also uses player 1 on the map).
- AD: 0 = town, 1 = map. AF: building under/at walker (town). BC: outfit being carried (0 none; 1-4 = good+1; 5 land office, 6 assay; 8 = mule) — also the player event number during playerEvent.
- E0: leading a mule. CB: plot marked (FF none). 91: count of messages? (store entry count). C2: pending message code.
- D3: time left in bar pixels (91 + 10, food-shortage scaled). C4 (Timers[1]) reload = playerTurnTimeWait.
- E3: stick mask ORed into the stick (F = frozen, C = no left/right). E7: controller index.
- A9: movement phase (moves when it counts up to 6; set to F1/F8 for the mountain/river slowdown; 7 or F.. = frozen).
- C1: VBI handler index (4 = $55FE walk; E = $80B7 event text scroll; $10 = $2D29).
- B33D: player event message state. B33A/B33C/B33E/B33F/B340: low-time warning sound.
- wampusTime: ticks down with the time bar; starts (255 - C6)/4 where C6 (Timers[3]) ran from 255 while waiting for the start press.

## Handler 4 ($55FE v05 + $5625 v14), every frame
1. If C4 == 0 and 0 < D3 < 128: D3--, wampusTime-- (if not 0), erase bar pixel, C4 = playerTurnTimeWait.
2. wampus (map only, no outfit carried, E3 == 0, button up): catch check, appear/hide.
3. A9++; if A9 != 6 return. A9 = 0. If D3 negative: (mule walk-off, $5853). Else move:
   - DC = stick[E7] | E3. dy = {up -2, down +2} (doubled in town: ASL patched in, NOP on map), dx = {left -2, right +2} likewise.
   - new position D9/DA; town ($577C) or map ($581A) checks; then 9F/A3 = D9/DA.
   - animation frame AA from direction tables, AB toggles each move.
   - leading a mule: town $53C9, map $56E5 (mule follows).
## Town ($577C)
- Street band Y in [$54,$5E): free walking, E3 = 0, AF = 0.
- Above (up) / below (down): column (D9-$30)/4 indexes unk_1E00 (top buildings: outfits) / unk_1E28 (bottom: 5 land, 6 assay, 7 pub, 8 corral).
  Blocked moves set DA = A3. Messages via C2 (1..4).
- Main loop: A3 < $39 → outfit purchase; A3 >= $78 → bottom building action. X < $49 or >= $AD → exit to map.
## Map
- Walk speed: 2 px per 6 frames; P0PL&4 (mountains = player 2) and P0PF == 0 → A9 = F1 (21 frames); P0PL&8 (river/store = player 3) → A9 = F8 (14 frames).
- Entering the store: P0PL&8 with Y in [$66,$8C).
