// One-off pass: shrink each city's red block on the map to match its size.
//
//   node scripts/resize-city-blocks.js
//
// The world generator paints every city as a 5x5 block of city tiles. Large
// cities keep that; medium ones shrink to 4x4 and small ones to 3x3, and the
// trimmed cells become grassland. Rewrites BOTH public/map.dmap (the terrain)
// and cities.json (a shrunk block re-centres, so the city's location moves by
// up to half a tile).
//
// Must run AFTER generate-cities.js — it takes each city's size from
// cities.json and finds that city's block on the map. Idempotent: a block
// that's already the right size is left alone, so re-running is a no-op.
//
// Note this makes the block shapes the record of city size on the map, which
// is what generate-cities.js reads back when it re-derives a shrunk map.

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MAP_PATH = join(ROOT, 'public', 'map.dmap');
const CITIES_PATH = join(ROOT, 'cities.json');

const TILE_SCALE = 20; // keep in sync with src/config.ts
const CITY = 7;
const GRASSLAND = 2;   // what a trimmed city cell becomes
// Block edge in cells, by city size. Keep in sync with generate-cities.js.
const BLOCK_CELLS = { small: 3, medium: 4, large: 5 };

// --- map reading (same format as src/dmap.ts) -----------------------------

function readDmap(path) {
  const buf = readFileSync(path);
  if (buf.length < 12 || buf.toString('ascii', 0, 4) !== 'DMAP') {
    throw new Error(`${path} is not a DMAP file`);
  }
  const width = buf.readUInt32LE(4);
  const height = buf.readUInt32LE(8);
  if (buf.length !== 12 + width * height) {
    throw new Error(`DMAP size mismatch: expected ${12 + width * height} bytes, got ${buf.length}`);
  }
  return { buf, width, height, data: buf.subarray(12) };
}

function findCityBlocks(map) {
  const { width, height, data } = map;
  const seen = new Uint8Array(width * height);
  const blocks = [];

  for (let start = 0; start < data.length; start++) {
    if (data[start] !== CITY || seen[start]) continue;
    seen[start] = 1;
    const stack = [start];
    let count = 0, minX = Infinity, maxX = -1, minY = Infinity, maxY = -1;

    while (stack.length) {
      const p = stack.pop();
      const x = p % width;
      const y = (p / width) | 0;
      count++;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
        const q = ny * width + nx;
        if (data[q] === CITY && !seen[q]) { seen[q] = 1; stack.push(q); }
      }
    }

    blocks.push({ minX, maxX, minY, maxY, count });
  }
  return blocks;
}

// --- main -----------------------------------------------------------------

const map = readDmap(MAP_PATH);
const blocks = findCityBlocks(map);
const cities = JSON.parse(readFileSync(CITIES_PATH, 'utf8')).cities;

if (blocks.length !== cities.length) {
  throw new Error(`map has ${blocks.length} city blocks but cities.json has ${cities.length} cities`);
}

// Match each city to its block by centre: locations came from these same
// blocks, and a resize only ever moves a centre by half a tile.
const centreOf = (b) => ({
  x: ((b.minX + b.maxX + 1) / 2) * TILE_SCALE,
  y: ((b.minY + b.maxY + 1) / 2) * TILE_SCALE,
});
const MATCH_TOLERANCE = TILE_SCALE; // one tile — far tighter than the block spacing

const taken = new Set();
const pairs = cities.map((city) => {
  let best = -1, bestDist = Infinity;
  blocks.forEach((block, i) => {
    if (taken.has(i)) return;
    const c = centreOf(block);
    const d = Math.hypot(c.x - city.location.x, c.y - city.location.y);
    if (d < bestDist) { bestDist = d; best = i; }
  });
  if (best < 0 || bestDist > MATCH_TOLERANCE) {
    throw new Error(`no block within ${MATCH_TOLERANCE} units of ${city.name} at ${city.location.x},${city.location.y}`);
  }
  taken.add(best);
  return { city, block: blocks[best] };
});

let trimmed = 0, resized = 0;
const counts = { small: 0, medium: 0, large: 0 };

const out = pairs.map(({ city, block }) => {
  const target = BLOCK_CELLS[city.size];
  if (!target) throw new Error(`${city.name} has unknown size ${city.size}`);

  const w = block.maxX - block.minX + 1;
  const h = block.maxY - block.minY + 1;
  if (w !== h) throw new Error(`${city.name}'s block is ${w}x${h}, not square`);
  if (block.count !== w * h) throw new Error(`${city.name}'s block is not solid (${block.count} of ${w * h} cells)`);
  if (w < target) throw new Error(`${city.name} is ${city.size} (needs ${target}x${target}) but its block is only ${w}x${w}`);
  counts[city.size]++;
  if (w === target) return city; // already right — keeps the pass idempotent

  // Centre the kept square in the old block. Trimming an odd amount (5 -> 4)
  // can't stay centred, so the extra row/column comes off the right/bottom.
  const startX = block.minX + Math.floor((w - target) / 2);
  const startY = block.minY + Math.floor((h - target) / 2);

  for (let y = block.minY; y <= block.maxY; y++) {
    for (let x = block.minX; x <= block.maxX; x++) {
      const keep = x >= startX && x < startX + target && y >= startY && y < startY + target;
      if (keep) continue;
      map.data[y * map.width + x] = GRASSLAND;
      trimmed++;
    }
  }
  resized++;

  return {
    ...city,
    location: {
      x: (startX + target / 2) * TILE_SCALE,
      y: (startY + target / 2) * TILE_SCALE,
    },
  };
});

writeFileSync(MAP_PATH, map.buf);
writeFileSync(CITIES_PATH, JSON.stringify({ cities: out }, null, 2) + '\n');

console.log(`${cities.length} cities: ${counts.small} small (3x3) / ${counts.medium} medium (4x4) / ${counts.large} large (5x5)`);
console.log(resized ? `resized ${resized} blocks, ${trimmed} city cells -> grassland` : 'every block was already the right size (no-op)');
console.log(`wrote ${MAP_PATH}`);
console.log(`wrote ${CITIES_PATH}`);
