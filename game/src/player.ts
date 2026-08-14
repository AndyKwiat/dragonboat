// The player's purse and cargo. Not persisted — a reload starts a fresh run
// with CONFIG.player.startingMoney.
//
// Rules: money may go negative (you can always buy), but a product count can
// never go below 0 — sells are capped at what's actually in the hold.

import { CONFIG } from './config';
import { clampCrew } from './crew';
import type { ProductId } from './products';

export interface Player {
  money: number;
  /** Hands aboard, always within CONFIG.crew's min/max. */
  crew: number;
  /** Hires or dismisses crew, clamped to the limits; returns the new count. */
  changeCrew(delta: number): number;
  /** Units held per product id. Absent id means 0; never negative. */
  inventory: Map<ProductId, number>;
  held(id: ProductId): number;
  /** Buys `qty` at `unitPrice`; returns the number actually bought. */
  buy(id: ProductId, qty: number, unitPrice: number): number;
  /** Removes up to `qty` from the hold for nothing (crew rations, spoilage);
   *  returns the number actually removed. Never takes the count below 0. */
  consume(id: ProductId, qty: number): number;
  /** Sells up to `qty`, limited by what's held; returns the number sold. */
  sell(id: ProductId, qty: number, unitPrice: number): number;
  totalUnits(): number;
}

export function createPlayer(
  startingMoney = CONFIG.player.startingMoney,
  startingInventory: Record<ProductId, number> = CONFIG.player.startingInventory,
): Player {
  const inventory = new Map<ProductId, number>(
    Object.entries(startingInventory).filter(([, n]) => n > 0),
  );
  const held = (id: ProductId) => inventory.get(id) ?? 0;

  return {
    money: startingMoney,
    crew: clampCrew(CONFIG.crew.starting, CONFIG.crew),
    changeCrew(delta) {
      this.crew = clampCrew(this.crew + delta, CONFIG.crew);
      return this.crew;
    },
    inventory,
    held,
    buy(id, qty, unitPrice) {
      const n = Math.max(0, Math.floor(qty));
      if (n === 0) return 0;
      this.money -= n * unitPrice;
      inventory.set(id, held(id) + n);
      return n;
    },
    consume(id, qty) {
      const n = Math.min(Math.max(0, Math.floor(qty)), held(id));
      if (n === 0) return 0;
      const left = held(id) - n;
      if (left === 0) inventory.delete(id);
      else inventory.set(id, left);
      return n;
    },
    sell(id, qty, unitPrice) {
      const n = this.consume(id, qty);
      this.money += n * unitPrice;
      return n;
    },
    totalUnits() {
      let total = 0;
      for (const n of inventory.values()) total += n;
      return total;
    },
  };
}
