# Dragon Boat - Island Map

## Project Overview
A Three.js 2D island map rendered on a 1000x1000 pixel canvas, served via Vite with hot module reload. Designed for iterative world-building with an LLM.

## Tech Stack
- **Three.js** for rendering (orthographic camera, 2D top-down view)
- **Vite** for dev server with HMR

## Running
```bash
npm run dev    # starts Vite dev server at localhost:5173
npm run build  # production build to dist/
```

## Architecture

The project separates **config**, **data generation**, and **rendering** into three files:

### Files
- `src/config.js` — **all tunable parameters** (radii, seeds, river/canal/lake/city definitions, colors). Edit this file to change the map. This is the primary interface for LLM-driven iteration.
- `src/geometry.js` — **pure data generation**, no Three.js dependency. Takes config, produces arrays of points and outlines. Testable independently.
- `src/map.js` — **rendering layer**. Takes generated data, creates Three.js meshes. Also handles index-mode rendering for export.
- `src/main.js` — scene setup, HMR wiring, and binary export logic.
- `index.html` — entry point with export button.

### How HMR works
`src/main.js` accepts hot updates from `config.js`, `geometry.js`, and `map.js`. Any change to these files triggers a full re-render without browser refresh.

### Config-driven design
Common changes map to `src/config.js` edits:
- **City count**: `cities.count`
- **Reshuffle canals**: change `canals[].riverA/riverB/tA/tB`
- **Add a lake**: append to `lakes[]`
- **River properties**: edit `rivers[]` entries
- **Colors**: `colors.*`

### Map layers (z-order)
1. **Ocean** (z=0) — blue background
2. **Beach** (z=0.05) — irregular coastline border, 2px wide
3. **Island** (z=0.1) — irregular circle with forest/grassland shader (denser forest near center)
4. **Rivers** (z=0.15) — 7 main rivers + 6 tributaries, width 2-20px
5. **Lakes** (z=0.15) — 2 irregular blobs on rivers
6. **Canals** (z=0.16) — 3 straight 2px dark blue lines connecting adjacent main rivers
7. **Mountains** (z=0.2) — grey irregular circle in center
8. **Cities** (z=0.25) — 100 red 5x5 squares beside waterways, round-robin distributed

### Key data generation functions (`geometry.js`)
- `generateIrregularOutline(radius, segments, seed)` — noisy circle outline points
- `generateRiverPoints(...)` — meandering river centerline with width
- `generateTributaryPoints(riverPoints, trib)` — branch off a river
- `generateCityPositions(waterways, canals, config)` — round-robin placement with interleaved spatial distribution
- `generateMapData(config)` — orchestrates all generation, returns complete map data

### Export
The "Export" button renders the map in index mode (each terrain type → unique color in red channel) to an offscreen target, reads pixels back, and downloads a `.dmap` binary. See `map-format.md` for the format spec.

### Important: main rivers vs all waterways
`mainRiverPoints[]` has exactly 7 entries (indices 0-6 matching `config.rivers[]`). Canals reference these indices. `allWaterwayPoints[]` includes tributaries and is used for city placement. This separation prevents index confusion.
