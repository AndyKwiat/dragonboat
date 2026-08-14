// Game time. The calendar is deliberately simple: 30-day months that just keep
// counting up (no years), and a 24-hour clock.
//
// Time only moves when the player does something — an hour per second of
// sailing forward, ten minutes per market transaction — so a parked boat sits
// in a frozen world. Nothing advances it per-frame on its own.
//
// Deliberately dependency-free (settings come in via createGameTime, from
// CONFIG.gameTime) so scripts/test-time.js can run this module directly.

export const MINUTES_PER_HOUR = 60;
export const HOURS_PER_DAY = 24;
export const DAYS_PER_MONTH = 30;
const MINUTES_PER_DAY = MINUTES_PER_HOUR * HOURS_PER_DAY;
const MINUTES_PER_MONTH = MINUTES_PER_DAY * DAYS_PER_MONTH;

export interface Stamp {
  month: number;  // 1-based, unbounded
  day: number;    // 1..30
  hour: number;   // 0..23
  minute: number; // 0..59
}

export interface GameTime {
  /** Whole minutes elapsed since the start of month 1, day 1, 00:00. */
  totalMinutes(): number;
  /** Advance by a (possibly fractional) number of minutes. Never goes back. */
  advance(minutes: number): void;
  advanceHours(hours: number): void;
  now(): Stamp;
  isNight(): boolean;
  /** e.g. "Month 2, Day 14   07:05" */
  format(): string;
}

export function stampFor(totalMinutes: number): Stamp {
  const t = Math.floor(totalMinutes);
  return {
    month: Math.floor(t / MINUTES_PER_MONTH) + 1,
    day: Math.floor((t % MINUTES_PER_MONTH) / MINUTES_PER_DAY) + 1,
    hour: Math.floor((t % MINUTES_PER_DAY) / MINUTES_PER_HOUR),
    minute: t % MINUTES_PER_HOUR,
  };
}

export interface GameTimeOptions {
  startMonth: number;
  startDay: number;
  startHour: number;
  startMinute: number;
  nightStartHour: number;
  nightEndHour: number;
}

// Night wraps past midnight, so the test is an OR, not a range.
export function isNightHour(hour: number, startHour: number, endHour: number): boolean {
  return hour >= startHour || hour < endHour;
}

const pad = (n: number) => String(n).padStart(2, '0');

export function formatStamp(s: Stamp): string {
  return `Month ${s.month}, Day ${s.day}   ${pad(s.hour)}:${pad(s.minute)}`;
}

export function createGameTime(options: GameTimeOptions): GameTime {
  const c = options;
  // Fractional minutes accumulate while sailing; only whole ones are shown.
  let minutes =
    (c.startMonth - 1) * MINUTES_PER_MONTH +
    (c.startDay - 1) * MINUTES_PER_DAY +
    c.startHour * MINUTES_PER_HOUR +
    c.startMinute;

  return {
    totalMinutes: () => Math.floor(minutes),
    advance(m) {
      if (m > 0) minutes += m;
    },
    advanceHours(h) {
      this.advance(h * MINUTES_PER_HOUR);
    },
    now: () => stampFor(minutes),
    isNight: () => isNightHour(stampFor(minutes).hour, c.nightStartHour, c.nightEndHour),
    format: () => formatStamp(stampFor(minutes)),
  };
}
