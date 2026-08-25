# Terrain Editor

Browser-based terrain painter built on Three.js + Bun. Two views side by side:

- **3D view (left, view only)** — LOD-rendered terrain; orbit / pan / zoom.
  It refreshes automatically ~1 s after you stop painting, so editing never
  waits on it. A **voxel 3D** checkbox renders every map cell as a
  flat-topped column with vertical cliffs between different heights (what a
  voxel engine would show), still LOD-ed.
- **2D map (right)** — overhead map with pan/zoom and circular brushes.
  - **Tiles mode**: paint the 5 tile types (water, sand, grass, forest,
    rock) with a hard-edged circular brush. *Clear* fills the whole map with
    the selected tile.
  - **Height mode**: map turns greyscale; *Raise* / *Lower* brushes have a
    soft falloff (strong in the middle, fading to the rim) so terrain stays
    smooth; *Smooth* blends toward the neighborhood average. *Clear*
    flattens to 0.
  - Adjustable brush size (slider or `[` / `]`) and strength; per-stroke
    **undo / redo** (`Cmd/Ctrl+Z`, `Shift+Cmd/Ctrl+Z`).

The map is a square power of two (8192² by default, selectable 512–8192 in
the toolbar) and starts flat: height 1 everywhere, every tile water. Heights are clamped to 1…100.
Changing resolution resamples what you painted — heights are bilinearly
interpolated up / block-averaged down, tiles use nearest up / most-common
tile down. Undo history is cleared on resize.

## Save / load

**Save** (or `Cmd/Ctrl+S`) downloads `map-<size>.terrain`, a gzipped binary
file; **Load** opens one (raw or gzipped) and replaces the map, switching
resolution to the file's. Format (`src/core/fileFormat.ts`, little-endian):

| offset | type | field |
|---|---|---|
| 0 | `char[4]` | magic `TGN1` |
| 4 | `u32` | size (pixels per side, power of two) |
| 8 | `u32` | flags (reserved, 0) |
| 12 | `f32` | worldSize |
| 16 | `f32[size²]` | heights, row-major, world units |
| 16 + 4·size² | `u8[size²]` | tile ids (0 water, 1 sand, 2 grass, 3 forest, 4 rock) |

Uncompressed that is 5 bytes per cell (320 MB at 8192²); gzip shrinks flat
or repetitive maps dramatically.

## Run

```sh
bun install
bun run dev     # http://localhost:3000
bun test        # unit tests for src/core
```

## Docs

- `docs/ARCHITECTURE.md` — data flow, brushes, undo, rendering/LOD.
- `AGENTS.md` — orientation for coding agents.
