// Top-left overlay showing the game date and time. Plain DOM, updated via a
// text diff like the HUD. The font dims at night (CONFIG.gameTime.nightColor)
// so you can feel the hours going by while you sail.

import { CONFIG } from './config';
import type { GameTime } from './gameTime';

export interface Clock {
  element: HTMLDivElement;
  update(): void;
}

export function createClock(time: GameTime): Clock {
  const c = CONFIG.gameTime;
  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 8px', 'left: 8px',
    `font: 600 ${c.fontPx}px monospace`,
    'background: rgba(0, 0, 0, 0.55)', 'padding: 6px 10px', 'border-radius: 3px',
    'white-space: pre', 'pointer-events: none', 'z-index: 10',
    `color: ${c.dayColor}`,
  ].join(';');

  let lastText = '';
  let lastNight: boolean | null = null;

  return {
    element,
    update() {
      const text = time.format();
      if (text !== lastText) {
        element.textContent = text;
        lastText = text;
      }
      const night = time.isNight();
      if (night !== lastNight) {
        element.style.color = night ? c.nightColor : c.dayColor;
        lastNight = night;
      }
    },
  };
}
