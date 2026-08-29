import { describe, expect, test } from "bun:test";
import { paintTiles, paintHeight, smoothHeight, falloff, circleBbox, unionBbox, fillHeight, HEIGHT_MIN, HEIGHT_MAX } from "../src/core/paint";
import { History, StrokeRecorder, CHUNK } from "../src/core/history";

describe("paint", () => {
  test("circleBbox clamps to map and returns null off-map", () => {
    expect(circleBbox(2, 2, 5, 64)).toEqual({ x0: 0, y0: 0, x1: 8, y1: 8 });
    expect(circleBbox(-100, -100, 5, 64)).toBeNull();
    expect(unionBbox({ x0: 0, y0: 0, x1: 2, y1: 2 }, { x0: 5, y0: 1, x1: 9, y1: 3 })).toEqual({ x0: 0, y0: 0, x1: 9, y1: 3 });
  });
  test("tile brush paints a disc of ~pi r^2 pixels", () => {
    const size = 128, tiles = new Uint8Array(size * size);
    paintTiles(tiles, size, 64, 64, 20, 3);
    let n = 0; for (const v of tiles) if (v === 3) n++;
    expect(Math.abs(n - Math.PI * 400) / (Math.PI * 400)).toBeLessThan(0.05);
    expect(tiles[64 * size + 64]).toBe(3);
    expect(tiles[0]).toBe(0);
  });
  test("falloff is 1 at center, 0 at rim, monotone", () => {
    expect(falloff(0, 10)).toBe(1);
    expect(falloff(10, 10)).toBe(0);
    let prev = 1;
    for (let d = 1; d <= 10; d++) { const f = falloff(d, 10); expect(f).toBeLessThanOrEqual(prev); prev = f; }
  });
  test("height brush is soft: full delta at center, none outside", () => {
    const size = 64, h = new Float32Array(size * size).fill(10);
    paintHeight(h, size, 32, 32, 10, 5);
    expect(h[32 * size + 32]).toBeCloseTo(15);
    expect(h[32 * size + 37]).toBeGreaterThan(10);
    expect(h[32 * size + 37]).toBeLessThan(15);
    expect(h[32 * size + 43]).toBe(10);
    paintHeight(h, size, 32, 32, 10, -5);
    expect(h[32 * size + 32]).toBeCloseTo(10);
  });
  test("heights clamp to [HEIGHT_MIN, HEIGHT_MAX]", () => {
    const size = 32, h = new Float32Array(size * size).fill(50);
    paintHeight(h, size, 16, 16, 8, 1000);
    expect(h[16 * size + 16]).toBe(HEIGHT_MAX);
    paintHeight(h, size, 16, 16, 8, -1000);
    expect(h[16 * size + 16]).toBe(HEIGHT_MIN);
    for (const v of h) { expect(v).toBeGreaterThanOrEqual(HEIGHT_MIN); expect(v).toBeLessThanOrEqual(HEIGHT_MAX); }
    fillHeight(h, -5); expect(h[0]).toBe(HEIGHT_MIN);
    fillHeight(h, 500); expect(h[0]).toBe(HEIGHT_MAX);
  });
  test("smooth brush reduces local variance and preserves flats", () => {
    const size = 64, h = new Float32Array(size * size);
    for (let i = 0; i < h.length; i++) h[i] = (i * 7919) % 13; // noisy
    const variance = () => {
      let s = 0, s2 = 0, n = 0;
      for (let y = 26; y < 38; y++) for (let x = 26; x < 38; x++) { const v = h[y * size + x]; s += v; s2 += v * v; n++; }
      return s2 / n - (s / n) ** 2;
    };
    const v0 = variance();
    for (let i = 0; i < 5; i++) smoothHeight(h, size, 32, 32, 12, 0.5);
    expect(variance()).toBeLessThan(v0 * 0.5);
    const flat = new Float32Array(size * size).fill(7);
    smoothHeight(flat, size, 32, 32, 12, 0.5);
    for (const v of flat) expect(v).toBeCloseTo(7);
  });
});

describe("history", () => {
  test("stroke record -> undo -> redo roundtrip", () => {
    const size = CHUNK * 4;
    const tiles = new Uint8Array(size * size), height = new Float32Array(size * size);
    const hist = new History();
    const rec = new StrokeRecorder("tiles", tiles, size);
    const b = circleBbox(70, 70, 10, size)!;
    rec.touch(b.x0, b.y0, b.x1, b.y1);
    paintTiles(tiles, size, 70, 70, 10, 4);
    const entry = rec.commit()!;
    expect(entry).not.toBeNull();
    expect(entry.chunks.length).toBeGreaterThanOrEqual(1);
    hist.push(entry);
    expect(hist.canUndo).toBe(true);
    hist.undo({ tiles, height, markers: [] }, size);
    expect(tiles[70 * size + 70]).toBe(0);
    expect(hist.canRedo).toBe(true);
    hist.redo({ tiles, height, markers: [] }, size);
    expect(tiles[70 * size + 70]).toBe(4);
  });
  test("a stroke that changes nothing commits null", () => {
    const size = CHUNK * 2, tiles = new Uint8Array(size * size);
    const rec = new StrokeRecorder("tiles", tiles, size);
    rec.touch(0, 0, 10, 10);
    paintTiles(tiles, size, 5, 5, 3, 0); // already 0
    expect(rec.commit()).toBeNull();
  });
  test("memory cap drops oldest entries", () => {
    const size = CHUNK * 2, height = new Float32Array(size * size), tiles = new Uint8Array(size * size);
    const hist = new History(CHUNK * CHUNK * 4 * 2 * 3); // room for ~3 single-chunk float entries
    for (let i = 0; i < 6; i++) {
      const rec = new StrokeRecorder("height", height, size);
      rec.touch(0, 0, 8, 8);
      paintHeight(height, size, 4, 4, 3, 1 + i);
      hist.push(rec.commit()!);
    }
    let n = 0; while (hist.undo({ tiles, height, markers: [] }, size)) n++;
    expect(n).toBeLessThanOrEqual(3);
    expect(n).toBeGreaterThanOrEqual(1);
  });
});
