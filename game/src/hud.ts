import type { CameraRig, CameraState, ViewportInfo } from './camera/CameraController';
import { viewRect } from './camera/CameraController';

// Top-right readout: view corners in world coords, current zoom, camera mode
// and how fast the zoom makes sailing time run. Money and cargo live in the
// top-left clock overlay instead.
export function createHud(worldW: number, worldH: number): { element: HTMLDivElement; update(state: CameraState, view: ViewportInfo, rig: CameraRig, minZoom: number, mode: string, timeScale: number, chunks: { visible: number; cached: number; step: number; pending: number }): void } {
  const element = document.createElement('div');
  element.style.cssText = [
    'position: fixed', 'top: 8px', 'right: 8px',
    'font: 11px monospace', 'color: #ddd',
    'background: rgba(0, 0, 0, 0.55)', 'padding: 6px 8px', 'border-radius: 3px',
    'white-space: pre', 'pointer-events: none', 'text-align: right', 'z-index: 10',
  ].join(';');

  let lastText = '';
  // FPS: frames counted over the last ~half second, refreshed on the boundary
  // so the readout is stable enough to read.
  let fpsFrames = 0;
  let fpsWindowStart = performance.now();
  let fps = 0;
  return {
    element,
    update(state, view, rig, minZoom, mode, timeScale, chunks) {
      fpsFrames++;
      const now = performance.now();
      if (now - fpsWindowStart >= 500) {
        fps = (fpsFrames * 1000) / (now - fpsWindowStart);
        fpsFrames = 0;
        fpsWindowStart = now;
      }
      const r = viewRect(state, view, rig);
      // When letterboxed the view extends past the world; report the visible
      // world portion so coords stay within bounds.
      const clamp = (v: number, max: number) => Math.min(Math.max(v, 0), max);
      const text =
        `${fps.toFixed(0)} fps\n` +
        `TL (${Math.round(clamp(r.left, worldW))}, ${Math.round(clamp(r.top, worldH))})\n` +
        `BR (${Math.round(clamp(r.right, worldW))}, ${Math.round(clamp(r.bottom, worldH))})\n` +
        `zoom ${state.zoom.toFixed(3)} px/u (${(state.zoom / minZoom).toFixed(1)}x)\n` +
        `cam ${mode} [C]  rig ${rig.name} [V]\n` +
        `sail time x${timeScale.toFixed(2)}\n` +
        `chunks ${chunks.visible} vis / ${chunks.cached} cached, lod x${chunks.step}${chunks.pending ? ` (+${chunks.pending} building)` : ''}`;
      if (text !== lastText) {
        element.textContent = text;
        lastText = text;
      }
    },
  };
}
