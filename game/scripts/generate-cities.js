// One-off generator: reads public/map.dmap, finds every city block, and writes
// cities.json (name / location / size) next to it.
//
//   node scripts/generate-cities.js
//
// Cities in the map export are 5x5 blocks of terrain index 7. Each block
// becomes one city whose location is the block's centroid in WORLD units
// (map cell coords * TILE_SCALE). Sizes are handed out 6 small : 3 medium :
// 1 large, then large cities are swapped around to spread them out.
//
// Deterministic: a fixed PRNG seed means re-running produces the same file.

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const MAP_PATH = join(ROOT, 'public', 'map.dmap');
const OUT_PATH = join(ROOT, 'cities.json');

const TILE_SCALE = 20; // keep in sync with src/config.ts
const CITY = 7;
const SEED = 0x5eed1234;

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
  return { width, height, data: buf.subarray(12) };
}

// Flood-fill every 4-connected blob of city tiles.
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

// --- names ----------------------------------------------------------------

// Two flavours mixed together: invented place names and the tourist-brochure
// stereotypes. Every entry must be unique (asserted below).
const NAMES = [
  'Dragon Bay', 'Jade City', 'Lotus Harbor', 'Golden Pagoda', 'Silk Harbor',
  'Pearl Cove', 'Bamboo Grove', 'Red Lantern', 'Tiger Gate', 'Phoenix Landing',
  'Koi Bay', 'Cherry Harbor', 'Moon Bridge', 'Ivory Pagoda', 'Crane Bay',
  'Willow Port', 'Thunder Gate', 'Emerald Reach', 'Jasmine Bay', 'Temple Rock',
  'Turtle Cove', 'Peony Hill', 'Sky Lantern', 'Monsoon Bay', 'Iron Pagoda',
  'Sunrise Gate', 'Rain Harbor', 'Coral Pagoda', 'White Crane', 'Nine Dragons',
  'Two Rivers', 'Lotus Fields', 'Painted Bridge', 'Salt Harbor', 'Tea Hill',
  'Bright Lotus', 'Storm Gate', 'Silver Bay', 'Jade Terrace', 'Dragon Gate',
  'Paper Lantern', 'Green Tortoise', 'Morning Bell', 'Blue Pagoda', 'Lucky Wind',
  'Old Harbor', 'Cloud Temple', 'Amber Cove', 'Sword Hill', 'Fish Market Bay',
  'Kaido', 'Sanjo', 'Hanwei', 'Meilin', 'Lianhu',
  'Baiyun', 'Fenglin', 'Haisha', 'Jinhai', 'Kunlai',
  'Linhe', 'Mingzhu', 'Nanpo', 'Panyu', 'Ruilin',
  'Shulan', 'Taipa', 'Wanli', 'Yulin', 'Anshan',
  'Bohai', 'Chenglu', 'Daiwan', 'Enjo', 'Fuyama',
  'Harumi', 'Isumi', 'Joban', 'Kamiya', 'Maruko',
  'Nagase', 'Okumi', 'Rinzai', 'Sakuma', 'Tomari',
  'Umino', 'Yagami', 'Sendo', 'Kirishima', 'Hoshin',
  'Naru', 'Toya', 'Ichimoto', 'Kanazu', 'Mitani',
  'Obara', 'Seto', 'Tanaru', 'Waseda', 'Yamato',
  'Aoki', 'Chiya', 'Domon', 'Fusano', 'Genji',
  'Hakuba', 'Ineda', 'Kobune', 'Mekaru', 'Nozomi',
  'Rensei', 'Shirane', 'Tsubaki', 'Ubaru', 'Yuhan',
  'Baisha', 'Congan', 'Danhu', 'Fanling', 'Guilin',
  'Hualin', 'Jiadao', 'Kaiping', 'Longwan', 'Meishan',
];

// --- deterministic RNG ----------------------------------------------------

function makeRng(seed) {
  let s = seed >>> 0;
  return () => {
    // mulberry32
    s = (s + 0x6d2b79f5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function shuffled(items, rng) {
  const out = items.slice();
  for (let i = out.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1));
    [out[i], out[j]] = [out[j], out[i]];
  }
  return out;
}

// --- size assignment ------------------------------------------------------

// Score to maximise: how far apart the large cities are. Sum of 1/d^2 over
// every large pair is the classic repulsion energy — minimising it pushes
// them apart and, unlike "maximise the minimum distance", it keeps improving
// once no two are adjacent.
function largeEnergy(cities) {
  const large = cities.filter((c) => c.size === 'large');
  let energy = 0;
  for (let i = 0; i < large.length; i++) {
    for (let j = i + 1; j < large.length; j++) {
      const dx = large[i].location.x - large[j].location.x;
      const dy = large[i].location.y - large[j].location.y;
      energy += 1 / Math.max(dx * dx + dy * dy, 1);
    }
  }
  return energy;
}

function minLargeDistance(cities) {
  const large = cities.filter((c) => c.size === 'large');
  let min = Infinity;
  for (let i = 0; i < large.length; i++) {
    for (let j = i + 1; j < large.length; j++) {
      const dx = large[i].location.x - large[j].location.x;
      const dy = large[i].location.y - large[j].location.y;
      min = Math.min(min, Math.hypot(dx, dy));
    }
  }
  return min;
}

// Deal out sizes at the 6:3:1 ratio, then spread the large ones by swapping a
// large city's size with a non-large one whenever that lowers the energy.
// Greedy, converges in a handful of passes at this map size.
function assignSizes(cities, rng) {
  const total = cities.length;
  const largeCount = Math.round(total / 10);
  const mediumCount = Math.round((total * 3) / 10);
  const order = shuffled(cities.map((_, i) => i), rng);
  order.forEach((cityIndex, rank) => {
    cities[cityIndex].size =
      rank < largeCount ? 'large' : rank < largeCount + mediumCount ? 'medium' : 'small';
  });

  const before = minLargeDistance(cities);
  let energy = largeEnergy(cities);
  let swaps = 0;
  for (let pass = 0; pass < 200; pass++) {
    let improved = false;
    for (let i = 0; i < total; i++) {
      if (cities[i].size !== 'large') continue;
      for (let j = 0; j < total; j++) {
        if (cities[j].size === 'large') continue;
        const a = cities[i].size, b = cities[j].size;
        cities[i].size = b;
        cities[j].size = a;
        const next = largeEnergy(cities);
        if (next < energy - 1e-12) {
          energy = next;
          improved = true;
          swaps++;
          break; // city i is no longer large; move on
        }
        cities[i].size = a;
        cities[j].size = b;
      }
    }
    if (!improved) break;
  }

  return { largeCount, mediumCount, smallCount: total - largeCount - mediumCount, swaps, before, after: minLargeDistance(cities) };
}

// --- main -----------------------------------------------------------------

const map = readDmap(MAP_PATH);
const blocks = findCityBlocks(map);

const uniqueNames = new Set(NAMES);
if (uniqueNames.size !== NAMES.length) throw new Error('NAMES contains duplicates');
if (NAMES.length < blocks.length) {
  throw new Error(`need ${blocks.length} names, only have ${NAMES.length}`);
}

const rng = makeRng(SEED);
const names = shuffled(NAMES, rng);

// Sort blocks top-to-bottom, left-to-right so ids are stable across runs.
blocks.sort((a, b) => a.minY - b.minY || a.minX - b.minX);

const cities = blocks.map((block, i) => ({
  name: names[i],
  location: {
    // Block spans cells [minX..maxX]; its centre in world units.
    x: ((block.minX + block.maxX + 1) / 2) * TILE_SCALE,
    y: ((block.minY + block.maxY + 1) / 2) * TILE_SCALE,
  },
  size: 'small',
}));

const stats = assignSizes(cities, rng);

writeFileSync(OUT_PATH, JSON.stringify({ cities }, null, 2) + '\n');

const shape = new Set(blocks.map((b) => `${b.maxX - b.minX + 1}x${b.maxY - b.minY + 1}`));
console.log(`map ${map.width}x${map.height}, ${blocks.length} city blocks (shapes: ${[...shape].join(', ')})`);
console.log(`sizes: ${stats.smallCount} small / ${stats.mediumCount} medium / ${stats.largeCount} large`);
console.log(`spread: ${stats.swaps} swaps, closest large pair ${Math.round(stats.before)} -> ${Math.round(stats.after)} world units`);
console.log(`wrote ${OUT_PATH}`);
