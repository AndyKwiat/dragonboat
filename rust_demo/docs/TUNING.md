# Tuning the demo

All knobs live in `src/config.rs`. The world is deterministic, so any change
is reproducible.

## More/fewer visible voxels (zoom)

Change `TARGET_VISIBLE_VOXELS` (default 200.0).

```text
smaller value = camera appears closer (fewer, larger voxels)
larger value = camera appears farther away (more, smaller voxels)
```

Implemented in `camera.rs::viewport_height_for_visible_voxels` — the
orthographic viewport height is `target * sin(elevation)`.

## More movers

Change `MOVER_COUNT` (default 15000; ~56-69 FPS measured in browser WebGPU on an M-series Mac). Suggested test values:

```text
100
500
1,000
2,000
5,000
```

Speeds: `MOVER_MIN_SPEED` / `MOVER_MAX_SPEED` (world units ≈ voxels per second).

## Terrain roughness

In `world.rs::generate_world`:

- `base = fbm(seed, fx * 0.02, fz * 0.02)` — multiply `0.02` for smaller/larger
  features; the `* 3.5` amplitude sets local relief.
- `hills = fbm(... * 0.008)` with `* 6.0` amplitude — broad hill height.
- `- 1.2` — global lowering; raise to get more water, lower for less.
- `TERRAIN_MAX_HEIGHT` in config.rs caps height (default 8).
- `WATER_LEVEL` sets which heights flood (default 1).

## Terrain colors

Palette: `terrain.rs::cell_color` (one RGB per terrain type + shade math).
Terrain-type assignment (what counts as sand/rock/dry grass): the end of
`world.rs::generate_world`. Side-face darkening factor: `darken(top, 0.72)`.

## Camera angle

`CAMERA_AZIMUTH_DEGREES` (default 45.0) and `CAMERA_ELEVATION_DEGREES`
(default 38.0). The camera is intentionally fixed; there are no runtime
controls.

## World seed

`WORLD_SEED` (default 12345). Same seed → identical map, decoration placement,
and mover spawns. Change it to regenerate everything.

## World size

`WORLD_WIDTH` / `WORLD_DEPTH` (default 256 × 256). Mesh generation is
O(width × depth) at startup and chunk count grows accordingly
(size/16 squared meshes). 512 × 512 is fine; much beyond that will slow
startup and add draw calls. Keep both a multiple of `TERRAIN_CHUNK_SIZE`.

## Decorations

`TREE_COUNT`, `ROCK_COUNT`, `HOUSE_COUNT` in config.rs; placement rules in
`terrain.rs::spawn_decorations`.
