// The player's purse and cargo. Not persisted — a reload starts a fresh run
// with CONFIG.player.startingMoney.
//
// Rules: money may go negative (you can always buy), but a product count can
// never go below 0 — sells are capped at what's actually in the hold.

import { CONFIG } from './config';
import type { ProductId } from './products';

export interface Player {
  money: number;
  /** Units held per product id. Absent id means 0; never negative. */
  inventory: Map<ProductId, number>;
  held(id: ProductId): number;
  /** Buys `qty` at `unitPrice`; returns the number actually bought. */
  buy(id: ProductId, qty: number, unitPrice: number): number;
  /** Sells up to `qty`, limited by what's held; returns the number sold. */
  sell(id: ProductId, qty: number, unitPrice: number): number;
  totalUnits(): number;
}

export function createPlayer(startingMoney = CONFIG.player.startingMoney): Player {
  const inventory = new Map<ProductId, number>();
  const held = (id: ProductId) => inventory.get(id) ?? 0;

  return {
    money: startingMoney,
    inventory,
    held,
    buy(id, qty, unitPrice) {
      const n = Math.max(0, Math.floor(qty));
      if (n === 0) return 0;
      this.money -= n * unitPrice;
      inventory.set(id, held(id) + n);
      return n;
    },
    sell(id, qty, unitPrice) {
      const n = Math.min(Math.max(0, Math.floor(qty)), held(id));
      if (n === 0) return 0;
      this.money += n * unitPrice;
      const left = held(id) - n;
      if (left === 0) inventory.delete(id);
      else inventory.set(id, left);
      return n;
    },
    totalUnits() {
      let total = 0;
      for (const n of inventory.values()) total += n;
      return total;
    },
  };
}
