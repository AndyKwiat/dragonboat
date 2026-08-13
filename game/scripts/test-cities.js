// Checks cities.json against public/map.dmap. Run after generate-cities.js:
//
//   node scripts/test-cities.js
//
// No test framework — it throws on the first failed assertion and prints a
// summary otherwise.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const TILE_SCALE = 20;
const CITY = 7;

let passed = 0;
function check(label, condition, detail = '') {
  if (!condition) throw new Error(`FAIL: ${label}${detail ? ` — ${detail}` : ''}`);
  passed++;
  console.log(`ok  ${label}`);
}

const buf = readFileSync(join(ROOT, 'public', 'map.dmap'));
const width = buf.readUInt32LE(4);
const height = buf.readUInt32LE(8);
const data = buf.subarray(12);
const { cities } = JSON.parse(readFileSync(join(ROOT, 'cities.json'), 'utf8'));

// 1. Every city sits on a city tile, and its location is a 5x5 block centre.
let cityTiles = 0;
for (const v of data) if (v === CITY) cityTiles++;
check('one city per 5x5 block of city tiles', cities.length === cityTiles / 25,
  `${cities.length} cities vs ${cityTiles} tiles`);

const covered = new Set();
for (const c of cities) {
  const cx = c.location.x / TILE_SCALE - 0.5; // centre cell
  const cy = c.location.y / TILE_SCALE - 0.5;
  if (!Number.isInteger(cx) || !Number.isInteger(cy)) {
    throw new Error(`FAIL: ${c.name} location ${c.location.x},${c.location.y} is not a 5x5 block centre`);
  }
  for (let y = cy - 2; y <= cy + 2; y++) {
    for (let x = cx - 2; x <= cx + 2; x++) {
      if (x < 0 || y < 0 || x >= width || y >= height || data[y * width + x] !== CITY) {
        throw new Error(`FAIL: ${c.name} block cell ${x},${y} is not city terrain`);
      }
      covered.add(y * width + x);
    }
  }
}
check('every city block is 5x5 city terrain', true);
check('cities cover every city tile exactly once', covered.size === cityTiles,
  `covered ${covered.size} of ${cityTiles}`);

// 2. Names.
const names = cities.map((c) => c.name);
check('names are unique', new Set(names).size === names.length);
check('names are non-empty strings', names.every((n) => typeof n === 'string' && n.trim().length > 0));

// 3. Size ratio 6:3:1.
const count = (s) => cities.filter((c) => c.size === s).length;
const [small, medium, large] = [count('small'), count('medium'), count('large')];
check('sizes are small/medium/large only', small + medium + large === cities.length);
check('ratio is 6:3:1', large === Math.round(cities.length / 10) && medium === Math.round(cities.length * 0.3),
  `${small}/${medium}/${large}`);

// 4. Large cities are spread out: their closest pair must beat what a random
//    assignment typically manages (median over 200 random draws).
const dist = (a, b) => Math.hypot(a.location.x - b.location.x, a.location.y - b.location.y);
const minPairDistance = (list) => {
  let min = Infinity;
  for (let i = 0; i < list.length; i++) {
    for (let j = i + 1; j < list.length; j++) min = Math.min(min, dist(list[i], list[j]));
  }
  return min;
};
const actual = minPairDistance(cities.filter((c) => c.size === 'large'));
const samples = [];
for (let trial = 0; trial < 200; trial++) {
  const pool = cities.slice();
  for (let i = pool.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [pool[i], pool[j]] = [pool[j], pool[i]];
  }
  samples.push(minPairDistance(pool.slice(0, large)));
}
samples.sort((a, b) => a - b);
const median = samples[Math.floor(samples.length / 2)];
check('large cities are spread out', actual > median,
  `closest large pair ${Math.round(actual)} vs random median ${Math.round(median)}`);

console.log(`\n${passed} checks passed for ${cities.length} cities (${small} small / ${medium} medium / ${large} large)`);
