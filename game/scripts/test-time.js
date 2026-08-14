// Checks the game-time calendar maths in src/gameTime.ts and the crew's rice
// rations in src/rations.ts:
//
//   node scripts/test-time.js
//
// Runs the real module (Node strips the TypeScript), so the calendar can't
// drift from what the game shows. No test framework — throws on the first
// failed assertion.

import { createGameTime, formatStamp, isNightHour, stampFor } from '../src/gameTime.ts';
import { createRations } from '../src/rations.ts';

// Mirrors CONFIG.gameTime (src/config.ts) — gameTime.ts takes its settings as
// arguments so this file can import it without pulling in the rest of the app.
const start = {
  startMonth: 1, startDay: 1, startHour: 8, startMinute: 0,
  nightStartHour: 20, nightEndHour: 5,
  hoursPerSecondSailing: 1, minutesPerTransaction: 10,
};
const night = (h) => isNightHour(h, start.nightStartHour, start.nightEndHour);

let passed = 0;
function check(label, condition, detail = '') {
  if (!condition) throw new Error(`FAIL: ${label}${detail ? ` — ${detail}` : ''}`);
  passed++;
  console.log(`ok  ${label}`);
}
const eq = (a, b) => JSON.stringify(a) === JSON.stringify(b);

const HOUR = 60;
const DAY = 24 * HOUR;
const MONTH = 30 * DAY;

// --- the calendar ---------------------------------------------------------

check('time starts at month 1, day 1, 00:00',
  eq(stampFor(0), { month: 1, day: 1, hour: 0, minute: 0 }));
check('minutes roll into hours', eq(stampFor(75), { month: 1, day: 1, hour: 1, minute: 15 }));
check('hours roll into days', eq(stampFor(DAY + 30), { month: 1, day: 2, hour: 0, minute: 30 }));
check('a month is 30 days', eq(stampFor(MONTH), { month: 2, day: 1, hour: 0, minute: 0 }));
check('the last day of a month is day 30', eq(stampFor(MONTH - 1).day, 30));
check('months run on past a year', eq(stampFor(14 * MONTH).month, 15));
check('the clock is 24-hour', eq(stampFor(23 * HOUR + 59).hour, 23));
check('format pads to hh:mm',
  formatStamp(stampFor(MONTH + 5 * DAY + 7 * HOUR + 5)) === 'Month 2, Day 6   07:05');

// --- night ----------------------------------------------------------------

check('night runs 20:00 to 04:59', [20, 21, 23, 0, 3, 4].every(night));
check('day runs 05:00 to 19:59', [5, 6, 12, 19].every((h) => !night(h)));

// --- advancing ------------------------------------------------------------

const time = createGameTime(start);
check('a new game starts at the configured stamp',
  eq(time.now(), { month: start.startMonth, day: start.startDay, hour: start.startHour, minute: start.startMinute }));

const before = time.totalMinutes();
time.advance(start.minutesPerTransaction);
check('a transaction costs 10 minutes', time.totalMinutes() - before === 10);

// A real second on the throttle is an hour; 24 of them is a whole day.
const dayStart = time.totalMinutes();
for (let i = 0; i < 24; i++) time.advanceHours(start.hoursPerSecondSailing);
check('24 seconds of sailing is one day', time.totalMinutes() - dayStart === DAY);

// Sailing arrives as fractional dt slices, which must not lose minutes.
const fracStart = time.totalMinutes();
for (let i = 0; i < 600; i++) time.advanceHours(1 / 60); // 600 frames at 1/60 s
check('fractional advances accumulate exactly', time.totalMinutes() - fracStart === 600,
  `${time.totalMinutes() - fracStart} minutes`);

// Idling must be free, and time must never run backwards.
const idle = time.totalMinutes();
time.advance(0);
time.advance(-500);
check('time never goes backwards', time.totalMinutes() === idle);

// --- crew rations ---------------------------------------------------------

const RATIONS = { productId: 'RICE', hoursPerUnit: 8 }; // mirrors CONFIG.rations

// A stand-in for the Player: rations.ts only needs held() and consume(), and
// consume() has to obey the same "never below 0" rule the real one does.
function fakePlayer(rice) {
  return {
    rice,
    held() { return this.rice; },
    consume(_id, qty) {
      const n = Math.min(Math.max(0, Math.floor(qty)), this.rice);
      this.rice -= n;
      return n;
    },
  };
}

function crew(rice) {
  const clock = createGameTime(start);
  const player = fakePlayer(rice);
  return { clock, player, rations: createRations(player, clock, RATIONS) };
}

{
  const { clock, player, rations } = crew(20);
  clock.advanceHours(7);
  rations.update();
  check('nothing is eaten before the first 8 hours', player.rice === 20);
  clock.advanceHours(1);
  rations.update();
  check('one rice per 8 game hours', player.rice === 19);
  check('the next ration is 8 hours out', rations.minutesToNext() === 8 * 60);
}

{
  const { clock, player, rations } = crew(20);
  clock.advanceHours(24);
  check('a single long haul eats every ration due', rations.update() === 3 && player.rice === 17);
}

{
  // Frame-by-frame sailing: 24 real seconds at 1 h/s, in 1/60 s slices.
  const { clock, player, rations } = crew(20);
  let eaten = 0;
  for (let i = 0; i < 24 * 60; i++) {
    clock.advanceHours(1 / 60);
    eaten += rations.update();
  }
  check('frame-by-frame sailing eats the same as one big jump',
    eaten === 3 && player.rice === 17, `${eaten} eaten, ${player.rice} left`);
}

{
  const { clock, player, rations } = crew(2);
  clock.advanceHours(40); // 5 rations due, only 2 aboard
  rations.update();
  check('rice never goes below 0', player.rice === 0);
  // Missed rations aren't owed: restocking starts a fresh 8-hour clock.
  player.rice = 5;
  rations.update();
  check('starving does not eat into a later restock', player.rice === 5);
  clock.advanceHours(8);
  rations.update();
  check('the crew eats again once restocked', player.rice === 4);
}

{
  const { clock, player, rations } = crew(20);
  clock.advance(10); // a market transaction
  rations.update();
  check('trading moves the clock toward the next ration',
    player.rice === 20 && rations.minutesToNext() === 8 * 60 - 10);
}

console.log(`\n${passed} checks passed`);
