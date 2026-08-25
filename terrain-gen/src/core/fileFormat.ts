/** Binary terrain file format (".terrain").
 *
 *  Little-endian, version 1:
 *    offset 0   char[4]  magic "TGN1"
 *    offset 4   u32      size (pixels per side, power of two)
 *    offset 8   u32      flags (reserved, 0)
 *    offset 12  f32      worldSize
 *    offset 16  f32[size*size]  heights (world units)
 *    then       u8[size*size]   tile ids
 *
 *  The UI gzips this (CompressionStream) when saving and transparently
 *  gunzips on load; `encode`/`decode` here are the raw, uncompressed form. */

export const MAGIC = "TGN1";
export const HEADER_BYTES = 16;

export interface TerrainFile {
  size: number;
  worldSize: number;
  height: Float32Array;
  tiles: Uint8Array;
}

export function encodeTerrain(t: TerrainFile): Uint8Array {
  const n = t.size * t.size;
  if (t.height.length !== n || t.tiles.length !== n) throw new Error("array length does not match size");
  const out = new Uint8Array(HEADER_BYTES + n * 4 + n);
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
  return out;
}

export function decodeTerrain(bytes: Uint8Array): TerrainFile {
  if (bytes.length < HEADER_BYTES) throw new Error("file too short");
  const magic = String.fromCharCode(bytes[0], bytes[1], bytes[2], bytes[3]);
  if (magic !== MAGIC) throw new Error(`not a terrain file (magic ${JSON.stringify(magic)})`);
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const size = dv.getUint32(4, true);
  if (size < 1 || size > 65536 || (size & (size - 1)) !== 0) throw new Error(`invalid size ${size}`);
  const worldSize = dv.getFloat32(12, true);
  const n = size * size;
  const expected = HEADER_BYTES + n * 4 + n;
  if (bytes.length !== expected) throw new Error(`expected ${expected} bytes for size ${size}, got ${bytes.length}`);
  const height = new Float32Array(n);
  if (isLittleEndian()) {
    height.set(new Float32Array(bytes.buffer.slice(bytes.byteOffset + HEADER_BYTES, bytes.byteOffset + HEADER_BYTES + n * 4)));
  } else {
    for (let i = 0; i < n; i++) height[i] = dv.getFloat32(HEADER_BYTES + i * 4, true);
  }
  const tiles = bytes.slice(HEADER_BYTES + n * 4, expected);
  return { size, worldSize, height, tiles };
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
