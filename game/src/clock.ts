// Top-left overlay: the game date and time, then the two things that drain as
// it passes — the crew's rice and the purse. Plain DOM, updated via a text
// diff like the HUD. The font dims at night (CONFIG.gameTime.nightColor) so
// you can feel the hours going by while you sail; rice goes red when the
// larder runs low, money when it goes into debt.

import { CONFIG } from './config';
import type { GameTime } from './gameTime';
import type { Player } from './player';
import type { Rations } from './upkeep';

export interface Clock {
  element: HTMLDivElement;
  update(): void;
}

export function createClock(time: GameTime, rations: Rations, player: Player): Clock {
  const c = CONFIG.gameTime;
  const r = CONFIG.rations;

  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 8px', 'left: 8px',
    `font: 600 ${c.fontPx}px monospace`,
    'background: rgba(0, 0, 0, 0.55)', 'padding: 6px 10px', 'border-radius: 3px',
    'white-space: pre', 'pointer-events: none', 'z-index: 10',
  ].join(';');

  const stamp = document.createElement('div');
  const rice = document.createElement('div');
  const money = document.createElement('div');
  const smallFont = `font-size: ${c.fontPx - 4}px`;
  rice.style.cssText = smallFont;
  money.style.cssText = smallFont;
  element.append(stamp, rice, money);

  let lastStamp = '';
  let lastRice = '';
  let lastMoney = '';
  let lastNight: boolean | null = null;
  let lastLowRice: boolean | null = null;
  let lastInDebt: boolean | null = null;

  return {
    element,
    update() {
      const night = time.isNight();
      const units = rations.remaining();
      const lowRice = units < r.lowUnits;
      const inDebt = player.money < 0;

      const text = time.format();
      if (text !== lastStamp) {
        stamp.textContent = text;
        lastStamp = text;
      }
      const riceText = `rice  ${units}`;
      if (riceText !== lastRice) {
        rice.textContent = riceText;
        lastRice = riceText;
      }
      const moneyText = `money ${player.money}`;
      if (moneyText !== lastMoney) {
        money.textContent = moneyText;
        lastMoney = moneyText;
      }

      if (night !== lastNight || lowRice !== lastLowRice || inDebt !== lastInDebt) {
        const normal = night ? c.nightColor : c.dayColor;
        stamp.style.color = normal;
        // A warning outranks the night dimming.
        rice.style.color = lowRice ? r.lowColor : normal;
        money.style.color = inDebt ? r.lowColor : normal;
        lastNight = night;
        lastLowRice = lowRice;
        lastInDebt = inDebt;
      }
    },
  };
}
