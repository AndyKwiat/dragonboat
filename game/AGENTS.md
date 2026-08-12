# dragonboat/game

A top-down 2D world viewer (the start of a game) built with TypeScript, three.js, and Vite. It loads a world exported from the sibling `../world-generator` project and lets you explore it with a free camera.

## Running

```bash
npm install
npm run dev        # vite dev server at http://localhost:5173
npm run build      # tsc --noEmit + vite build
npx tsc --noEmit   # typecheck only
```

No test suite yet. Verify changes by running the app and looking at it.

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
src/input.ts      Single InputState shared by all controllers: key set, drag
                  deltas, wheel delta, cursor pos. Deltas accumulate between
                  frames; the active controller reads them then calls
                  consumeFrameDeltas(). Don't add per-controller listeners.
src/camera/CameraController.ts   The camera-mode seam (see below) + pure
                  helpers: clampCamera, minZoomFor, viewRect, applyToCamera.
src/camera/FreeCameraController.ts  The only mode so far.
src/hud.ts        Top-right overlay: view TL/BR corners (clamped to world
                  bounds) + zoom. Plain DOM, updates via textContent diff.
src/minimap.ts    Bottom-right <canvas> overlay: static map image cached in
                  an offscreen canvas once, blit + white viewport strokeRect
                  each frame. Not a three.js scene.
src/main.ts       Wiring: load map → renderer/scene → controllers → rAF loop
                  (update → clamp → apply → grid opacity → render → minimap/
                  hud). Resize handler recomputes minZoom and re-clamps.
```

### Camera modes

`CameraState` is `{centerX, centerY, zoom}` where **zoom = screen px per world unit** (the single zoom source of truth; `minZoom` = whole-world-fits, recomputed on resize; `maxZoom` in config). To add a camera mode (e.g. follow-a-unit, scripted):

1. Implement `CameraController` (`name`, `enable(state)`, `disable()`, `update(dt, input, view)`, `getState()`).
2. Register it in the `controllers` record in `main.ts`.
3. Call `switchMode(name)` — it hands the current `CameraState` to the new mode so transitions are seamless.

Controllers only mutate their own state; `main.ts` clamps (`clampCamera`) and applies to the actual camera after every update, so modes never need bounds logic. Clamping rule: per axis, if the view is wider than the world, lock centered (letterbox); else clamp center to `[half, worldSize - half]`.

### Rendering notes

- The whole terrain is one draw call. Don't create per-tile meshes — for gameplay entities, add new meshes/instances on top (terrain sits at z=0; camera looks down -z from z=10, near 0.1 / far 100).
- The grid is drawn in the terrain fragment shader using `fract(worldPos / uTileScale)` + `fwidth()` for constant ~1px lines; it fades in between `grid.fadeMinPxPerTile` and `fadeMaxPxPerTile` screen px per tile (config).
- The terrain `DataTexture` is deliberately `NoColorSpace`: the raw ShaderMaterial skips three.js sRGB output encoding, so an untagged texture passes hex colors through 1:1 and matches the minimap's 2D canvas. If you switch to a built-in material, revisit this or colors will shift.
- Wheel zoom is exponential and anchored at the cursor (the world point under the cursor stays fixed). Keyboard pan speed is in screen px/sec (`keyPanPxPerSec / zoom` world units), so panning feels the same at any zoom.

## Conventions

2-space indent, semicolons, single quotes, named exports, no classes except camera controllers, no frameworks. Tunables belong in `config.ts`, not inline. The sibling `../world-generator/CLAUDE.md` describes the same style. Keep `dmap.ts` free of three.js imports.
