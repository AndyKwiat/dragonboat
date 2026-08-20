# UI overlays

Files: `src/ui/*` — Godot `Control`s on one `CanvasLayer` added in `Main`,
in draw order: city labels (under everything), HUD, clock, minimap, city
popup, tweak panel. The popup and tweak panel have `MouseFilter.Stop`, so
clicks and wheel events inside them never reach `InputState` (which listens
in `_UnhandledInput`) — that is what keeps a slider drag from firing a cannon
or zooming the map.

- **Fonts** — `SystemFont`s standing in for the web version's `monospace`
  (Menlo) and `system-ui` (Helvetica Neue); sizes are the web version's CSS
  px (`allow_hidpi=false` keeps viewport px = window points). Glyphs are
  quarter-pixel positioned and unhinted so labels that track the world glide
  instead of shimmering; `snap_controls_to_pixels=false` for the same reason.
  `Fonts.Panel` is the rgba(0,0,0,.55) rounded box used by the overlays.
- **Hud** (top right) — fps, view corners, zoom, camera mode/rig, sail-time
  factor, chunk stats, and a `guns N/M loaded, shots, in air` line from combat.
- **Clock** (top left) — date/time, rations, money, crew, cargo; tints at
  night and when rations run low.
- **Minimap** (bottom right) — a window onto `MinimapWorldFraction` of the
  world following the camera, with the view rectangle.
- **CityLabels** — one label per city, styled by size (`Config.CityLabel`),
  hidden below each size's min zoom, hover highlight; returns the hovered
  city to `Main` for the click. Under the name, discovered price intel
  (`RefreshIntel`): one red/green line per product the player knows trades
  off base price, with days left for story events — rebuilt only when the
  popup opens or the midnight pass runs, never per frame. Discovery rules:
  [trading.md](trading.md).
- **CityPopup** — the market panel: facts, rumours (nearest other-city
  story events, red/green by high/low), the hiring hall (large cities),
  then the market rows; measures its content for a few frames
  after opening / resizing (wrapped labels only know their height after
  layout) and sizes itself to 62vw clamped to 520–900 px, at most 80vh,
  scrolling beyond that. Rules it drives: [trading.md](trading.md).
- **TweakPanel** (left, under the clock; **T**) — built by reflection over
  `Tweaks.All`: a group header per `[Tweak]` group, a name / slider (or
  checkbox) / value row per field, a *dump* button that prints the current
  values as C# and copies them to the clipboard. Sliders take no keyboard
  focus so WASD keeps driving. Scrolls when taller than the window. Which
  values exist is up to `BoatConfig` / `CombatConfig`, not this file.
