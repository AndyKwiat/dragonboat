// The product catalogue, data-driven from products.json (bundled at build
// time, like cities.json — no fetch, no load ordering). Tweak prices and
// descriptions there; nothing about a product is hardcoded here.

import productsJson from '../products.json';

export type ProductId = string;

export interface Product {
  id: ProductId;
  name: string;
  description: string;
  basePrice: number; // what a small city pays for one unit
}

export const PRODUCTS: Product[] = (productsJson as { products: Product[] }).products;

const BY_ID = new Map(PRODUCTS.map((p) => [p.id, p]));

export function productById(id: ProductId): Product | undefined {
  return BY_ID.get(id);
}
