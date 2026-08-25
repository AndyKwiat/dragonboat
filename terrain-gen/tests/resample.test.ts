import { describe, expect, test } from "bun:test";
import { resampleHeight, resampleTiles } from "../src/core/resample";

describe("resampleHeight", () => {
  test("downsample averages blocks", () => {
    const src = new Float32Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]); // 4x4
    const d = resampleHeight(src, 4, 2);
    expect(Array.from(d)).toEqual([3.5, 5.5, 11.5, 13.5]);
  });
  test("upsample interpolates smoothly and keeps range", () => {
    const src = new Float32Array([0, 10, 0, 10]); // 2x2: left col 0, right col 10
    const u = resampleHeight(src, 2, 4);
    const row = Array.from(u.slice(0, 4));
    expect(row[0]).toBeCloseTo(0); expect(row[3]).toBeCloseTo(10);
    expect(row[1]).toBeGreaterThan(row[0]); expect(row[2]).toBeGreaterThan(row[1]);
    for (const v of u) { expect(v).toBeGreaterThanOrEqual(0); expect(v).toBeLessThanOrEqual(10); }
  });
  test("constant maps stay constant both ways", () => {
    const src = new Float32Array(64 * 64).fill(1);
    for (const v of resampleHeight(src, 64, 128)) expect(v).toBe(1);
    for (const v of resampleHeight(src, 64, 16)) expect(v).toBe(1);
  });
});

describe("resampleTiles", () => {
  test("downsample picks the most common tile", () => {
    const src = new Uint8Array([4, 4, 2, 4, 4, 1, 1, 1, 0, 0, 3, 3, 0, 2, 3, 3]); // 4x4, no ties
    const d = resampleTiles(src, 4, 2);
    expect(Array.from(d)).toEqual([4, 1, 0, 3]);
  });
  test("upsample replicates nearest", () => {
    const src = new Uint8Array([1, 2, 3, 4]);
    const u = resampleTiles(src, 2, 4);
    expect(Array.from(u)).toEqual([1, 1, 2, 2, 1, 1, 2, 2, 3, 3, 4, 4, 3, 3, 4, 4]);
  });
});
