// Top-left overlay: the game date and time, with the crew's remaining rice
// underneath. Plain DOM, updated via a text diff like the HUD. The font dims
// at night (CONFIG.gameTime.nightColor) so you can feel the hours going by
// while you sail; the rice line goes red when the larder runs low.

import { CONFIG } from './config';
import type { GameTime } from './gameTime';
import type { Rations } from './rations';

export interface Clock {
  element: HTMLDivElement;
  update(): void;
}

export function createClock(time: GameTime, rations: Rations): Clock {
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
  rice.style.cssText = `font-size: ${c.fontPx - 4}px`;
  element.append(stamp, rice);

  let lastText = '';
  let lastRice = '';
  let lastNight: boolean | null = null;
  let lastLow: boolean | null = null;

  return {
    element,
    update() {
      const night = time.isNight();
      const units = rations.remaining();
      const low = units < r.lowUnits;

      const text = time.format();
      if (text !== lastText) {
        stamp.textContent = text;
        lastText = text;
      }
      const riceText = `rice ${units}`;
      if (riceText !== lastRice) {
        rice.textContent = riceText;
        lastRice = riceText;
      }
      if (night !== lastNight || low !== lastLow) {
        stamp.style.color = night ? c.nightColor : c.dayColor;
        // A low larder outranks the night dimming — it's a warning.
        rice.style.color = low ? r.lowColor : night ? c.nightColor : c.dayColor;
        lastNight = night;
        lastLow = low;
      }
    },
  };
}
