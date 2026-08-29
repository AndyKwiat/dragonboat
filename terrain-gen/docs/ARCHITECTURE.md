# Architecture

## Data

Two flat arrays are the whole document, mutated in place by painting:

- `height: Float32Array(size*size)` — world units, clamped to [`HEIGHT_MIN`=1, `HEIGHT_MAX`=100] by every brush
- `tiles: Uint8Array(size*size)` — `Tile` ids (see `src/core/types.ts`)

Plus a third, non-pixel layer:

- `markers: Marker[]` — labelled circles (`{x, y, r, tag}` in map px; see
  `src/core/markers.ts`). Not rasterized: they are drawn as an overlay by
  the 2D view and stored as their own section in the file. Tags are
  mandatory, 1–100 chars, whitespace-collapsed.

Both the 2D view and the 3D textures reference these same arrays; nothing
is copied on edit.

## Painting (`src/core/paint.ts`, pure)

- `paintTiles` — hard-edged disc.
- `paintHeight` — adds `delta * falloff(d/r)` with `falloff = (1-t²)²`
  (1 at center → 0 at rim), which is why raised terrain comes out smooth.
- `smoothHeight` — each pixel inside the disc blends toward the mean of a
  (2k+1)² window (k = r/5) weighted by the same falloff; the means come from
  a summed-area table over the padded region, so cost is O(pixels) no matter
  the window size. Strength 100 = full replacement at the brush center.
- `App.stampPath` interpolates stamps along the pointer path at ~r/4
  spacing so fast strokes don't leave gaps.

## Markers (`src/core/markers.ts`, pure)

`hitMarker` picks the topmost (last-placed) marker whose disc contains a
point, with a `minHit` floor so markers smaller than ~6 screen px stay
clickable when zoomed out. `resampleMarkers` scales x/y/r on resolution
change. `App.onMarkerStroke` drives the interaction: click empty map =
place (`prompt` for the tag), click a marker = edit its tag (empty =
delete), drag = move. Every one of those commits a `markerEntry` to the
same history stack the brushes use.

## Undo / redo (`src/core/history.ts`, pure)

Chunked snapshots: a `StrokeRecorder` copies each 64×64 chunk the *first*
time a stroke touches it (`touch()` before mutating), then on `commit()`
captures "after" and drops unchanged chunks. `History` keeps undo/redo
stacks of these entries with a byte cap (256 MB, oldest dropped). *Clear* is
just a stroke that touches every chunk. Undoing an entry from the other
layer switches the 2D view to that layer so the change is visible.

Marker edits do not fit the chunk model (the list is tiny and unindexed),
so a `MarkerEntry` snapshots the whole before/after list. `History` holds
both entry kinds; `undo`/`redo` take `{tiles, height, markers}` and mutate
the marker array **in place** so the App's reference stays valid.

## 2D view (`src/ui/mapView2d.ts`)

A map-sized backing `<canvas>` holds the rasterized image (tile palette or
greyscale heights over the full 1…100 range, with the tile colors ghosted
underneath at 30% so borders stay visible while sculpting). Brushes report a dirty bbox;
only that region is re-rasterized (`putImageData`) on the next frame. The
visible canvas draws the backing canvas under a pan/zoom transform (nearest
sampling when zoomed in) plus the brush cursor ring.

Markers mode reuses the tile raster and draws markers on top every frame:
discs in map space, labels in screen space (constant 12 px) so they stay
legible at any zoom. Markers are dimmed in tiles/height mode, and the
hovered one is highlighted.

## 3D view (`src/render/terrainRenderer.ts`)

Unchanged from the generator era: height (R32F) + tile (R8) DataTextures,
a quadtree of pooled 32×32 skirted patches displaced in the vertex shader
(skirts drop to an absolute floor below `HEIGHT_MIN`, so LOD cracks are
covered no matter how steep the neighboring patch is),
palette lookup + height-derived normals in the fragment shader. Map size
must be a power of two.

**Voxel mode** (toolbar checkbox → `TerrainRenderer.setVoxel`): the pool
swaps to a second shared patch geometry with two vertices per cell per
axis, placed at the cell boundaries pulled in by a tiny eps. An `aCell`
attribute carries each vertex's cell center; in voxel mode the vertex
shader samples the height at that cell's texel (snapped to the texel
center), so the cell top is flat and the 2·eps strip between neighboring
cells becomes a vertical wall as tall as the height difference. The
fragment shader switches to flat shading from `dFdx/dFdy` so walls and tops
light correctly. LOD is unchanged: a coarser patch's cell spans several
texels and renders as one bigger block (height of its center texel), so
distant terrain is cheap and the finest level is exactly one block per map
cell. Vertex cost ≈ 4× the smooth patch (64² vs 33² per patch).

**Sync**: every stamp sets `dirty3d`; the render loop re-uploads both
textures once `DEBOUNCE_3D_MS` (1 s) has passed since the last stamp. There
is no CPU generation anymore, so the upload (tens of ms) is the whole cost —
no worker needed. If procedural generation returns, run it in a worker and
feed its result through the same `upload3d()`.

## Startup & resolution

The map starts flat (`BASE_HEIGHT` = 1, all water); default 8192². There is
no procedural generation in the codebase anymore.

`src/core/resample.ts` handles resolution changes (powers of two, so the
ratio is an integer): heights bilinear up / block-average down; tiles
nearest up / block-mode (most common id) down. `App.resize()` swaps the
arrays (the 2D view reads them through getters), clears history (chunk
indices are per-resolution), resets the backing canvas and uploads to 3D
immediately (the renderer recreates textures when the size changes).

## File format (`src/core/fileFormat.ts`, pure)

`encodeTerrain` / `decodeTerrain` produce/parse the raw binary. **The magic
is the version**: `TGN1` (heights + tiles) and `TGN2` (those, plus an
appended marker section). `decode` reads both — a v1 file yields an empty
marker list — and `encode` always writes v2, so loading an old map and
saving upgrades it. `decode` validates magic, power-of-two size, exact byte
length, per-marker bounds, tag UTF-8 and radius. The full spec lives in
`docs/FILE_FORMAT.md` (written to be handed to other repos); keep it in
sync when the format changes, and append new sections behind a new magic
rather than reordering old ones. `gzip`/`gunzip` wrap the web-streams
`CompressionStream`, used by `App.save`/`App.load`; load sniffs the gzip
magic so both raw and compressed files open. Loading replaces the arrays
and resets history/views the same way a resolution change does.

## Invariants

- `src/core` is pure and DOM-free; keep new logic there with tests.
- Always `touch()` the recorder before mutating, and `markDirty()` +
  `touch3d()` after — otherwise undo or the views desync.
