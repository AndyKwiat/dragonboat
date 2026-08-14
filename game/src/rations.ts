// The crew eats. Every `hoursPerUnit` of GAME time, one unit of rice leaves
// the hold — so rice is both cargo and fuel, and a long haul with a thin
// larder is a real risk.
//
// Driven by the clock rather than real time, so it only ticks while the player
// is sailing or trading. update() is called every frame from main.ts; it works
// out how many rations came due since the last check, which means one big
// time jump (a long stretch on the throttle) eats the right amount rather than
// just one unit.
//
// Running out isn't fatal yet: the hold simply stops at 0 and the missed
// rations are NOT owed later — buying rice again starts a fresh 8-hour clock
// rather than losing the units you just bought.
//
// Type-only imports, so scripts/test-time.js can run this module directly.

import type { GameTime } from './gameTime';
import type { Player } from './player';
import type { ProductId } from './products';

export interface RationsOptions {
  productId: ProductId;
  hoursPerUnit: number;
}

export interface Rations {
  /** Eats whatever is due; returns how many units were actually consumed. */
  update(): number;
  /** Units still in the hold. */
  remaining(): number;
  /** Game minutes until the next unit is eaten. */
  minutesToNext(): number;
}

export function createRations(player: Player, time: GameTime, options: RationsOptions): Rations {
  const period = Math.max(1, Math.round(options.hoursPerUnit * 60));
  let nextDue = time.totalMinutes() + period;

  return {
    update() {
      const now = time.totalMinutes();
      if (now < nextDue) return 0;
      // Catch up on every ration that came due, not just one.
      let due = 0;
      while (now >= nextDue) {
        due++;
        nextDue += period;
      }
      // consume() caps at what's held, so the hold never goes negative.
      return player.consume(options.productId, due);
    },
    remaining: () => player.held(options.productId),
    minutesToNext: () => Math.max(0, nextDue - time.totalMinutes()),
  };
}
