# UI overlays

Files: `src/ui/*` — Godot `Control`s on one `CanvasLayer` added in `Main`,
in draw order: city labels (under everything), HUD, clock, stamina bar, wind dial, minimap, city
popup, bank/monastery windows, event log, tweak panel. The popup and tweak panel have `MouseFilter.Stop`, so
clicks and wheel events inside them never reach `InputState` (which listens
in `_UnhandledInput`) — that is what keeps a slider drag from firing a cannon
or zooming the map.

- **Fonts** — `SystemFont`s standing in for the web version's `monospace`
  (Menlo) and `system-ui` (Helvetica Neue); sizes are the web version's CSS
  px (`allow_hidpi=false` keeps viewport px = window points). Glyphs are
  quarter-pixel positioned and unhinted so labels that track the world glide
  instead of shimmering; `snap_controls_to_pixels=false` for the same reason.
  `Fonts.Panel` is the rgba(0,0,0,.55) rounded box used by the overlays, and
  `Spacer`/`Rule`/`Heading`/`MakeButton`/`SetEnabled` are the panel furniture
  the popups share (the city market panel and the bank window).
- **Hud** (top right) — fps, view corners, zoom, camera mode/rig, sail-time
  factor, the sail state / crew / wind lines, chunk stats, and a
  `guns N/M loaded, shots, in air` line from combat.
- **StaminaBar** (top centre) — the sail state (`SAILS UP` / `OARS -
  ROWING` / `OARS - RESTING`, SPACE toggles), the crew's remaining pull as a
  bar that fades green → red as it empties. Spent (0%), the track flashes
  red; that is the only thing exhaustion costs so far. Rules:
  [boat.md](boat.md).
- **WindDial** (bottom left) — `WIND 22 TO THE SE` over an arrow pointing
  the way the wind blows, with a ring and a north tick. Both directions are
  projected through the live camera rig (two points on the water either side
  of the boat, screen-space delta), so the arrow lines up with the map in
  either rig; the arrow lengthens and warms from `WindDialCalmColor` to
  `WindDialBlowColor` as the force approaches `WindDialFullForce`. The event
  log stacks above it. Rules: [boat.md](boat.md).
- **Clock** (top left) — date/time, rations, money, honour, piety (red when
  negative — [religion.md](religion.md)), crew, cargo, one
  line per VIP aboard (`VIP delivery - Ravenport (+170, 12d left)`: what
  landing them today pays and how long before they jump ship —
  [trading.md](trading.md)), one per monk aboard
  (`MONK delivery - Redcliff (due in 12d)`) and then one per outstanding
  bank loan
  (`BANK LOAN $12K - CINDERFALL (due in 11d)`, red once overdue — rebuilt
  only when a loan changes or the day turns). Tints at night and when
  rations run low.
- **Minimap** (bottom right) — a window onto `MinimapWorldFraction` of the
  world following the camera, with the view rectangle.
- **EventLog** (bottom left) — world news while you sail: the loan warnings
  and late-payment honour losses from [banking.md](banking.md), a line on
  borrowing or repaying, and the VIP passengers losing patience and
  jumping ship ([trading.md](trading.md)). `NewsEvent` (`src/core/News.cs`)
  is the shape of a line; keep them short enough for a narrow window. It
  sits above whatever the wind dial has claimed at the foot of the screen
  (`Update`'s `bottomPx`). Lines fade after `Config.EventLogSeconds` and
  the oldest go once past `EventLogMaxLines`. Anything the player must
  notice while not in a menu belongs here.
- **CityLabels** — one label per city, styled by size (`Config.CityLabel`),
  hidden below each size's min zoom, hover highlight; returns the hovered
  city to `Main` for the click. Under the name, discovered price intel
  (`RefreshIntel`): one red/green line per product the player knows trades
  off base price, with days left for story events, and under those the
  tags: `◆ OWE $12K (11d)` when that city's bank is owed money,
  `▲ VIP TO LAND HERE` / `✦ MONK TO LAND HERE (12d)` when someone aboard is
  bound for it, and `✦ MONASTERY` for a city with a house — that last one
  shows even on an undiscovered city, since a monastery is a landmark you
  can see from the water ([religion.md](religion.md)) — rebuilt only when the
  popup opens or the midnight pass runs, never per frame. Discovery rules:
  [trading.md](trading.md).
- **CityPopup** — the market panel: facts, rumours (nearest other-city
  story events, red/green by high/low), the hiring hall (large cities),
  then the market rows, each with a centre-zero trade slider (left =
  sell, right = buy; rules in [trading.md](trading.md)); measures its
  content for a few frames
  after opening / resizing (wrapped labels only know their height after
  layout) and sizes itself to 62vw clamped to 520–900 px, growing to fill the whole screen height before it scrolls,
  scrolling beyond that. Between the crew and the market it carries a
  **Bank** row (medium and large cities) whose button opens the bank
  window, and a **Monastery** row (the small cities with a house).
  Rules it drives: [trading.md](trading.md).
- **MonasteryPopup** — the monastery window, built like the bank's: the
  house's terms, alms (a slider + Give, or the days left in its week) and
  the brothers' errand with an Accept button.
  Rules: [religion.md](religion.md).
- **BankPopup** — the bank window, a second panel centred over the market
  panel that opened it: the city's terms, your account there, a slider +
  Borrow button or a Repay button. Esc / a click outside closes it and
  leaves the market open underneath. Rules: [banking.md](banking.md).
- **TweakPanel** (left, under the clock; **T**) — built by reflection over
  `Tweaks.All`: a group header per `[Tweak]` group, a name / slider (or
  checkbox) / value row per field, a *dump* button that writes every value
  moved off its code default as C# lines to `tweaks.dump` in the project
  root (gitignored; also console + clipboard). The file is re-applied over
  the code defaults at every launch, so dumped tweaks stick between runs —
  delete it for the defaults, or hand it to Claude to fold into the
  config. `--dump-tweaks` triggers the same save on startup. Sliders take no keyboard
  focus so WASD keeps driving. Scrolls when taller than the window. Which
  values exist is up to `BoatConfig` / `CombatConfig`, not this file.
