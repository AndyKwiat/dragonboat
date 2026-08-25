import { describe, expect, test } from "bun:test";
import { encodeTerrain, decodeTerrain, gzip, gunzip, isGzip, HEADER_BYTES, MAGIC } from "../src/core/fileFormat";

const sample = (size = 16) => {
  const n = size * size;
  const height = new Float32Array(n), tiles = new Uint8Array(n);
  for (let i = 0; i < n; i++) { height[i] = 1 + (i * 37 % 99) + 0.25; tiles[i] = i % 5; }
  return { size, worldSize: 1024, height, tiles };
};

describe("terrain file format", () => {
  test("encode layout: magic, header, exact byte length", () => {
    const t = sample(8);
    const b = encodeTerrain(t);
    expect(String.fromCharCode(...b.slice(0, 4))).toBe(MAGIC);
    expect(b.length).toBe(HEADER_BYTES + 64 * 4 + 64);
    expect(new DataView(b.buffer).getUint32(4, true)).toBe(8);
  });
  test("roundtrip preserves heights and tiles exactly", () => {
    const t = sample(32);
    const d = decodeTerrain(encodeTerrain(t));
    expect(d.size).toBe(32);
    expect(d.worldSize).toBe(1024);
    expect(Array.from(d.height)).toEqual(Array.from(t.height));
    expect(Array.from(d.tiles)).toEqual(Array.from(t.tiles));
  });
  test("rejects garbage, truncation, bad size", () => {
    expect(() => decodeTerrain(new Uint8Array(3))).toThrow();
    const b = encodeTerrain(sample(8));
    b[0] = 88; // break magic
    expect(() => decodeTerrain(b)).toThrow(/not a terrain file/);
    const ok = encodeTerrain(sample(8));
    expect(() => decodeTerrain(ok.slice(0, ok.length - 1))).toThrow(/expected/);
    const bad = encodeTerrain(sample(8));
    new DataView(bad.buffer).setUint32(4, 12, true); // not a power of two
    expect(() => decodeTerrain(bad)).toThrow(/invalid size/);
  });
  test("mismatched array lengths refuse to encode", () => {
    const t = sample(8); t.tiles = new Uint8Array(10);
    expect(() => encodeTerrain(t)).toThrow();
  });
  test("gzip roundtrip and detection", async () => {
    const raw = encodeTerrain(sample(64));
    const z = await gzip(raw);
    expect(isGzip(z)).toBe(true);
    expect(isGzip(raw)).toBe(false);
    expect(z.length).toBeLessThan(raw.length); // repetitive sample compresses
    const back = await gunzip(z);
    expect(Array.from(back)).toEqual(Array.from(raw));
    expect(decodeTerrain(back).size).toBe(64);
  });
});
