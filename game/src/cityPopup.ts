import type { City } from './cities';

// Closable panel describing the clicked city. To show more about a city
// later, add an entry to FIELDS — the panel renders whatever is in the list.
const FIELDS: Array<{ label: string; value(city: City): string }> = [
  { label: 'coords', value: (c) => `${Math.round(c.location.x)}, ${Math.round(c.location.y)}` },
  { label: 'size', value: (c) => c.size },
];

export interface CityPopup {
  element: HTMLDivElement;
  show(city: City): void;
  hide(): void;
  isOpen(): boolean;
}

export function createCityPopup(): CityPopup {
  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 8px', 'left: 8px',
    'min-width: 180px',
    'font: 12px monospace', 'color: #ddd',
    'background: rgba(0, 0, 0, 0.8)', 'border: 1px solid #555', 'border-radius: 4px',
    'padding: 8px 10px', 'z-index: 20', 'display: none',
  ].join(';');

  const header = document.createElement('div');
  header.style.cssText = 'display: flex; align-items: baseline; gap: 12px; margin-bottom: 6px';

  const title = document.createElement('div');
  title.style.cssText = 'font: 600 14px system-ui, sans-serif; color: #fff; flex: 1';

  const close = document.createElement('button');
  close.textContent = '✕';
  close.title = 'Close (Esc)';
  close.style.cssText = [
    'all: unset', 'cursor: pointer', 'color: #aaa',
    'font: 12px monospace', 'line-height: 1', 'padding: 2px 4px',
  ].join(';');
  close.addEventListener('click', () => hide());
  close.addEventListener('pointerdown', (e) => e.stopPropagation());

  header.append(title, close);

  const body = document.createElement('div');
  body.style.cssText = 'white-space: pre; line-height: 1.5';

  element.append(header, body);

  function hide() {
    element.style.display = 'none';
  }

  return {
    element,
    show(city) {
      title.textContent = city.name;
      body.textContent = FIELDS.map((f) => `${f.label.padEnd(7)}${f.value(city)}`).join('\n');
      element.style.display = 'block';
    },
    hide,
    isOpen: () => element.style.display !== 'none',
  };
}
