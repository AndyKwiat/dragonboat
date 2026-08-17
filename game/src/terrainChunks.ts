import * as THREE from 'three';
import { CONFIG, TILE_SCALE } from './config';
import type { DMap } from './dmap';

// Streamed 3D terrain: the map is cut into chunks of cellsPerChunk×cellsPerChunk
// columns, each chunk one mesh (top face + only the exposed side faces of
// every raised column; water columns at height 0 emit nothing — the flat 2D
// terrain plane under everything is the water). Chunks are built on demand
// around the camera within a per-frame CPU budget and cached; when the
// visible area needs more chunks than maxVisibleChunks, the LOD step doubles
// (each column covers step×step map cells, majority terrain) so the whole
// world is always covered by a bounded number of meshes.

interface Chunk {
  key: string;
  mesh: THREE.Mesh | null;  // null when the chunk has no raised columns
  lastUsed: number;
}

export interface TerrainChunks {
  group: THREE.Group;
  /** Called once per frame with the visible ground bounds in world units. */
  update(bounds: { left: number; top: number; right: number; bottom: number }, focusX: number, focusY: number): void;
  stats(): { visible: number; cached: number; step: number; pending: number };
}

export function createTerrainChunks(map: DMap): TerrainChunks {
  const C = CONFIG.chunks.cellsPerChunk;
  const heights = CONFIG.terrainHeights;
  const colors = CONFIG.terrainColors.map((hex) => new THREE.Color(hex));
  const material = new THREE.MeshBasicMaterial({ vertexColors: true, side: THREE.DoubleSide });

  const group = new THREE.Group();
  const cache = new Map<string, Chunk>();
  let frame = 0;
  let currentStep = 1;
  let pending = 0;

  // Majority terrain over a step×step block of map cells (top-left cell cx, cy).
  const counts = new Uint16Array(CONFIG.terrainColors.length);
  function blockType(cx: number, cy: number, step: number): number {
    if (cx < 0 || cy < 0 || cx >= map.width || cy >= map.height) return -1;
    if (step === 1) return map.data[cy * map.width + cx]!;
    counts.fill(0);
    const x1 = Math.min(cx + step, map.width);
    const y1 = Math.min(cy + step, map.height);
    for (let y = cy; y < y1; y++) {
      const row = y * map.width;
      for (let x = cx; x < x1; x++) counts[map.data[row + x]!] = counts[map.data[row + x]!]! + 1;
    }
    let best = 0;
    for (let i = 1; i < counts.length; i++) if (counts[i]! > counts[best]!) best = i;
    return best;
  }

  const shadeX = CONFIG.chunks.sideShadeX;
  const shadeY = CONFIG.chunks.sideShadeY;

  function buildChunk(ci: number, cj: number, step: number): THREE.Mesh | null {
    const cellW = TILE_SCALE * step;
    const x0 = ci * C;   // in LOD-cell units
    const y0 = cj * C;
    // Types with a 1-cell border so side-face exposure is known at the edges.
    const N = C + 2;
    const types = new Int16Array(N * N);
    for (let j = 0; j < N; j++) {
      for (let i = 0; i < N; i++) {
        types[j * N + i] = blockType((x0 + i - 1) * step, (y0 + j - 1) * step, step);
      }
    }
    const hOf = (i: number, j: number) => {
      const t = types[j * N + i]!;
      return t < 0 ? 0 : heights[t]!;
    };

    const positions: number[] = [];
    const cols: number[] = [];
    const indices: number[] = [];
    let v = 0;
    const quad = (
      ax: number, ay: number, az: number, bx: number, by: number, bz: number,
      cx: number, cy: number, cz: number, dx: number, dy: number, dz: number,
      color: THREE.Color, shade: number,
    ) => {
      positions.push(ax, ay, az, bx, by, bz, cx, cy, cz, dx, dy, dz);
      const r = color.r * shade, g = color.g * shade, b = color.b * shade;
      cols.push(r, g, b, r, g, b, r, g, b, r, g, b);
      indices.push(v, v + 1, v + 2, v, v + 2, v + 3);
      v += 4;
    };

    for (let j = 0; j < C; j++) {
      for (let i = 0; i < C; i++) {
        const t = types[(j + 1) * N + (i + 1)]!;
        if (t < 0) continue;
        const h = heights[t]!;
        if (h <= 0) continue;
        const color = colors[t]!;
        const wx0 = (x0 + i) * cellW, wx1 = wx0 + cellW;
        const wy0 = (y0 + j) * cellW, wy1 = wy0 + cellW;
        // Top.
        quad(wx0, wy0, h, wx1, wy0, h, wx1, wy1, h, wx0, wy1, h, color, 1);
        // Sides, only down to the neighbour's height where it is lower.
        const hw = hOf(i, j + 1), he = hOf(i + 2, j + 1), hn = hOf(i + 1, j), hs = hOf(i + 1, j + 2);
        if (hw < h) quad(wx0, wy0, hw, wx0, wy1, hw, wx0, wy1, h, wx0, wy0, h, color, shadeX);
        if (he < h) quad(wx1, wy0, he, wx1, wy1, he, wx1, wy1, h, wx1, wy0, h, color, shadeX);
        if (hn < h) quad(wx0, wy0, hn, wx1, wy0, hn, wx1, wy0, h, wx0, wy0, h, color, shadeY);
        if (hs < h) quad(wx0, wy1, hs, wx1, wy1, hs, wx1, wy1, h, wx0, wy1, h, color, shadeY);
      }
    }
    if (v === 0) return null;

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setAttribute('color', new THREE.Float32BufferAttribute(cols, 3));
    geometry.setIndex(indices);
    const mesh = new THREE.Mesh(geometry, material);
    mesh.frustumCulled = false; // we do our own visibility, and the -y group scale confuses bounds
    return mesh;
  }

  function evict() {
    const max = CONFIG.chunks.maxCachedChunks;
    if (cache.size <= max) return;
    const sorted = [...cache.values()].sort((a, b) => a.lastUsed - b.lastUsed);
    for (let i = 0; i < cache.size - max; i++) {
      const chunk = sorted[i]!;
      if (chunk.lastUsed === frame) break;
      if (chunk.mesh) {
        group.remove(chunk.mesh);
        chunk.mesh.geometry.dispose();
      }
      cache.delete(chunk.key);
    }
  }

  let visibleCount = 0;

  return {
    group,

    update(bounds, focusX, focusY) {
      frame++;
      const pad = CONFIG.chunks.boundsPadCells * TILE_SCALE;
      const worldW = map.width * TILE_SCALE;
      const worldH = map.height * TILE_SCALE;
      const left = Math.max(0, bounds.left - pad);
      const top = Math.max(0, bounds.top - pad);
      const right = Math.min(worldW, bounds.right + pad);
      const bottom = Math.min(worldH, bounds.bottom + pad);

      // Coarsest-necessary LOD: the smallest step whose chunk count fits.
      let step = 1;
      let chunkW = C * TILE_SCALE;
      let ci0 = 0, ci1 = 0, cj0 = 0, cj1 = 0;
      for (;;) {
        chunkW = C * TILE_SCALE * step;
        ci0 = Math.floor(left / chunkW);
        ci1 = Math.min(Math.ceil(right / chunkW), Math.ceil(map.width / (C * step))) - 1;
        cj0 = Math.floor(top / chunkW);
        cj1 = Math.min(Math.ceil(bottom / chunkW), Math.ceil(map.height / (C * step))) - 1;
        const count = (ci1 - ci0 + 1) * (cj1 - cj0 + 1);
        if (count <= CONFIG.chunks.maxVisibleChunks || step >= CONFIG.chunks.maxLodStep) break;
        step *= 2;
      }
      currentStep = step;

      // Wanted chunks, nearest to the focus first so they build first.
      const wanted: { ci: number; cj: number; d: number }[] = [];
      for (let cj = cj0; cj <= cj1; cj++) {
        for (let ci = ci0; ci <= ci1; ci++) {
          const dx = (ci + 0.5) * chunkW - focusX;
          const dy = (cj + 0.5) * chunkW - focusY;
          wanted.push({ ci, cj, d: dx * dx + dy * dy });
        }
      }
      wanted.sort((a, b) => a.d - b.d);

      // Hide everything, then show/build what's wanted within the budget.
      for (const chunk of cache.values()) if (chunk.mesh) chunk.mesh.visible = false;
      const start = performance.now();
      pending = 0;
      visibleCount = 0;
      for (const w of wanted) {
        const key = `${step}:${w.ci}:${w.cj}`;
        let chunk = cache.get(key);
        if (!chunk) {
          if (performance.now() - start > CONFIG.chunks.buildBudgetMs) { pending++; continue; }
          const mesh = buildChunk(w.ci, w.cj, step);
          if (mesh) group.add(mesh);
          chunk = { key, mesh, lastUsed: frame };
          cache.set(key, chunk);
        }
        chunk.lastUsed = frame;
        if (chunk.mesh) chunk.mesh.visible = true;
        visibleCount++;
      }
      evict();
    },

    stats() {
      return { visible: visibleCount, cached: cache.size, step: currentStep, pending };
    },
  };
}
