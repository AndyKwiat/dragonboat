import type { CameraState, ViewportInfo } from './camera/CameraController';
import { viewRect } from './camera/CameraController';

// Top-right readout: view corners in world coords and current zoom.
export function createHud(worldW: number, worldH: number): { element: HTMLDivElement; update(state: CameraState, view: ViewportInfo, minZoom: number, mode: string): void } {
  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 8px', 'right: 8px',
    'font: 11px monospace', 'color: #ddd',
    'background: rgba(0, 0, 0, 0.55)', 'padding: 6px 8px', 'border-radius: 3px',
    'white-space: pre', 'pointer-events: none', 'text-align: right', 'z-index: 10',
  ].join(';');

  let lastText = '';
  return {
    element,
    update(state, view, minZoom, mode) {
      const r = viewRect(state, view);
      // When letterboxed the view extends past the world; report the visible
      // world portion so coords stay within bounds.
      const clamp = (v: number, max: number) => Math.min(Math.max(v, 0), max);
      const text =
        `TL (${Math.round(clamp(r.left, worldW))}, ${Math.round(clamp(r.top, worldH))})\n` +
        `BR (${Math.round(clamp(r.right, worldW))}, ${Math.round(clamp(r.bottom, worldH))})\n` +
        `zoom ${state.zoom.toFixed(3)} px/u (${(state.zoom / minZoom).toFixed(1)}x)\n` +
        `cam ${mode} [C]`;
      if (text !== lastText) {
        element.textContent = text;
        lastText = text;
      }
    },
  };
}
