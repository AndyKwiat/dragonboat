import type { CameraState, ViewportInfo } from './camera/CameraController';
import { viewRect } from './camera/CameraController';
import { CONFIG } from './config';
import type { DMap } from './dmap';
import { worldSize } from './terrain';

// Bottom-right minimap: the full map compressed to a small canvas, with a
// rectangle marking the main camera's view.
export function createMinimap(map: DMap): { element: HTMLCanvasElement; update(state: CameraState, view: ViewportInfo): void } {
  const { sizePx, margin, rectColor } = CONFIG.minimap;

  // Render the static map once at native resolution (dmap rows are already
  // top-to-bottom, matching canvas orientation).
  const mapCanvas = document.createElement('canvas');
  mapCanvas.width = map.width;
  mapCanvas.height = map.height;
  const mapCtx = mapCanvas.getContext('2d')!;
  const image = mapCtx.createImageData(map.width, map.height);
  for (let i = 0; i < map.data.length; i++) {
    const color = CONFIG.terrainColors[map.data[i]!] ?? 0xff00ff;
    image.data[i * 4] = (color >> 16) & 0xff;
    image.data[i * 4 + 1] = (color >> 8) & 0xff;
    image.data[i * 4 + 2] = color & 0xff;
    image.data[i * 4 + 3] = 255;
  }
  mapCtx.putImageData(image, 0, 0);

  const dpr = window.devicePixelRatio || 1;
  const element = document.createElement('canvas');
  element.width = sizePx * dpr;
  element.height = sizePx * dpr;
  element.style.cssText = [
    'position: fixed', `right: ${margin}px`, `bottom: ${margin}px`,
    `width: ${sizePx}px`, `height: ${sizePx}px`,
    'border: 1px solid #666', 'z-index: 10',
  ].join(';');
  const ctx = element.getContext('2d')!;
  ctx.scale(dpr, dpr);
  ctx.imageSmoothingEnabled = false;

  const { w: worldW, h: worldH } = worldSize(map);

  return {
    element,
    update(state, view) {
      ctx.drawImage(mapCanvas, 0, 0, sizePx, sizePx);
      const r = viewRect(state, view);
      const x = (r.left / worldW) * sizePx;
      const y = (r.top / worldH) * sizePx;
      const w = ((r.right - r.left) / worldW) * sizePx;
      const h = ((r.bottom - r.top) / worldH) * sizePx;
      ctx.strokeStyle = rectColor;
      ctx.lineWidth = 1;
      ctx.strokeRect(
        Math.max(0.5, x), Math.max(0.5, y),
        Math.min(w, sizePx - 1), Math.min(h, sizePx - 1),
      );
    },
  };
}
