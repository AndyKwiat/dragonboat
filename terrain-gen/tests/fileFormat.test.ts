import { describe, expect, test } from "bun:test";
import { encodeTerrain, decodeTerrain, gzip, gunzip, isGzip, HEADER_BYTES, MAGIC, MAGIC_V1, MAGIC_V2 } from "../src/core/fileFormat";
import type { Marker } from "../src/core/markers";

const sample = (size = 16) => {
  const n = size * size;
  const height = new Float32Array(n), tiles = new Uint8Array(n);
  for (let i = 0; i < n; i++) { height[i] = 1 + (i * 37 % 99) + 0.25; tiles[i] = i % 5; }
  return { size, worldSize: 1024, height, tiles, markers: [] as Marker[] };
};

/** Hand-build a version-1 file (no marker section) the way the old encoder did. */
const encodeV1 = (t: ReturnType<typeof sample>): Uint8Array => {
  const n = t.size * t.size;
  const out = new Uint8Array(HEADER_BYTES + n * 4 + n);
  const dv = new DataView(out.buffer);
  for (let i = 0; i < 4; i++) out[i] = MAGIC_V1.charCodeAt(i);
  dv.setUint32(4, t.size, true); dv.setUint32(8, 0, true); dv.setFloat32(12, t.worldSize, true);
  for (let i = 0; i < n; i++) dv.setFloat32(HEADER_BYTES + i * 4, t.height[i], true);
  out.set(t.tiles, HEADER_BYTES + n * 4);
  return out;
};

describe("terrain file format", () => {
  test("encode layout: magic, header, exact byte length", () => {
    const t = sample(8);
    const b = encodeTerrain(t);
    expect(String.fromCharCode(...b.slice(0, 4))).toBe(MAGIC);
    expect(MAGIC).toBe(MAGIC_V2);
    expect(b.length).toBe(HEADER_BYTES + 64 * 4 + 64 + 4); // + u32 marker count (0)
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
    expect(() => decodeTerrain(encodeV1(sample(8)).slice(0, -1))).toThrow(/expected/);
    const bad = encodeTerrain(sample(8));
    new DataView(bad.buffer).setUint32(4, 12, true); // not a power of two
    expect(() => decodeTerrain(bad)).toThrow(/invalid size/);
  });
  test("mismatched array lengths refuse to encode", () => {
    const t = sample(8); t.tiles = new Uint8Array(10);
    expect(() => encodeTerrain(t)).toThrow();
  });
  test("loads a version-1 file (no markers) and re-saves as version 2", () => {
    const t = sample(16);
    const v1 = encodeV1(t);
    expect(String.fromCharCode(...v1.slice(0, 4))).toBe("TGN1");
    const d = decodeTerrain(v1);
    expect(d.markers).toEqual([]);
    expect(Array.from(d.height)).toEqual(Array.from(t.height));
    expect(Array.from(d.tiles)).toEqual(Array.from(t.tiles));
    d.markers.push({ x: 1, y: 2, r: 10, tag: "upgraded" });
    const v2 = encodeTerrain(d);
    expect(String.fromCharCode(...v2.slice(0, 4))).toBe("TGN2");
    expect(decodeTerrain(v2).markers).toEqual([{ x: 1, y: 2, r: 10, tag: "upgraded" }]);
    // a v1 file with extra trailing bytes is corrupt, not v2
    expect(() => decodeTerrain(new Uint8Array([...v1, 0, 0, 0, 0]))).toThrow(/expected/);
  });
  test("markers roundtrip incl. unicode tags; exact section layout", () => {
    const t = sample(8);
    t.markers = [
      { x: 3.5, y: 4.25, r: 10, tag: "harbor" },
      { x: 0, y: 7, r: 0.5, tag: "cité — ☃ 🐉" },
      { x: 7, y: 0, r: 800, tag: "x".repeat(100) },
    ];
    const b = encodeTerrain(t);
    const pixelsEnd = HEADER_BYTES + 64 * 4 + 64;
    const dv = new DataView(b.buffer);
    expect(dv.getUint32(pixelsEnd, true)).toBe(3);
    expect(dv.getFloat32(pixelsEnd + 4, true)).toBe(3.5);
    expect(dv.getFloat32(pixelsEnd + 8, true)).toBe(4.25);
    expect(dv.getFloat32(pixelsEnd + 12, true)).toBe(10);
    expect(dv.getUint16(pixelsEnd + 16, true)).toBe(6); // "harbor"
    expect(String.fromCharCode(...b.slice(pixelsEnd + 18, pixelsEnd + 24))).toBe("harbor");
    const d = decodeTerrain(b);
    expect(d.markers).toEqual(t.markers);
  });
  test("marker validation on encode and decode", () => {
    const t = sample(8);
    t.markers = [{ x: 0, y: 0, r: 1, tag: "" }];
    expect(() => encodeTerrain(t)).toThrow(/tag/);
    t.markers = [{ x: 0, y: 0, r: 1, tag: "y".repeat(101) }];
    expect(() => encodeTerrain(t)).toThrow(/tag/);
    t.markers = [{ x: 0, y: 0, r: 1, tag: "ok" }];
    const b = encodeTerrain(t);
    expect(() => decodeTerrain(b.slice(0, -1))).toThrow(/truncated/);
    expect(() => decodeTerrain(new Uint8Array([...b, 7]))).toThrow(/trailing/);
    const badR = encodeTerrain(t);
    new DataView(badR.buffer).setFloat32(HEADER_BYTES + 64 * 5 + 4 + 8, 0, true); // r = 0
    expect(() => decodeTerrain(badR)).toThrow(/radius/);
    const badUtf = encodeTerrain(t);
    badUtf[badUtf.length - 1] = 0xff;
    expect(() => decodeTerrain(badUtf)).toThrow(/UTF-8/);
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
