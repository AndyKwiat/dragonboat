/** Resolution changes. Sizes are powers of two, so the ratio is an integer.
 *  Heights: bilinear when upsampling, block average when downsampling.
 *  Tiles: nearest when upsampling, block mode (most common id) when down. */

export function resampleHeight(src: Float32Array, srcSize: number, dstSize: number): Float32Array {
  if (srcSize === dstSize) return src.slice();
  const dst = new Float32Array(dstSize * dstSize);
  if (dstSize > srcSize) {
    const r = srcSize / dstSize; // < 1
    for (let y = 0; y < dstSize; y++) {
      const sy = Math.max(0, Math.min(srcSize - 1, (y + 0.5) * r - 0.5));
      const y0 = Math.floor(sy), y1 = Math.min(srcSize - 1, y0 + 1), fy = sy - y0;
      for (let x = 0; x < dstSize; x++) {
        const sx = Math.max(0, Math.min(srcSize - 1, (x + 0.5) * r - 0.5));
        const x0 = Math.floor(sx), x1 = Math.min(srcSize - 1, x0 + 1), fx = sx - x0;
        const a = src[y0 * srcSize + x0], b = src[y0 * srcSize + x1];
        const c = src[y1 * srcSize + x0], d = src[y1 * srcSize + x1];
        dst[y * dstSize + x] = (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
      }
    }
  } else {
    const k = srcSize / dstSize, inv = 1 / (k * k);
    for (let y = 0; y < dstSize; y++) {
      for (let x = 0; x < dstSize; x++) {
        let sum = 0;
        for (let yy = 0; yy < k; yy++) {
          const row = (y * k + yy) * srcSize + x * k;
          for (let xx = 0; xx < k; xx++) sum += src[row + xx];
        }
        dst[y * dstSize + x] = sum * inv;
      }
    }
  }
  return dst;
}

export function resampleTiles(src: Uint8Array, srcSize: number, dstSize: number, tileCount = 5): Uint8Array {
  if (srcSize === dstSize) return src.slice();
  const dst = new Uint8Array(dstSize * dstSize);
  if (dstSize > srcSize) {
    const k = dstSize / srcSize;
    for (let y = 0; y < dstSize; y++) {
      const srow = Math.floor(y / k) * srcSize;
      for (let x = 0; x < dstSize; x++) dst[y * dstSize + x] = src[srow + Math.floor(x / k)];
    }
  } else {
    const k = srcSize / dstSize;
    const counts = new Uint32Array(tileCount);
    for (let y = 0; y < dstSize; y++) {
      for (let x = 0; x < dstSize; x++) {
        counts.fill(0);
        for (let yy = 0; yy < k; yy++) {
          const row = (y * k + yy) * srcSize + x * k;
          for (let xx = 0; xx < k; xx++) counts[src[row + xx]]++;
        }
        let best = 0;
        for (let t = 1; t < tileCount; t++) if (counts[t] > counts[best]) best = t;
        dst[y * dstSize + x] = best;
      }
    }
  }
  return dst;
}
