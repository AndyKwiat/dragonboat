# Architecture

For a new engineer (or agent) joining the project.

## 1. App startup

`src/main.rs` builds the Bevy `App`: `DefaultPlugins` with a customized
`WindowPlugin` (attaches to the `#bevy-canvas` element on the web and tracks
its size via `fit_canvas_to_parent`), then five local plugins:

```rust
WorldPlugin      // generates terrain data (a Resource) at plugin build time
TerrainPlugin    // Startup: builds chunk meshes + decorations
MoversPlugin     // Startup: spawns movers; Update: moves them
CameraPlugin     // Startup: camera + sun + ambient light
OverlayPlugin    // FPS/frame overlay (diagnostics.rs)
```

## 2. Data flow

```text
World seed (config.rs)
   |
   v
WorldGrid (world.rs) ----------> mover height queries (movers.rs)
   |                       \---> decoration placement (terrain.rs)
   v
Terrain chunk builder (terrain.rs)
   |
   v
Bevy Mesh assets (one per 16x16 chunk)
   |
   v
Bevy renderer (wgpu)
   |
   v
WebGPU (browser) / Metal-Vulkan-DX12 (native)
   |
   v
Canvas / window
```

## 3. Coordinate system

- X/Z ground plane, Y up. 1 voxel = 1.0 world unit.
- Cell (x, z) covers world [x, x+1) × [z, z+1); top face at y = height.
- World center is (128, 0, 128); the camera orbits that point conceptually.

## 4. World generation (`world.rs`)

Deterministic, seeded by `WORLD_SEED`:

- **SplitMix64** hash → per-coordinate random values (no RNG crate).
- **Value noise** (bilinear-interpolated hashed lattice) layered as 3-octave
  fBm: a base layer (freq 0.02) plus broad hills (freq 0.008).
- A radial basin is subtracted near (70, 170) to guarantee a lake.
- Heights are rounded and clamped to 0..=TERRAIN_MAX_HEIGHT (8).
- Cells at/below `WATER_LEVEL` become Water (stored height = water surface);
  one step above is Sand; the top heights are Rock; a moisture noise field
  picks DryGrass vs Grass; a per-cell roll sprinkles Earth.

## 5. Terrain cell data

```rust
TerrainCell { height: u8, terrain_type: TerrainType, color_variant: u8 }
WorldGrid   { cells: Vec<TerrainCell> }   // index = z * WORLD_WIDTH + x
```

Lookup helpers: `cell(x, z)`, `height_at(f32, f32)`, `is_water(f32, f32)`.

## 6. Chunk mesh generation (`terrain.rs`)

Per 16 × 16 chunk (256 chunks total):

1. Top quad per cell at its height.
2. Compare against the 4 neighbors; emit vertical side quads down to any
   lower neighbor (out-of-bounds counts as height 0, walling the world edge).
3. No bottom faces, no faces between equal-height cells.
4. Flat normals, hard edges — voxels read as blocks, not smoothed terrain.

## 7. Terrain coloring

Per-vertex colors on a single shared white `StandardMaterial`
(roughness 1.0, reflectance 0). `cell_color()` mixes: base palette by terrain
type, ±8% per-voxel brightness from `color_variant`, +12% toward higher
ground. Side faces are darkened ×0.72 for cheap edge definition. This is what
makes individual voxels legible at 5 px/voxel.

## 8. Moving entities (`movers.rs`)

15,000 (configurable) "people": one flat entity per mover holding a `Mover`
component (heading, speed, retarget timer, inline u64 rng state) and a merged
body+head mesh — 5 vertex-colored mesh variants (one per palette color, skin
color baked in) sharing a single white material, so Bevy's automatic GPU
instancing draws all movers as 5 instanced batches. `NoFrustumCulling` skips
per-entity culling (they're tiny and everywhere). The Update system: advance
heading timer → step position → bounce at world margin → reverse at water →
snap smoothly to `height_at` → face travel direction. No per-frame
allocations. This flat/instanced layout replaced a parent+2-children design
and took 15k movers from ~65 to ~95 FPS in browser WebGPU.

## 9. Camera / framing (`camera.rs`)

Fixed orthographic camera. Position = world center + spherical offset
(azimuth 45°, elevation 38°, distance 400) looking at the center.
Framing: `ScalingMode::FixedVertical { viewport_height }` where
`viewport_height = TARGET_VISIBLE_VOXELS * sin(elevation)` — looking down at
angle e foreshortens ground depth by sin(e), so this makes roughly 200 ground
cells span the viewport height at any window size (resize-safe by
construction). Near/far are widened (-600/1200) to keep the whole diagonal
world in clip range.

Lighting: one directional sun (shadow maps on) + ambient fill on the camera.

## 10. Diagnostics (`diagnostics.rs`)

`FrameTimeDiagnosticsPlugin` + a single UI `Text` node (top-left) refreshed
every frame with FPS / frame ms / mover count / world size. Shows `--` until
diagnostics warm up.

## 11. Browser/WASM build path

`cargo run --target wasm32-unknown-unknown --features webgpu` → cargo invokes
`wasm-server-runner` (configured in `.cargo/config.toml`) → it runs
wasm-bindgen and serves the module on http://127.0.0.1:1334 using
`web/index.html` (selected via `WASM_SERVER_RUNNER_CUSTOM_INDEX_HTML` in
`run-web.sh`). The page hosts the canvas, shows a WebGPU fallback message,
auto-reloads on rebuild, and forwards browser console output to the terminal.

## 12. Native build path

`cargo run` — same code, Bevy picks the platform backend. No wasm-specific
logic exists outside `main.rs` window config (canvas selector is ignored
natively) and the `webgpu` cargo feature.

## 13. Performance considerations

- 256 chunk meshes ≈ 256 terrain draw calls; hidden faces never generated
  (~10× fewer quads than naive cubes).
- Shared meshes/materials for movers and decorations keep asset counts tiny.
- Movement loop is allocation-free; RNG is inline integer hashing.
- If thousands more movers are needed, instancing is the next lever — do not
  reach for it before measuring.
