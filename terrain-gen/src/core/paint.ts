/** Pure brush operations on the height / tile arrays. Everything returns the
 *  dirty bbox {x0,y0,x1,y1} (inclusive-exclusive) or null if nothing changed. */

/** Valid height range, world units. All height brushes clamp to it. */
export const HEIGHT_MIN = 1;
export const HEIGHT_MAX = 100;

export interface Bbox { x0: number; y0: number; x1: number; y1: number }

export function circleBbox(cx: number, cy: number, r: number, size: number): Bbox | null {
  const x0 = Math.max(0, Math.floor(cx - r)), x1 = Math.min(size, Math.ceil(cx + r) + 1);
  const y0 = Math.max(0, Math.floor(cy - r)), y1 = Math.min(size, Math.ceil(cy + r) + 1);
  if (x0 >= x1 || y0 >= y1) return null;
  return { x0, y0, x1, y1 };
}

export function unionBbox(a: Bbox | null, b: Bbox | null): Bbox | null {
  if (!a) return b; if (!b) return a;
  return { x0: Math.min(a.x0, b.x0), y0: Math.min(a.y0, b.y0), x1: Math.max(a.x1, b.x1), y1: Math.max(a.y1, b.y1) };
}

/** Hard-edged circular tile stamp. */
export function paintTiles(tiles: Uint8Array, size: number, cx: number, cy: number, r: number, tile: number): Bbox | null {
  const b = circleBbox(cx, cy, r, size);
  if (!b) return null;
  const r2 = r * r;
  for (let y = b.y0; y < b.y1; y++) {
    const dy = y - cy;
    for (let x = b.x0; x < b.x1; x++) {
      const dx = x - cx;
      if (dx * dx + dy * dy <= r2) tiles[y * size + x] = tile;
    }
  }
  return b;
}

/** Smooth radial falloff: 1 at center, 0 at the rim. */
export function falloff(d: number, r: number): number {
  const t = d / r;
  if (t >= 1) return 0;
  const f = 1 - t * t;
  return f * f;
}

/** Soft circular height stamp: adds `delta * falloff` (delta may be negative). */
export function paintHeight(height: Float32Array, size: number, cx: number, cy: number, r: number, delta: number): Bbox | null {
  const b = circleBbox(cx, cy, r, size);
  if (!b) return null;
  for (let y = b.y0; y < b.y1; y++) {
    const dy = y - cy;
    for (let x = b.x0; x < b.x1; x++) {
      const dx = x - cx;
      const f = falloff(Math.hypot(dx, dy), r);
      if (f > 0) {
        const i = y * size + x;
        height[i] = Math.max(HEIGHT_MIN, Math.min(HEIGHT_MAX, height[i] + delta * f));
      }
    }
  }
  return b;
}

/** Smoothing stamp: blends each pixel toward the mean of a (2k+1)² window
 *  around it, strongest at the brush center. Uses a summed-area table over
 *  the padded region so cost is O(pixels), independent of window size.
 *  `amount` in (0,1]: 1 = fully replace with the local mean at the center. */
export function smoothHeight(height: Float32Array, size: number, cx: number, cy: number, r: number, amount: number): Bbox | null {
  const b = circleBbox(cx, cy, r, size);
  if (!b) return null;
  const k = Math.max(1, Math.round(r / 5)); // window radius scales with brush
  const sx0 = Math.max(0, b.x0 - k), sy0 = Math.max(0, b.y0 - k);
  const sx1 = Math.min(size, b.x1 + k), sy1 = Math.min(size, b.y1 + k);
  const sw = sx1 - sx0, sh = sy1 - sy0;
  // summed-area table with a zero row/column of padding: sat[(y+1)*(sw+1)+(x+1)]
  const W = sw + 1;
  const sat = new Float64Array(W * (sh + 1));
  for (let y = 0; y < sh; y++) {
    let rowSum = 0;
    const srcRow = (sy0 + y) * size + sx0;
    const o = (y + 1) * W, oPrev = y * W;
    for (let x = 0; x < sw; x++) {
      rowSum += height[srcRow + x];
      sat[o + x + 1] = sat[oPrev + x + 1] + rowSum;
    }
  }
  const r2 = r * r;
  for (let y = b.y0; y < b.y1; y++) {
    const dy = y - cy;
    const ly = y - sy0;
    const wy0 = Math.max(0, ly - k), wy1 = Math.min(sh, ly + k + 1); // [wy0, wy1)
    for (let x = b.x0; x < b.x1; x++) {
      const dx = x - cx;
      const d2 = dx * dx + dy * dy;
      if (d2 > r2) continue;
      const lx = x - sx0;
      const wx0 = Math.max(0, lx - k), wx1 = Math.min(sw, lx + k + 1);
      const sum = sat[wy1 * W + wx1] - sat[wy0 * W + wx1] - sat[wy1 * W + wx0] + sat[wy0 * W + wx0];
      const mean = sum / ((wy1 - wy0) * (wx1 - wx0));
      const f = falloff(Math.sqrt(d2), r) * amount;
      const i = y * size + x;
      height[i] = Math.max(HEIGHT_MIN, Math.min(HEIGHT_MAX, height[i] + (mean - height[i]) * f));
    }
  }
  return b;
}

export function fillTiles(tiles: Uint8Array, tile: number): void { tiles.fill(tile); }
export function fillHeight(height: Float32Array, value: number): void { height.fill(Math.max(HEIGHT_MIN, Math.min(HEIGHT_MAX, value))); }
