import type { CameraState, ViewportInfo } from './camera/CameraController';
import { viewRect } from './camera/CameraController';
import { CONFIG } from './config';
import type { DMap } from './dmap';
import { worldSize } from './terrain';

// Bottom-right minimap: a window onto part of the map (worldFraction of the
// world's width/height) that follows the camera, with a rectangle marking the
// main camera's view. The window is clamped to the world, so near an edge it
// stops scrolling instead of showing blank space.
export function createMinimap(map: DMap): { element: HTMLCanvasElement; update(state: CameraState, view: ViewportInfo): void } {
  const { sizePx, margin, rectColor, worldFraction } = CONFIG.minimap;

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

  // Same fraction on both axes, so the minimap keeps the world's aspect (the
  // canvas is square because the world is). Never larger than the world.
  const windowW = Math.min(worldW * worldFraction, worldW);
  const windowH = Math.min(worldH * worldFraction, worldH);

  const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), hi);

  return {
    element,
    update(state, view) {
      // Window in world coords, centred on the camera and clamped to bounds.
      const left = clamp(state.centerX - windowW / 2, 0, worldW - windowW);
      const top = clamp(state.centerY - windowH / 2, 0, worldH - windowH);

      // World coords -> source pixels in the cached map image (1 px per cell).
      const sx = (left / worldW) * map.width;
      const sy = (top / worldH) * map.height;
      const sw = (windowW / worldW) * map.width;
      const sh = (windowH / worldH) * map.height;
      ctx.drawImage(mapCanvas, sx, sy, sw, sh, 0, 0, sizePx, sizePx);

      // View rect, clipped to the window: when zoomed out far enough the view
      // is bigger than the window and the rect becomes the minimap's border.
      const r = viewRect(state, view);
      const toX = (worldX: number) => clamp(((worldX - left) / windowW) * sizePx, 0, sizePx);
      const toY = (worldY: number) => clamp(((worldY - top) / windowH) * sizePx, 0, sizePx);
      const x0 = toX(r.left), x1 = toX(r.right);
      const y0 = toY(r.top), y1 = toY(r.bottom);
      ctx.strokeStyle = rectColor;
      ctx.lineWidth = 1;
      ctx.strokeRect(
        clamp(x0, 0.5, sizePx - 0.5), clamp(y0, 0.5, sizePx - 0.5),
        Math.max(x1 - x0 - 1, 1), Math.max(y1 - y0 - 1, 1),
      );
    },
  };
}
