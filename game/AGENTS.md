# dragonboat/game

A top-down 2D world viewer (the start of a game) built with TypeScript, three.js, and Vite. It loads a world exported from the sibling `../world-generator` project and lets you explore it with a free camera.

## Running

```bash
npm install
npm run dev        # vite dev server at http://localhost:5173
npm run build      # tsc --noEmit + vite build
npx tsc --noEmit   # typecheck only
npm run cities          # regenerate cities.json: map -> products -> overrides
npm run city-products   # re-deal the product lists (+ overrides)
npm run price-overrides # re-deal just the local price quirks
npm run test:cities   # check cities.json against the map
npm run test:products # check products.json + each city's market
```

No general test suite. `npm run test:cities` and `npm run test:products` are
the only automated checks; otherwise verify changes by running the app and
looking at it.

## The world

- The map lives in `public/map.dmap`, exported by `../world-generator` (format spec: `../world-generator/map-format.md`). **`*.dmap` is gitignored** — a fresh clone must copy an export into `public/` or the app won't load.
- Format (little-endian): bytes 0–3 ASCII `DMAP`, uint32 width (1000), uint32 height (1000), then width×height uint8 terrain indices, row-major, **top-to-bottom**.
- Terrain indices: 0 ocean, 1 beach, 2 grassland, 3 forest, 4 mountain, 5 river, 6 canal, 7 city.
- Each map cell is a `TILE_SCALE`×`TILE_SCALE` block of world units (`TILE_SCALE = 20` in `src/config.ts`), so the 1000×1000 map is a 20000×20000 world. Changing `TILE_SCALE` rescales everything; nothing else hardcodes the world size.

### Coordinate system (important)

World coordinates are **y-down** with (0,0) at the map's **top-left**, x right — matching the dmap file's row order and screen space. All camera state, HUD readouts, and minimap math use these coordinates (0–20000 on both axes). Three.js is y-up internally; the trick that makes everything line up is in `applyToCamera` (`src/camera/CameraController.ts`): the orthographic frustum sets `top` to the *smaller* y, vertically flipping the view. Consequences:

- dmap rows map straight into texture rows — no flips anywhere (terrain bake, minimap, cursor math are all direct).
- The flipped projection reverses triangle winding, so the terrain material uses `side: THREE.DoubleSide`.
- Screen-px → world conversions never negate y: `world = center + (cursorPx - screenCenterPx) / zoom` on both axes.

## Cities

`cities.json` (repo root of `game/`, committed) is generated from the map by
`scripts/generate-cities.js`: cities are 5×5 blocks of terrain index 7, one
city per block at the block's centroid in world units. Names come from a
fixed pool, sizes are dealt 6 small : 3 medium : 1 large and then the large
ones are swapped with non-large ones until their mutual 1/d² repulsion energy
stops dropping, which spreads them across the map. A fixed PRNG seed makes
runs reproducible. Regenerate after re-exporting the map — the file is not
derived at runtime. `scripts/test-cities.js` re-derives the blocks from the
.dmap and checks coverage, unique names, the 6:3:1 ratio, and that the large
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

`src/player.ts` holds the run state: `money` (may go negative) and an
inventory Map (a count never goes below 0 — `sell()` caps at what's held and
returns how many actually moved). Nothing persists across a reload; the run
starts at `CONFIG.player.startingMoney`. The HUD shows money/cargo, and
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
                  ShaderMaterial samples it and overlays the tile grid.
src/input.ts      Single InputState shared by all controllers: key set,
                  `pressed` (keys that went down this frame, for one-shot
                  actions), drag deltas, wheel delta, cursor pos. Deltas
                  accumulate between frames; the active controller reads them
                  then calls consumeFrameDeltas(). No per-controller listeners.
src/boat.ts       The player boat: x/y, heading, signed speed + its brown
                  PlaneGeometry mesh (z=1, above terrain). Arcade physics:
                  accelerate/brake/friction, steering authority scaled by
                  speed. No collision — it drives over any terrain.
src/camera/CameraController.ts   The camera-mode seam (see below) + pure
                  helpers: clampCamera, minZoomFor, viewRect, applyToCamera.
src/camera/FreeCameraController.ts  WASD/drag pan, cursor-anchored zoom.
src/camera/FollowCameraController.ts  Center locked to a FollowTarget (the
                  boat); wheel zooms, center isn't user-movable.
src/camera/DynamicZoomCameraController.ts  Follows the boat and picks the
                  zoom itself from the distance to the nearest city (eased),
                  leading it along its heading by speed-scaled look-ahead;
                  also exposes speedScale() for the boat's top speed.
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
                  (add a row to its FIELDS list) then the Market section built
                  from marketFor(city), with buy/sell buttons wired to the
                  Player. Sized from CONFIG.cityPopup.
src/products.ts   Loads products.json as typed Product[] + productById().
src/market.ts     Pure price rules: sellPriceFor / buyPriceFor / marketFor.
src/player.ts     Money + inventory, with the buy/sell invariants.
src/hud.ts        Top-right overlay: view TL/BR corners (clamped to world
                  bounds), zoom, and active camera mode. Plain DOM, updates
                  via textContent diff.
src/minimap.ts    Bottom-right <canvas> overlay: static map image cached in
                  an offscreen canvas once; each frame it blits the window
                  around the camera (minimap.worldFraction of the world,
                  clamped to bounds) + a white viewport strokeRect clipped to
                  that window. Not a three.js scene.
src/main.ts       Wiring: load map → renderer/scene → boat → controllers →
                  rAF loop (mode toggle → boat → camera update → clamp →
                  apply → grid opacity → render → minimap/hud). Resize
                  handler recomputes minZoom and re-clamps.
```

### Camera modes

`CameraState` is `{centerX, centerY, zoom}` where **zoom = screen px per world unit** (the single zoom source of truth; `minZoom` = whole-world-fits, recomputed on resize; `maxZoom` in config). To add a camera mode (e.g. follow-a-unit, scripted):

1. Implement `CameraController` (`name`, `enable(state)`, `disable()`, `update(dt, input, view)`, `getState()`).
2. Register it in the `controllers` record in `main.ts`.
3. Call `switchMode(name)` — it hands the current `CameraState` to the new mode so transitions are seamless.

`C` cycles `free` → `follow` → `dynamic` (handled in `main.ts` via
`input.pressed` and `MODE_CYCLE`). WASD is shared: it pans in free mode and
drives the boat in the boat-following modes, and `main.ts` only calls
`boat.update()` outside free mode, so the two never fight over the keys.

The `dynamic` mode also owns the boat's top speed while it's active: it zooms
in near cities (`dynamicZoom.nearZoom` inside `nearDistance` of a city centre,
`farZoom` past `farDistance`, eased with a `smoothing` time constant) and
scales `boat.maxSpeedScale` from 1 at `farZoom` down to `slowFactor` at
`nearZoom`, so zoomed-in travel isn't a blur. Any other mode resets the scale
to 1. Its look-ahead offsets the centre along the boat's heading by up to
`lookAheadPx` **screen** px (so it reads the same at any zoom), scaled by the
fraction of top speed in use and eased with its own slower time constant.

Controllers only mutate their own state; `main.ts` clamps (`clampCamera`) and applies to the actual camera after every update, so modes never need bounds logic. Clamping rule: per axis, if the view is wider than the world, lock centered (letterbox); else clamp center to `[half, worldSize - half]`.

### Rendering notes

- The whole terrain is one draw call. Don't create per-tile meshes — for gameplay entities, add new meshes/instances on top (terrain sits at z=0; camera looks down -z from z=10, near 0.1 / far 100).
- The grid is drawn in the terrain fragment shader using `fract(worldPos / uTileScale)` + `fwidth()` for constant ~1px lines; it fades in between `grid.fadeMinPxPerTile` and `fadeMaxPxPerTile` screen px per tile (config).
- The terrain `DataTexture` is deliberately `NoColorSpace`: the raw ShaderMaterial skips three.js sRGB output encoding, so an untagged texture passes hex colors through 1:1 and matches the minimap's 2D canvas. If you switch to a built-in material, revisit this or colors will shift.
- Wheel zoom is exponential and anchored at the cursor (the world point under the cursor stays fixed). Keyboard pan speed is in screen px/sec (`keyPanPxPerSec / zoom` world units), so panning feels the same at any zoom.

## Conventions

2-space indent, semicolons, single quotes, named exports, no classes except camera controllers, no frameworks. Tunables belong in `config.ts`, not inline. The sibling `../world-generator/CLAUDE.md` describes the same style. Keep `dmap.ts` free of three.js imports.
