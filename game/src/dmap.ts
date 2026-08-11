// Parser for the world-generator .dmap format (see world-generator/map-format.md).
// Layout: 'DMAP' magic, uint32 LE width, uint32 LE height, then width*height
// uint8 terrain indices, row-major, top-to-bottom.

export const TERRAIN = {
  OCEAN: 0,
  BEACH: 1,
  GRASSLAND: 2,
  FOREST: 3,
  MOUNTAIN: 4,
  RIVER: 5,
  CANAL: 6,
  CITY: 7,
} as const;

export interface DMap {
  width: number;
  height: number;
  data: Uint8Array;  // row-major, top-to-bottom
}

// Terrain index at map cell (x, y), y growing downward from the top-left.
export function terrainAt(map: DMap, x: number, y: number): number {
  return map.data[y * map.width + x] ?? TERRAIN.OCEAN;
}

export function parseDmap(buf: ArrayBuffer): DMap {
  const view = new DataView(buf);
  if (buf.byteLength < 12 ||
      view.getUint8(0) !== 0x44 || view.getUint8(1) !== 0x4d ||
      view.getUint8(2) !== 0x41 || view.getUint8(3) !== 0x50) {
    throw new Error('Not a DMAP file');
  }
  const width = view.getUint32(4, true);
  const height = view.getUint32(8, true);
  if (buf.byteLength !== 12 + width * height) {
    throw new Error(`DMAP size mismatch: expected ${12 + width * height} bytes, got ${buf.byteLength}`);
  }
  return { width, height, data: new Uint8Array(buf, 12, width * height) };
}

export async function loadDmap(url: string): Promise<DMap> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Failed to fetch ${url}: ${res.status}`);
  return parseDmap(await res.arrayBuffer());
}
