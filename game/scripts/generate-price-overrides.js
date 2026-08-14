// One-off generator: gives medium and large cities a few local price quirks.
//
//   node scripts/generate-price-overrides.js
//
// Each medium/large city gets 0-2 `priceOverrides` entries — a multiplier
// between 0.5 and 1.5 applied on top of the size multiplier, so a city can be
// the place to dump wool or the place to stock up on it. Only products the
// city actually stocks can be overridden. Small cities are left alone: they
// always trade at flat base price.
//
// Must run AFTER generate-city-products.js (it needs the product lists, and
// that script rewrites each city object). Deterministic: fixed PRNG seed.
// Rewrites cities.json in place.

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const CITIES_PATH = join(ROOT, 'cities.json');
const SEED = 0xc0ffee42;

const SIZES = ['medium', 'large'];   // sizes that get overrides at all
const MAX_OVERRIDES = 2;
const MIN_MULT = 0.5;
const MAX_MULT = 1.5;
const STEP = 0.05;                   // multipliers snap to this, so they read cleanly

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

const rng = makeRng(SEED);

// A multiplier in [MIN_MULT, MAX_MULT] snapped to STEP, never exactly 1 — an
// override that changes nothing is just noise in the file.
function multiplier() {
  const steps = Math.round((MAX_MULT - MIN_MULT) / STEP);
  for (;;) {
    const m = MIN_MULT + Math.floor(rng() * (steps + 1)) * STEP;
    const rounded = Math.round(m * 100) / 100;
    if (rounded !== 1) return rounded;
  }
}

const cities = JSON.parse(readFileSync(CITIES_PATH, 'utf8')).cities;

let overrideCount = 0, cheap = 0, dear = 0;
const out = cities.map((city) => {
  const { priceOverrides: _drop, ...rest } = city;
  if (!SIZES.includes(city.size)) return rest;

  const count = Math.floor(rng() * (MAX_OVERRIDES + 1));
  const pool = city.products.slice();
  const overrides = {};
  for (let i = 0; i < count && pool.length; i++) {
    const [id] = pool.splice(Math.floor(rng() * pool.length), 1);
    const m = multiplier();
    overrides[id] = m;
    overrideCount++;
    if (m < 1) cheap++; else dear++;
  }
  // Omit the key entirely when a city has no quirks.
  return count && Object.keys(overrides).length ? { ...rest, priceOverrides: overrides } : rest;
});

writeFileSync(CITIES_PATH, JSON.stringify({ cities: out }, null, 2) + '\n');

const eligible = out.filter((c) => SIZES.includes(c.size)).length;
const withOverrides = out.filter((c) => c.priceOverrides).length;
console.log(`${eligible} medium/large cities, ${withOverrides} of them quirky`);
console.log(`${overrideCount} overrides: ${cheap} cheap (<1), ${dear} expensive (>1)`);
console.log(`wrote ${CITIES_PATH}`);
