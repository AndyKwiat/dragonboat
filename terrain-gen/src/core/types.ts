/** Tile ids and palette. The map is `height: Float32Array` (world units,
 *  sea level = 0) + `tiles: Uint8Array` of these ids, both size*size. */

export const Tile = {
  Water: 0,   // blue
  Sand: 1,    // yellow
  Grass: 2,   // green
  Forest: 3,  // dark green
  Rock: 4,    // grey (mountain)
} as const;
export type TileId = (typeof Tile)[keyof typeof Tile];

/** Tile palette (sRGB 0-255). Index == TileId. */
export const TILE_COLORS: [number, number, number][] = [
  [38, 100, 173],  // water  - blue
  [222, 203, 130], // sand   - yellow
  [110, 156, 74],  // grass  - green
  [52, 92, 46],    // forest - dark green
  [130, 130, 132], // rock   - grey
];
