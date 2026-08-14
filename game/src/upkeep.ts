// Keeping a crew costs you, on GAME time rather than real time — so both of
// these only bite while the player is sailing or trading, never while the boat
// sits idle.
//
//   rations  one unit of rice out of the hold every `hoursPerUnit` hours
//   wages    one coin out of the purse every day
//
// Both work the same way: update() is called every frame from main.ts and
// works out how many charges came due since the last check, so one big time
// jump (a long stretch on the throttle) settles the whole bill rather than a
// single unit.
//
// Type-only imports, so scripts/test-time.js can run this module directly.

import type { GameTime } from './gameTime';
import type { Player } from './player';
import type { ProductId } from './products';

const MINUTES_PER_HOUR = 60;
const MINUTES_PER_DAY = 24 * MINUTES_PER_HOUR;

export interface Upkeep {
  /** Charges whatever came due; returns how many charges were applied. */
  update(): number;
  /** Game minutes until the next charge. */
  minutesToNext(): number;
}

export interface Rations extends Upkeep {
  /** Units of the ration product still in the hold. */
  remaining(): number;
}

// Counts how many periods have elapsed, catching up over big jumps. Charges
// never accrue retroactively: a period that passes is settled once, whatever
// the player could afford at the time.
function createDue(time: GameTime, periodMinutes: number) {
  const period = Math.max(1, Math.round(periodMinutes));
  let nextDue = time.totalMinutes() + period;
  return {
    take(): number {
      const now = time.totalMinutes();
      if (now < nextDue) return 0;
      let due = 0;
      while (now >= nextDue) {
        due++;
        nextDue += period;
      }
      return due;
    },
    minutesToNext: () => Math.max(0, nextDue - time.totalMinutes()),
  };
}

export interface RationsOptions {
  productId: ProductId;
  hoursPerUnit: number;
}

// The crew eats. Running out isn't fatal yet: the hold simply stops at 0 and
// the missed meals are NOT owed later — buying rice again starts a fresh
// clock rather than losing the units you just bought.
export function createRations(player: Player, time: GameTime, options: RationsOptions): Rations {
  const due = createDue(time, options.hoursPerUnit * MINUTES_PER_HOUR);
  return {
    update() {
      const n = due.take();
      // consume() caps at what's held, so the hold never goes negative.
      return n === 0 ? 0 : player.consume(options.productId, n);
    },
    remaining: () => player.held(options.productId),
    minutesToNext: due.minutesToNext,
  };
}

export interface WagesOptions {
  moneyPerDay: number;
}

// The crew gets paid, daily. Money is allowed to go negative — the debt is
// the player's problem, not a hard stop.
export function createWages(player: Player, time: GameTime, options: WagesOptions): Upkeep {
  const due = createDue(time, MINUTES_PER_DAY);
  return {
    update() {
      const days = due.take();
      if (days > 0) player.money -= days * options.moneyPerDay;
      return days;
    },
    minutesToNext: due.minutesToNext,
  };
}
