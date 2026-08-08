# Dragon Boat - Island Map

## Project Overview
A Three.js 2D island map rendered on a 1000x1000 pixel canvas, served via Vite with hot module reload.

## Tech Stack
- **Three.js** for rendering (orthographic camera, 2D top-down view)
- **Vite** for dev server with HMR

## Running
```bash
npm run dev    # starts Vite dev server at localhost:5173
npm run build  # production build to dist/
```

## Architecture

### Files
- `index.html` — entry point, hosts the 1000x1000 map container
- `src/main.js` — sets up Three.js scene, camera, renderer, and HMR wiring
- `src/map.js` — all map generation logic (island, mountains, rivers, lakes, canals)

### How HMR works
`src/main.js` accepts hot updates from `src/map.js`. When `map.js` changes, it clears the scene and calls `createMap()` again. This means map generation changes appear instantly without a browser refresh.

### Map generation (`src/map.js`)
All geometry is built in the `createMap(scene)` function using these layers (z-order):

1. **Ocean** (z=0) — blue PlaneGeometry background
2. **Beach** (z=0.05) — irregular ring using `makeIrregularShape()`, 2px wide
3. **Island** (z=0.1) — green irregular circle, radius ~350px with ±15% noise
4. **Rivers** (z=0.15) — 7 rivers from mountains to coast, built as triangle strip meshes. Width varies 2-20px, wider at coast. 3 biggest rivers have tributaries branching off
5. **Lake** (z=0.15) — irregular blob on one river
6. **Canals** (z=0.16) — straight 2px dark blue lines connecting adjacent rivers
7. **Mountains** (z=0.2) — grey irregular circle in center, radius ~120px
8. **Cities** (z=0.25) — 20 red 5x5 pixel squares placed beside rivers, spaced apart

### Key patterns
- `noisyRadius(baseRadius, angle, seed)` — generates smooth irregular outlines by summing sine waves at different frequencies
- `makeIrregularShape(baseRadius, segments, seed)` — creates a THREE.Shape with noisy radius for islands, mountains, lakes
- `buildRiverPoints(...)` — generates meandering river centerline points with width
- `renderRiverMesh(points, z)` — builds a triangle strip mesh from river centerline points
- Rivers, tributaries, and canals all connect to actual computed point positions, not hardcoded coordinates
