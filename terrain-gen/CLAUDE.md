# Agent notes

Browser terrain *painter*. Bun + Three.js + TypeScript, no framework.

## Commands
- `bun install` · `bun run dev` (http://localhost:3000) · `bun test` · `bunx tsc --noEmit`

## Map of the code
- `src/core/` — pure, DOM-free. **All unit tests target this.**
  - `paint.ts` brushes (tile disc, soft height, smooth) · `history.ts` chunked undo/redo + whole-list marker entries · `resample.ts` resolution changes · `fileFormat.ts` binary .terrain save/load (TGN1 read, TGN2 read/write) · `markers.ts` labelled circles layer
  - `types.ts` Tile ids + palette
- `src/render/terrainRenderer.ts` — Three.js texture-displaced quadtree LOD (view only)
- `src/ui/mapView2d.ts` — 2D canvas: pan/zoom, dirty-region raster, stroke events
- `src/ui/toolbar.ts` — DOM toolbar (mode, swatches, sliders, undo/redo/clear)
- `src/main.ts` — `App`: stroke stamping, history wiring, debounced 3D upload. `window.app` for console poking.
- `docs/ARCHITECTURE.md` explains the design; `docs/FILE_FORMAT.md` is the
  versioned `.terrain` spec (point other repos at it).

## Rules
- Mutations go: `recorder.touch(bbox)` → paint op → `map.markDirty(bbox)` → `touch3d()`.
- The 3D view must never block 2D painting: it only refreshes via the 1 s debounce in the render loop.
- Keep `src/core` pure; add tests for new core code. Run `bun test` before committing.
- Update this file + docs when structure changes. Any file-format change =
  new magic (`TGN3`…), section appended at the end, old versions still
  readable, `docs/FILE_FORMAT.md` updated.
