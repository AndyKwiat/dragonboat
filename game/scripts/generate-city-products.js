// One-off generator: fills in each city's `products` list in cities.json from
// the catalogue in products.json.
//
//   node scripts/generate-city-products.js
//
// What each city stocks is fixed by its size:
//   large   every product
//   medium  the six staples + ONE luxury (jewelry / furniture / clothes)
//   small   rice + ONE other staple
//
// Prices are NOT stored here — they're derived at runtime from the product's
// basePrice and the city size (see src/market.ts). Deterministic: the fixed
// PRNG seed means re-running produces the same file. Rewrites cities.json in
// place, leaving name/location/size untouched.

import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const CITIES_PATH = join(ROOT, 'cities.json');
const PRODUCTS_PATH = join(ROOT, 'products.json');
const SEED = 0x9a17c0de;

// Keep in sync with the rules above; ids must exist in products.json.
const STAPLES = ['WOOD', 'RICE', 'FISH', 'FRUIT', 'SAFFRON', 'WOOL'];
const LUXURIES = ['JEWELRY', 'FURNITURE', 'CLOTHES'];
const SMALL_ALWAYS = 'RICE';
const SMALL_EXTRAS = STAPLES.filter((id) => id !== SMALL_ALWAYS);

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

const { products } = JSON.parse(readFileSync(PRODUCTS_PATH, 'utf8'));
const catalogue = products.map((p) => p.id);
const order = new Map(catalogue.map((id, i) => [id, i]));

for (const id of [...STAPLES, ...LUXURIES]) {
  if (!order.has(id)) throw new Error(`${id} is not a product id in products.json`);
}
if (catalogue.length !== STAPLES.length + LUXURIES.length) {
  throw new Error(`products.json has ${catalogue.length} products, expected ${STAPLES.length + LUXURIES.length}`);
}

const cities = JSON.parse(readFileSync(CITIES_PATH, 'utf8')).cities;
const rng = makeRng(SEED);
const pick = (list) => list[Math.floor(rng() * list.length)];

// Listed in catalogue order so the market panel reads the same everywhere.
const sortIds = (ids) => ids.slice().sort((a, b) => order.get(a) - order.get(b));

const counts = { small: 0, medium: 0, large: 0 };
const withProducts = cities.map((city) => {
  let ids;
  if (city.size === 'large') ids = catalogue.slice();
  else if (city.size === 'medium') ids = [...STAPLES, pick(LUXURIES)];
  else if (city.size === 'small') ids = [SMALL_ALWAYS, pick(SMALL_EXTRAS)];
  else throw new Error(`${city.name} has unknown size ${city.size}`);
  counts[city.size]++;
  // Rebuilt (not mutated) so `products` always lands after `size` in the JSON.
  return { name: city.name, location: city.location, size: city.size, products: sortIds(ids) };
});

writeFileSync(CITIES_PATH, JSON.stringify({ cities: withProducts }, null, 2) + '\n');

const tally = {};
for (const c of withProducts) for (const id of c.products) tally[id] = (tally[id] ?? 0) + 1;
console.log(`${withProducts.length} cities: ${counts.small} small / ${counts.medium} medium / ${counts.large} large`);
console.log(`products per city: small 2, medium ${STAPLES.length + 1}, large ${catalogue.length}`);
console.log('cities stocking each product:', tally);
console.log(`wrote ${CITIES_PATH}`);
