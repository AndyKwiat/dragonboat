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
  - **Markers mode**: click empty map to drop a labelled circle (default
    radius 10 map px, radius slider or `[` / `]`); it prompts for a **tag**
    (required, max 100 chars). Click an existing marker to edit its tag
    (clearing the tag deletes it), drag it to move it. *Clear* removes every
    marker. Markers stay visible (dimmed) in the other two modes.
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
| 0 | `char[4]` | magic / version: `TGN1` = v1, `TGN2` = v2 (current) |
| 4 | `u32` | size (pixels per side, power of two) |
| 8 | `u32` | flags (reserved, 0) |
| 12 | `f32` | worldSize |
| 16 | `f32[size²]` | heights, row-major, world units |
| 16 + 4·size² | `u8[size²]` | tile ids (0 water, 1 sand, 2 grass, 3 forest, 4 rock) |
| 16 + 5·size² | `u32` | **v2:** marker count |
| … | per marker | **v2:** `f32 x, f32 y, f32 r, u16 tagLen, u8[tagLen]` UTF-8 tag |

The pixel layers are 5 bytes per cell (320 MB at 8192² uncompressed); gzip
shrinks flat or repetitive maps dramatically. **v1 files still load** (as a
map with no markers) and are re-saved as v2. Full spec, including reader /
writer checklists for other codebases: [`docs/FILE_FORMAT.md`](docs/FILE_FORMAT.md).

## Run

```sh
bun install
bun run dev     # http://localhost:3000
bun test        # unit tests for src/core
```

## Docs

- `docs/ARCHITECTURE.md` — data flow, brushes, undo, rendering/LOD.
- `docs/FILE_FORMAT.md` — the `.terrain` binary format spec (versioned).
- `AGENTS.md` — orientation for coding agents.
