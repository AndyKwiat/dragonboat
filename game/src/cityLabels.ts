import type { CameraState, ViewportInfo } from './camera/CameraController';
import type { City } from './cities';
import { CONFIG } from './config';

// DOM overlay drawing a name label for every city currently on screen, plus
// the hit-testing for hover/click. The layer itself never takes pointer
// events (dragging the map through a label must keep working) — instead we
// hit-test the cursor against city positions ourselves, which is also what
// makes the target forgiving: a hit is "within hitRadiusPx of the city
// centre, OR inside the label box padded by hitPadPx".

const FONT_STACK = 'system-ui, -apple-system, "Segoe UI", sans-serif';

interface LabelEntry {
  city: City;
  element: HTMLDivElement;
  halfWidthPx: number;
  heightPx: number;
  minZoom: number;
  visible: boolean;
  screenX: number;
  screenY: number;
  hovered: boolean;
}

export interface CityLabels {
  element: HTMLDivElement;
  /** Positions visible labels and returns the city under the cursor, if any. */
  update(state: CameraState, view: ViewportInfo, cursorPx: { x: number; y: number }): City | null;
}

function measureText(text: string, font: string): number {
  const canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')!;
  ctx.font = font;
  return ctx.measureText(text).width;
}

export function createCityLabels(cities: City[]): CityLabels {
  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'inset: 0',
    'pointer-events: none', 'overflow: hidden', 'z-index: 5',
  ].join(';');

  const entries: LabelEntry[] = cities.map((city) => {
    const style = CONFIG.cities.label[city.size];
    const font = `${style.weight} ${style.fontPx}px ${FONT_STACK}`;
    const label = document.createElement('div');
    label.textContent = city.name;
    label.style.cssText = [
      'position: absolute', 'transform: translateX(-50%)',
      `font: ${font}`, `color: ${style.color}`,
      'text-shadow: 0 1px 2px rgba(0,0,0,0.9), 0 0 4px rgba(0,0,0,0.7)',
      'white-space: nowrap', 'display: none',
    ].join(';');
    element.appendChild(label);
    return {
      city,
      element: label,
      halfWidthPx: measureText(city.name, font) / 2,
      heightPx: style.fontPx * 1.3,
      minZoom: style.minZoom,
      visible: false,
      screenX: 0,
      screenY: 0,
      hovered: false,
    };
  });

  function setHovered(entry: LabelEntry, hovered: boolean) {
    if (entry.hovered === hovered) return;
    entry.hovered = hovered;
    entry.element.style.color = hovered
      ? CONFIG.cities.hoverColor
      : CONFIG.cities.label[entry.city.size].color;
    entry.element.style.textDecoration = hovered ? 'underline' : 'none';
  }

  return {
    element,

    update(state, view, cursorPx) {
      const { labelOffsetPx, hitRadiusPx, hitPadPx } = CONFIG.cities;
      const halfW = view.widthPx / (2 * state.zoom);
      const halfH = view.heightPx / (2 * state.zoom);
      const margin = 200; // px of slack so labels don't pop at the edges

      let best: LabelEntry | null = null;
      let bestDistance = Infinity;

      for (const entry of entries) {
        const screenX = (entry.city.location.x - (state.centerX - halfW)) * state.zoom;
        const screenY = (entry.city.location.y - (state.centerY - halfH)) * state.zoom;
        // Smaller cities only earn a label once zoomed in past their
        // threshold; hidden labels are also unclickable.
        const visible =
          state.zoom >= entry.minZoom &&
          screenX > -margin && screenX < view.widthPx + margin &&
          screenY > -margin && screenY < view.heightPx + margin;

        if (visible !== entry.visible) {
          entry.element.style.display = visible ? 'block' : 'none';
          entry.visible = visible;
        }
        if (!visible) {
          setHovered(entry, false);
          continue;
        }

        entry.screenX = screenX;
        entry.screenY = screenY;
        entry.element.style.left = `${screenX}px`;
        entry.element.style.top = `${screenY + labelOffsetPx}px`;

        // Forgiving hit test: circle around the city centre, or the padded
        // label box below it. Nearest city centre wins if several overlap.
        const dx = cursorPx.x - screenX;
        const dy = cursorPx.y - screenY;
        const inCircle = dx * dx + dy * dy <= hitRadiusPx * hitRadiusPx;
        const inLabel =
          Math.abs(dx) <= entry.halfWidthPx + hitPadPx &&
          dy >= labelOffsetPx - hitPadPx &&
          dy <= labelOffsetPx + entry.heightPx + hitPadPx;
        if (inCircle || inLabel) {
          const distance = dx * dx + dy * dy;
          if (distance < bestDistance) {
            bestDistance = distance;
            best = entry;
          }
        }
      }

      for (const entry of entries) setHovered(entry, entry === best);
      return best ? best.city : null;
    },
  };
}
