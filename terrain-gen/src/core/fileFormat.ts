/** Binary terrain file format (".terrain"). Full spec: docs/FILE_FORMAT.md.
 *
 *  Little-endian. The first 4 bytes are the magic, which doubles as the
 *  version number: "TGN1" = version 1, "TGN2" = version 2.
 *
 *    offset 0   char[4]  magic "TGN1" | "TGN2"
 *    offset 4   u32      size (pixels per side, power of two)
 *    offset 8   u32      flags (reserved, 0)
 *    offset 12  f32      worldSize
 *    offset 16  f32[size*size]  heights (world units)
 *    then       u8[size*size]   tile ids
 *    -- version 2 only, appended after the tiles --
 *    then       u32      marker count
 *    then       marker[count]:  f32 x, f32 y, f32 r, u16 tagLen, u8[tagLen] utf-8 tag
 *
 *  `decodeTerrain` reads both versions (v1 yields an empty marker list);
 *  `encodeTerrain` always writes the current version.
 *
 *  The UI gzips this (CompressionStream) when saving and transparently
 *  gunzips on load; `encode`/`decode` here are the raw, uncompressed form. */
import type { Marker } from "./markers";
import { MAX_MARKERS, MAX_TAG_LENGTH, makeMarker } from "./markers";

export const MAGIC_V1 = "TGN1";
export const MAGIC_V2 = "TGN2";
/** Magic written by `encodeTerrain` (current version). */
export const MAGIC = MAGIC_V2;
export const CURRENT_VERSION = 2;
export const HEADER_BYTES = 16;

export interface TerrainFile {
  size: number;
  worldSize: number;
  height: Float32Array;
  tiles: Uint8Array;
  markers: Marker[];
}

const enc = new TextEncoder();
const dec = new TextDecoder("utf-8", { fatal: true });

export function encodeTerrain(t: TerrainFile): Uint8Array {
  const n = t.size * t.size;
  if (t.height.length !== n || t.tiles.length !== n) throw new Error("array length does not match size");
  const markers = t.markers ?? [];
  if (markers.length > MAX_MARKERS) throw new Error(`too many markers (${markers.length})`);
  const tagBytes = markers.map((m) => {
    if (m.tag.length === 0 || m.tag.length > MAX_TAG_LENGTH) throw new Error(`invalid marker tag ${JSON.stringify(m.tag)}`);
    return enc.encode(m.tag);
  });
  let markerBytes = 4;
  for (const b of tagBytes) markerBytes += 12 + 2 + b.length;

  const out = new Uint8Array(HEADER_BYTES + n * 4 + n + markerBytes);
  const dv = new DataView(out.buffer);
  for (let i = 0; i < 4; i++) out[i] = MAGIC.charCodeAt(i);
  dv.setUint32(4, t.size, true);
  dv.setUint32(8, 0, true);
  dv.setFloat32(12, t.worldSize, true);
  // heights: copy as bytes (little-endian host assumed; DataView fallback otherwise)
  const heightBytes = new Uint8Array(t.height.buffer, t.height.byteOffset, n * 4);
  if (isLittleEndian()) out.set(heightBytes, HEADER_BYTES);
  else for (let i = 0; i < n; i++) dv.setFloat32(HEADER_BYTES + i * 4, t.height[i], true);
  out.set(t.tiles, HEADER_BYTES + n * 4);

  let p = HEADER_BYTES + n * 4 + n;
  dv.setUint32(p, markers.length, true); p += 4;
  for (let i = 0; i < markers.length; i++) {
    const m = markers[i], b = tagBytes[i];
    dv.setFloat32(p, m.x, true); dv.setFloat32(p + 4, m.y, true); dv.setFloat32(p + 8, m.r, true); p += 12;
    dv.setUint16(p, b.length, true); p += 2;
    out.set(b, p); p += b.length;
  }
  return out;
}

export function decodeTerrain(bytes: Uint8Array): TerrainFile {
  if (bytes.length < HEADER_BYTES) throw new Error("file too short");
  const magic = String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]);
  const version = magic === MAGIC_V1 ? 1 : magic === MAGIC_V2 ? 2 : 0;
  if (version === 0) throw new Error(`not a terrain file (magic ${JSON.stringify(magic)})`);
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const size = dv.getUint32(4, true);
  if (size < 1 || size > 65536 || (size & (size - 1)) !== 0) throw new Error(`invalid size ${size}`);
  const worldSize = dv.getFloat32(12, true);
  const n = size * size;
  const pixelsEnd = HEADER_BYTES + n * 4 + n;
  if (version === 1 && bytes.length !== pixelsEnd) throw new Error(`expected ${pixelsEnd} bytes for size ${size}, got ${bytes.length}`);
  if (version === 2 && bytes.length < pixelsEnd + 4) throw new Error(`expected at least ${pixelsEnd + 4} bytes for size ${size}, got ${bytes.length}`);
  const height = new Float32Array(n);
  if (isLittleEndian()) {
    height.set(new Float32Array(bytes.buffer.slice(bytes.byteOffset + HEADER_BYTES, bytes.byteOffset + HEADER_BYTES + n * 4)));
  } else {
    for (let i = 0; i < n; i++) height[i] = dv.getFloat32(HEADER_BYTES + i * 4, true);
  }
  const tiles = bytes.slice(HEADER_BYTES + n * 4, pixelsEnd);

  const markers: Marker[] = [];
  if (version >= 2) {
    let p = pixelsEnd;
    const count = dv.getUint32(p, true); p += 4;
    if (count > MAX_MARKERS) throw new Error(`too many markers (${count})`);
    for (let i = 0; i < count; i++) {
      if (p + 14 > bytes.length) throw new Error(`truncated marker ${i}`);
      const x = dv.getFloat32(p, true), y = dv.getFloat32(p + 4, true), r = dv.getFloat32(p + 8, true); p += 12;
      const len = dv.getUint16(p, true); p += 2;
      if (p + len > bytes.length) throw new Error(`truncated marker ${i} tag`);
      let tag: string;
      try { tag = dec.decode(bytes.subarray(p, p + len)); } catch { throw new Error(`marker ${i} tag is not valid UTF-8`); }
      p += len;
      markers.push(makeMarker(x, y, r, tag));
    }
    if (p !== bytes.length) throw new Error(`${bytes.length - p} trailing bytes after markers`);
  }
  return { size, worldSize, height, tiles, markers };
}

export function isGzip(bytes: Uint8Array): boolean {
  return bytes.length > 2 && bytes[0] === 0x1f && bytes[1] === 0x8b;
}

/** gzip/gunzip via the web streams API in browsers; Bun (tests) lacks
 *  CompressionStream, so fall back to its native zlib there. */
export async function gzip(bytes: Uint8Array): Promise<Uint8Array> {
  const bun = (globalThis as any).Bun;
  if (typeof CompressionStream === "undefined" && bun?.gzipSync) return new Uint8Array(bun.gzipSync(bytes));
  return pipe(bytes, new CompressionStream("gzip"));
}
export async function gunzip(bytes: Uint8Array): Promise<Uint8Array> {
  const bun = (globalThis as any).Bun;
  if (typeof DecompressionStream === "undefined" && bun?.gunzipSync) return new Uint8Array(bun.gunzipSync(bytes));
  return pipe(bytes, new DecompressionStream("gzip"));
}
async function pipe(bytes: Uint8Array, ts: GenericTransformStream): Promise<Uint8Array> {
  const stream = new Blob([bytes as BlobPart]).stream().pipeThrough(ts);
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

function isLittleEndian(): boolean {
  return new Uint8Array(new Uint16Array([1]).buffer)[0] === 1;
}
