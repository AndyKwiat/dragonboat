// Checks products.json and the `products` lists in cities.json against the
// market rules. Run after generate-city-products.js:
//
//   node scripts/test-products.js
//
// No test framework — it throws on the first failed assertion and prints a
// summary otherwise. The price math here deliberately mirrors src/market.ts:
// if the two ever disagree, one of them is wrong.

import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const SIZE_MULTIPLIER = { small: 1, medium: 2, large: 3 }; // keep in sync with config.ts
const BUY_MARKUP = 1.2;
const STAPLES = ['WOOD', 'RICE', 'FISH', 'FRUIT', 'SAFFRON', 'WOOL'];
const LUXURIES = ['JEWELRY', 'FURNITURE', 'CLOTHES'];
// keep in sync with generate-price-overrides.js
const MAX_OVERRIDES = 2;
const MIN_MULT = 0.5;
const MAX_MULT = 1.5;

let passed = 0;
function check(label, condition, detail = '') {
  if (!condition) throw new Error(`FAIL: ${label}${detail ? ` — ${detail}` : ''}`);
  passed++;
  console.log(`ok  ${label}`);
}

const { products } = JSON.parse(readFileSync(join(ROOT, 'products.json'), 'utf8'));
const { cities } = JSON.parse(readFileSync(join(ROOT, 'cities.json'), 'utf8'));
const ids = products.map((p) => p.id);
const catalogue = new Set(ids);
const rank = new Map(ids.map((id, i) => [id, i]));
const byId = new Map(products.map((p) => [p.id, p]));

// --- the catalogue --------------------------------------------------------

check('product ids are unique', catalogue.size === ids.length);
check('product ids are readable ALL_CAPS', ids.every((id) => /^[A-Z][A-Z_]*$/.test(id)),
  ids.filter((id) => !/^[A-Z][A-Z_]*$/.test(id)).join(', '));
check('every product has a name and description',
  products.every((p) => p.name?.length > 0 && p.description?.length > 0));
check('base prices are positive integers',
  products.every((p) => Number.isInteger(p.basePrice) && p.basePrice > 0));
check('the catalogue is exactly the staples + luxuries',
  ids.length === STAPLES.length + LUXURIES.length &&
  [...STAPLES, ...LUXURIES].every((id) => catalogue.has(id)));
// Saffron is a deliberately expensive staple, so luxuries aren't uniformly
// dearer — what matters is that the catalogue has a wide, rice-anchored spread.
const cheapest = Math.min(...products.map((p) => p.basePrice));
const dearest = Math.max(...products.map((p) => p.basePrice));
check('rice is the cheapest product', byId.get('RICE').basePrice === cheapest);
check('the dearest product is at least 10x rice', dearest >= cheapest * 10,
  `${dearest} vs ${cheapest}`);

// --- what each city stocks ------------------------------------------------

for (const c of cities) {
  const p = c.products;
  if (!Array.isArray(p) || p.length === 0) throw new Error(`FAIL: ${c.name} has no products`);
  if (new Set(p).size !== p.length) throw new Error(`FAIL: ${c.name} lists a product twice`);
  for (const id of p) {
    if (!catalogue.has(id)) throw new Error(`FAIL: ${c.name} stocks unknown product ${id}`);
  }
  const sorted = p.every((id, i) => i === 0 || rank.get(p[i - 1]) < rank.get(id));
  if (!sorted) throw new Error(`FAIL: ${c.name} products are not in catalogue order`);

  if (c.size === 'large') {
    if (p.length !== ids.length) throw new Error(`FAIL: large ${c.name} stocks ${p.length}/${ids.length} products`);
  } else if (c.size === 'medium') {
    const lux = p.filter((id) => LUXURIES.includes(id));
    if (!STAPLES.every((id) => p.includes(id)) || lux.length !== 1 || p.length !== STAPLES.length + 1) {
      throw new Error(`FAIL: medium ${c.name} stocks ${p.join(',')}`);
    }
  } else {
    const extras = p.filter((id) => id !== 'RICE');
    if (!p.includes('RICE') || p.length !== 2 || !STAPLES.includes(extras[0])) {
      throw new Error(`FAIL: small ${c.name} stocks ${p.join(',')}`);
    }
  }
}
check('every city stocks a valid, size-appropriate, ordered product list', true,
  `${cities.length} cities`);
check('every luxury is stocked somewhere beyond the large cities',
  LUXURIES.every((id) => cities.some((c) => c.size === 'medium' && c.products.includes(id))));

// --- local price overrides ------------------------------------------------

let overrides = 0;
for (const c of cities) {
  const o = c.priceOverrides;
  if (o === undefined) continue;
  const entries = Object.entries(o);
  if (entries.length === 0) throw new Error(`FAIL: ${c.name} has an empty priceOverrides`);
  if (entries.length > MAX_OVERRIDES) throw new Error(`FAIL: ${c.name} has ${entries.length} overrides`);
  if (c.size === 'small') throw new Error(`FAIL: small ${c.name} has price overrides`);
  for (const [id, m] of entries) {
    if (!c.products.includes(id)) throw new Error(`FAIL: ${c.name} overrides ${id}, which it doesn't stock`);
    if (typeof m !== 'number' || !(m >= MIN_MULT && m <= MAX_MULT)) {
      throw new Error(`FAIL: ${c.name} override ${id}=${m} is outside ${MIN_MULT}..${MAX_MULT}`);
    }
    if (m === 1) throw new Error(`FAIL: ${c.name} override ${id} is 1x, which changes nothing`);
    overrides++;
  }
}
check('overrides are 0-2 per medium/large city, on stocked products, in range', true,
  `${overrides} overrides`);
check('some cities are dear and some are cheap',
  cities.some((c) => Object.values(c.priceOverrides ?? {}).some((m) => m > 1)) &&
  cities.some((c) => Object.values(c.priceOverrides ?? {}).some((m) => m < 1)));

// --- derived prices -------------------------------------------------------

let priceChecks = 0;
for (const c of cities) {
  for (const id of c.products) {
    const base = byId.get(id).basePrice;
    const sell = Math.ceil(base * SIZE_MULTIPLIER[c.size] * (c.priceOverrides?.[id] ?? 1));
    const buy = Math.ceil(sell * BUY_MARKUP);
    if (!Number.isInteger(sell) || !Number.isInteger(buy)) throw new Error(`FAIL: ${c.name}/${id} price is not an integer`);
    if (buy <= sell) throw new Error(`FAIL: ${c.name}/${id} buy ${buy} is not above sell ${sell}`);
    if (sell < base) throw new Error(`FAIL: ${c.name}/${id} sell ${sell} is under base ${base}`);
    priceChecks++;
  }
}
check('derived prices are integers with buy > sell >= base', true, `${priceChecks} quotes`);

// A large city always beats a small one for the same product, so hauling
// upmarket is worth it.
const rice = byId.get('RICE').basePrice;
check('size multipliers order the market small < medium < large',
  Math.ceil(rice * SIZE_MULTIPLIER.small) < Math.ceil(rice * SIZE_MULTIPLIER.medium) &&
  Math.ceil(rice * SIZE_MULTIPLIER.medium) < Math.ceil(rice * SIZE_MULTIPLIER.large));

console.log(`\n${passed} checks passed`);
