# dragonboat/game

A 3D world viewer (the start of a game) built with TypeScript, three.js, and Vite, seen from an isometric camera by default (V switches to a 45° perspective rig). (The previous top-down 2D version is kept, un-tracked, in `../game-old` for comparison.) It loads a world exported from the sibling `../world-generator` project and lets you explore it with a free camera.

## Running

```bash
npm install
npm run dev        # vite dev server at http://localhost:5173
npm run build      # tsc --noEmit + vite build
npx tsc --noEmit   # typecheck only
npm run cities          # regenerate cities.json: map -> products -> overrides
npm run city-products   # re-deal the product lists (+ overrides)
npm run price-overrides # re-deal just the local price quirks
npm run resize-cities   # re-cut the map's city blocks to match city sizes
npm run test:cities   # check cities.json against the map
npm run test:products # check products.json + each city's market
npm run test:time     # check the calendar, crew and upkeep (runs the real src/)
```

No general test suite. `npm run test:cities`, `test:products` and `test:time`
are the only automated checks; otherwise verify changes by running the app and
looking at it. `test:time` imports `src/gameTime.ts`, `src/crew.ts` and
`src/upkeep.ts`
directly (Node strips the types) — that only works because those modules have
no value imports, since Node can't resolve the extensionless imports the rest
of `src/` uses. Keep them that way, or the test stops running.

## The world

- The map lives in `public/map.dmap`, exported by `../world-generator` (format spec: `../world-generator/map-format.md`). It is **committed** (the root `.gitignore` ignores `*.dmap` but un-ignores this one), so a fresh clone runs as-is; re-exporting the world means committing the new `public/map.dmap` and regenerating `cities.json`.
- Format (little-endian): bytes 0–3 ASCII `DMAP`, uint32 width (1000), uint32 height (1000), then width×height uint8 terrain indices, row-major, **top-to-bottom**.
- Terrain indices: 0 ocean, 1 beach, 2 grassland, 3 forest, 4 mountain, 5 river, 6 canal, 7 city.
- Each map cell is a `TILE_SCALE`×`TILE_SCALE` block of world units (`TILE_SCALE = 20` in `src/config.ts`), so the 1000×1000 map is a 20000×20000 world. Changing `TILE_SCALE` rescales everything; nothing else hardcodes the world size.

### Coordinate system (important)

World coordinates are **y-down** with (0,0) at the map's **top-left**, x right — matching the dmap file's row order and screen space; height is z (up). All camera state, HUD readouts, and minimap math use these coordinates (0–20000 on both axes). Three.js is y-up internally; the trick that makes everything line up is the `world` group in `main.ts`, scaled by -1 in y: every mesh lives inside it with local position = world position, and three flips face winding for the negative determinant so meshes render normally (materials use `DoubleSide` anyway). Consequences:

- dmap rows map straight into texture rows — no flips anywhere (terrain bake, minimap, cursor math are all direct).
- Scene y = -world y. A **camera rig** (`createRig` in `src/camera/CameraController.ts`, configured in `CONFIG.camera3d.rigs`) is a projection (orthographic or perspective) plus pitch and yaw; `applyToCamera(rig, state, view)` places the rig's camera behind the focus along the rig's `forward` ground direction, looking down at `pitch`, so screen-up is `forward` on the ground. Yaw 0 = screen-up is north (world -y); the default `isometric` rig is orthographic, pitch 35.264°, yaw 45° (north appears up-left, east up-right). `zoom` is still screen px per world unit, measured at the focus point; a perspective rig derives its distance from it (`focusDistanceFor`), an orthographic one sets its frustum from it. Rigs are independent of camera *modes* (follow/free decide where the focus is); `main.ts` cycles rigs with V (`rigCycle`) and hands the new rig to the free camera.
- Screen-px → world conversions: `screenToGround` (ray onto z=0, works for both projections) when exact; the free camera's pan/drag/zoom-anchoring uses `screenDeltaToGround(rig, …)`, the linear focus-plane approximation rotated by yaw with vertical distances divided by `rig.groundYScale` (= sin(pitch)). `worldToScreen` projects world points (city labels).

### 3D terrain (src/terrainChunks.ts)

The flat textured plane from the 2D version is still drawn at z=0 — it is the ground and the water. Raised terrain (`CONFIG.terrainHeights` per terrain index: land 8, forest 10, mountain 14, city 24, water 0) is streamed on top as chunk meshes: each chunk is `chunks.cellsPerChunk`² columns merged into one vertex-colored `MeshBasicMaterial` mesh (top face + only exposed side faces, sides shaded darker; water columns emit nothing). Every frame `main.ts` casts the four screen corners onto the ground (`screenToGround`, capped at `maxViewDistance`), and `chunks.update(bounds)` picks the smallest LOD step (1, 2, 4 … `maxLodStep` map cells per column, majority terrain per block) whose chunk count fits `maxVisibleChunks`, shows cached chunks, builds missing ones nearest-first within `buildBudgetMs`, and evicts the least-recently-used beyond `maxCachedChunks`. Chunks are keyed by `step:ci:cj`, so zooming across an LOD boundary reuses whatever was built before. Never build per-tile meshes; add new things as meshes/instances inside the `world` group.

## Cities

`cities.json` (repo root of `game/`, committed) is generated from the map by
`scripts/generate-cities.js`: cities are blocks of terrain index 7, one city
per block at the block's centroid in world units. Names come from a
fixed pool, sizes are dealt 6 small : 3 medium : 1 large and then the large
ones are swapped with non-large ones until their mutual 1/d² repulsion energy
stops dropping, which spreads them across the map. A fixed PRNG seed makes
runs reproducible. Regenerate after re-exporting the map — the file is not
derived at runtime. `scripts/test-cities.js` re-derives the blocks from the
.dmap and checks coverage, that each block is solid and the right size for its
city (see below), unique names, the 6:3:1 ratio, and that the large
cities beat a random assignment's spread.

Each city also carries a `products` list (product ids only) filled in by
`scripts/generate-city-products.js` — a separate pass because it depends on
`products.json`, not the map. `npm run cities` runs both in order; running the
map generator alone would drop the product lists. Size decides the stock:
large = every product, medium = the six staples + one luxury, small = rice +
one other staple.

A third pass, `scripts/generate-price-overrides.js`, gives each medium/large
city 0–2 `priceOverrides` — `{ id: multiplier }` between 0.5 and 1.5 (snapped
to 0.05, never exactly 1) on products it actually stocks. Small cities never
get any. The three generators must run in order (`npm run cities` does), since
each later pass rebuilds the city objects the earlier one wrote.

### Block sizes

A fresh export paints every city as a 5×5 block. The last pass,
`scripts/resize-city-blocks.js`, cuts each block down to match its city size —
**3×3 small, 4×4 medium, 5×5 large** — turning the trimmed cells into
grassland, and writes both `public/map.dmap` and the cities' re-centred
locations. It's idempotent (a block already at its target size is left alone),
so re-running is a no-op. Trimming 5→4 can't stay centred, so the extra
row/column comes off the right/bottom and that city's location moves half a
tile.

Because the block shape now *is* the record of a city's size on the map,
`generate-cities.js` reads the size back from the block edge whenever it sees
a map that isn't all-5×5, instead of re-dealing the 6:3:1 ratio — otherwise a
re-run would assign a large city to a 3×3 block. Only a freshly exported map
gets sizes dealt (and the large ones spread).

## Products and markets

`products.json` (committed, bundled like cities.json) is the whole catalogue:
`id` (ALL_CAPS, human readable), `name`, `description`, `basePrice`. Tweak it
freely — nothing about a product is hardcoded. **Prices are never stored.**
`src/market.ts` derives them: the price a city *pays* is
`ceil(basePrice * CONFIG.market.sizeMultiplier[size] * override)` (1x / 2x / 3x
for small / medium / large, times that city's `priceOverrides` entry if it has
one) and the price it *charges* is that times `CONFIG.market.buyMarkup` (1.2),
rounded up again. Every price is an integer; rounding is always up, on both
sides. A row whose multiplier isn't 1 is tinted in the popup —
`CONFIG.market.dearColor` (red) above 1, `cheapColor` (green) below — with the
percentage next to the name.

## Game time

`src/gameTime.ts` is the clock: 30-day months counting up forever (no years)
and a 24-hour time. **It only moves when the player acts** — nothing ticks it
per frame. Being under way costs `hoursPerSecondSailing` (1 hour) per real
second while `boat.speed !== 0` and the boat is drivable (`main.ts`) — any
direction, coasting included, so the clock keeps running until the boat has
fully stopped, and free-camera panning is free (the boat isn't driven there, so
its stale speed must not count).

Zoomed out, **the whole voyage runs in fast-forward**, since the same screen
covers more world: `timeScaleForZoom` is `nearRate` (1x) at or above `nearZoom`
(3.5 px/u), `farRate` at or below `farZoom` (0.37), linear in zoom in between
and flat outside. `main.ts` multiplies the frame's `dt` by it once and uses
that `gameDt` for **both** the boat's physics and the clock, so the boat really
does cover more ground per real second and a day of sailing still buys a day of
distance. Because one frame can then be a long stretch of game time, the boat
is sub-stepped in slices of at most `CONFIG.boat.maxStepSeconds` (0.05) so
turns don't cut corners. The scale is read from the live camera state, so it
works in every mode, and the HUD shows the current rate.

This applies to travel only — every market button still costs a flat
`minutesPerTransaction` (10). Fractional minutes
accumulate internally; only whole ones are displayed. `src/clock.ts` is the
top-left readout, dimmed to `nightColor` between `nightStartHour` (20:00) and
`nightEndHour` (05:00) — the night test wraps past midnight, so it's an OR.

### Crew and upkeep

The crew is a head count on the Player, `CONFIG.crew.min`..`max` (1–20),
starting at `starting` (1). Hands are hired and dismissed for free in the
**large cities only** — the city panel grows a Crew section above the Market
there and hides it everywhere else — and those buttons are the one exception to
the market's ten-minutes-per-click rule (`button(..., { costsTime: false })`).

`src/crew.ts` is the pure maths:

- `crewSpeedMultiplier` runs straight from `speedAtMin` (1x) at a minimum crew
  to `speedAtMax` (2x) at `speedFullCrew` (10), then stays flat — hands past
  ten are for the cargo they carry, not the pace they set. `main.ts` feeds it
  into `boat.maxSpeedScale` every frame, which scales the boat's forward top
  speed only — reversing is unaffected.
- `cargoCapacity` is `crew * cargoPerCrew` (100), so the hold runs 100 units at
  one hand to 2000 at twenty. `player.buy()` caps at `cargoSpace()` — a partial
  buy is fine, so Buy 10 with 3 spaces buys 3, and the Buy buttons only grey out
  when the hold is completely full. Money still never blocks a trade.
- `crewChangeAllowed` is the dismissal rule: a hand can't go ashore if the
  cargo wouldn't fit without them. Enforced in `player.changeCrew` (not just
  the UI), with the Remove button greyed out and a tooltip saying why.

`src/upkeep.ts` holds both running costs, **each multiplied by the crew size**.
They tick on **game** time (so they only bite while sailing or trading) and
share one `createDue` helper that catches up on every charge that came due —
one long stretch on the throttle settles the whole bill, not a single unit:

- **Rations** — one unit of `CONFIG.rations.productId` per crew member per
  `hoursPerUnit` (8), so rice is cargo and fuel at once. The hold stops at 0
  (`player.consume` caps at what's held) and missed meals are *not* owed later:
  restocking starts a fresh 8-hour clock. Turns `lowColor` below `lowUnits`
  (10).
- **Wages** — `CONFIG.wages.moneyPerDay` (1) per crew member per game day.
  Money is allowed to go negative; the readout goes red when it is.

Rice, money, the crew count and `cargo used/capacity` all sit under the date in
the clock overlay (always visible, wherever you are), and a warning colour
outranks the night dimming. The player starts with
`CONFIG.player.startingInventory` (20 rice) so the low warning means something.
An open market panel is refreshed when either charge lands, or it would show a
stale hold and purse.

`src/player.ts` holds the run state: `money` (may go negative), the crew count
and an inventory Map (a count never goes below 0 — `sell()` caps at what's held
and returns how many actually moved — and the total never exceeds
`cargoCapacity()`). Nothing persists across a reload; the run
starts at `CONFIG.player.startingMoney`. The clock overlay shows money, and
`src/cityPopup.ts` renders the market: one row per product the city trades,
with Buy 1/10 and Sell 1/10 (sell greys out when the hold is short). The
player can only sell what the city stocks, since only stocked products get a
row.

## Architecture

```
src/config.ts     All tunables: TILE_SCALE, zoom limits, pan speed, grid fade,
                  minimap size, terrain color table. New knobs go here.
src/dmap.ts       Pure .dmap parser (no three.js import). Exposes DMap
                  {width, height, data}, TERRAIN enum, terrainAt(map, x, y)
                  — use terrainAt for gameplay logic reading the map.
src/terrain.ts    Bakes indices → 1000×1000 RGBA DataTexture (NearestFilter)
                  on ONE PlaneGeometry covering the whole world; a raw
                  ShaderMaterial samples it and overlays the tile grid. In 3D
                  this is the ground/water plane at z=0.
src/terrainChunks.ts  Streamed raised-terrain chunk meshes with LOD (see
                  "3D terrain" above).
src/input.ts      Single InputState shared by all controllers: key set,
                  `pressed` (keys that went down this frame, for one-shot
                  actions), drag deltas, wheel delta, cursor pos. Deltas
                  accumulate between frames; the active controller reads them
                  then calls consumeFrameDeltas(). No per-controller listeners.
src/boat.ts       The player boat: x/y, heading, signed speed + its brown
                  BoxGeometry mesh (sitting on the water at z=0). Arcade physics:
                  accelerate/brake/friction, steering authority scaled by
                  speed, forward top speed scaled by maxSpeedScale (crew size).
                  No collision — it drives over any terrain.
src/camera/CameraController.ts   The camera-mode seam (see below) + pure
                  helpers: clampCamera, minZoomFor, viewRect, applyToCamera.
src/camera/FreeCameraController.ts  WASD/drag pan, cursor-anchored zoom.
src/camera/FollowCameraController.ts  Center locked to a FollowTarget (the
                  boat); wheel zooms, center isn't user-movable.
src/cities.ts     Loads cities.json (bundled, not fetched) and re-exports it
                  as typed City[] {name, location, size}.
src/cityLabels.ts DOM overlay: one absolutely-positioned label per on-screen
                  city, font/color/min-zoom per size (small names appear only
                  when zoomed in; large ones always). Hidden labels are also
                  unclickable. The layer is pointer-events:none
                  so map dragging still works — hover/click hit-testing is
                  done in JS against city screen positions (circle around the
                  centre OR the padded label box, nearest wins).
src/cityPopup.ts  Closable, screen-centred panel for the clicked city: facts
                  (add a row to its FIELDS list), a Crew section in large
                  cities only, then the Market section built from
                  marketFor(city), with buy/sell buttons wired to the Player.
                  Sized from CONFIG.cityPopup.
src/gameTime.ts   The month/day/hour clock. Pure and dependency-free (its
                  settings are passed in) so the test script can run it.
src/crew.ts       Crew clamping, the crew -> top-speed multiplier, hold
                  capacity and the dismissal rule. Pure and dependency-free,
                  same as gameTime.ts.
src/upkeep.ts     Crew rations (rice) and wages (money), per head, driven by
                  the clock. Pure and dependency-free too.
src/clock.ts      Top-left scoreboard: date/time + rice, money, crew and
                  cargo/capacity; dims at night.
src/products.ts   Loads products.json as typed Product[] + productById().
src/market.ts     Pure price rules: sellPriceFor / buyPriceFor / marketFor.
src/player.ts     Money + inventory, with the buy/sell invariants.
src/hud.ts        Top-right overlay: view TL/BR corners (clamped to world
                  bounds), zoom, active camera mode and the zoom-driven sailing
                  time rate. Plain DOM, updates via textContent diff.
src/minimap.ts    Bottom-right <canvas> overlay: static map image cached in
                  an offscreen canvas once; each frame it blits the window
                  around the camera (minimap.worldFraction of the world,
                  clamped to bounds) + a white viewport strokeRect clipped to
                  that window. Not a three.js scene.
src/main.ts       Wiring: load map → renderer/scene/world group → terrain plane + chunks → boat → controllers →
                  rAF loop (mode toggle → boat → camera update → clamp →
                  apply → grid opacity → render → minimap/hud). Resize
                  handler recomputes minZoom and re-clamps.
```

### Camera modes

`CameraState` is `{centerX, centerY, zoom}` where **zoom = screen px per world unit** (the single zoom source of truth; `minZoom` = whole-world-fits, recomputed on resize; `maxZoom` in config). To add a camera mode (e.g. follow-a-unit, scripted):

1. Implement `CameraController` (`name`, `enable(state)`, `disable()`, `update(dt, input, view)`, `getState()`).
2. Register it in the `controllers` record in `main.ts`.
3. Call `switchMode(name)` — it hands the current `CameraState` to the new mode so transitions are seamless.

`follow` is the mode the game starts in, on the boat at `boat.followZoom`. `C`
cycles `follow` → `free` (handled in `main.ts` via `input.pressed` and
`MODE_CYCLE`). WASD is shared: it pans in free mode and drives the boat in the
boat-following modes, and `main.ts` only calls `boat.update()` outside free
mode, so the two never fight over the keys.

Controllers only mutate their own state; `main.ts` clamps (`clampCamera`) and applies to the actual camera after every update, so modes never need bounds logic. Clamping rule: per axis, if the view is wider than the world, lock centered (letterbox); else clamp center to `[half, worldSize - half]`.

### Rendering notes

- The flat terrain plane is one draw call, plus one draw call per visible chunk (bounded by `maxVisibleChunks`). Camera near/far are derived from the focus distance in `applyToCamera`.
- The grid is drawn in the terrain fragment shader using `fract(worldPos / uTileScale)` + `fwidth()` for constant ~1px lines; it fades in between `grid.fadeMinPxPerTile` and `fadeMaxPxPerTile` screen px per tile (config).
- The terrain `DataTexture` is deliberately `NoColorSpace`: the raw ShaderMaterial skips three.js sRGB output encoding, so an untagged texture passes hex colors through 1:1 and matches the minimap's 2D canvas. If you switch to a built-in material, revisit this or colors will shift.
- Wheel zoom is exponential and anchored at the cursor (the world point under the cursor stays fixed). Keyboard pan speed is in screen px/sec (`keyPanPxPerSec / zoom` world units), so panning feels the same at any zoom.

## Conventions

2-space indent, semicolons, single quotes, named exports, no classes except camera controllers, no frameworks. Tunables belong in `config.ts`, not inline. The sibling `../world-generator/CLAUDE.md` describes the same style. Keep `dmap.ts` free of three.js imports.
