// City markets: prices are derived, never stored. cities.json only lists which
// product ids a city trades (see scripts/generate-city-products.js) plus any
// local price quirks; the price comes from the product's basePrice scaled by
// the city size and that city's override multiplier.
//
// Every price is a whole number — always rounded UP, so the house wins the
// rounding on both sides of the trade.

import type { City, CitySize } from './cities';
import { CONFIG } from './config';
import type { Product, ProductId } from './products';
import { productById } from './products';

export interface Quote {
  product: Product;
  sellPrice: number; // what the city pays the player per unit
  buyPrice: number;  // what the player pays the city per unit
  /** This city's local multiplier: 1 normally, >1 dear, <1 cheap. */
  priceMultiplier: number;
}

export function sizeMultiplier(size: CitySize): number {
  return CONFIG.market.sizeMultiplier[size];
}

export function overrideFor(city: City, id: ProductId): number {
  return city.priceOverrides?.[id] ?? 1;
}

// The city's offer for one unit: base * size multiplier * local quirk.
export function sellPriceFor(product: Product, size: CitySize, override = 1): number {
  return Math.ceil(product.basePrice * sizeMultiplier(size) * override);
}

// Buying costs a markup over what the same city would pay you.
export function buyPriceFor(product: Product, size: CitySize, override = 1): number {
  return Math.ceil(sellPriceFor(product, size, override) * CONFIG.market.buyMarkup);
}

export function quoteFor(product: Product, size: CitySize, override = 1): Quote {
  return {
    product,
    sellPrice: sellPriceFor(product, size, override),
    buyPrice: buyPriceFor(product, size, override),
    priceMultiplier: override,
  };
}

// The city's market, in products.json order. Unknown ids are skipped so a
// stale cities.json can't crash the panel.
export function marketFor(city: City): Quote[] {
  const quotes: Quote[] = [];
  for (const id of city.products ?? []) {
    const product = productById(id);
    if (product) quotes.push(quoteFor(product, city.size, overrideFor(city, id)));
  }
  return quotes;
}

export function trades(city: City, id: ProductId): boolean {
  return (city.products ?? []).includes(id);
}
