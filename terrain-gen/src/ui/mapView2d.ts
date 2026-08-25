/** 2D overhead map: a map-sized backing canvas (tile colors or greyscale
 *  heights) drawn with pan/zoom onto the visible canvas. Left-drag emits
 *  brush stroke events in map pixel coords; right/middle-drag pans; wheel
 *  zooms about the cursor. Dirty regions are re-rasterized lazily. */
import { TILE_COLORS } from "../core/types";
import { HEIGHT_MAX, HEIGHT_MIN } from "../core/paint";
import type { Bbox } from "../core/paint";

export type ViewMode = "tiles" | "height";

export interface MapView2DHost {
  size: number;
  tiles: Uint8Array;
  height: Float32Array;
  onStroke(kind: "start" | "move" | "end", x: number, y: number): void;
}

const H_MIN = HEIGHT_MIN, H_MAX = HEIGHT_MAX; // greyscale spans the full valid range
/** In height mode the tile map shows through the greyscale at this opacity
 *  so tile borders stay visible while sculpting. */
const TILE_GHOST = 0.3;

export class MapView2D {
  mode: ViewMode = "tiles";
  brushRadius = 40; // map px, for the cursor ring
  private canvas: HTMLCanvasElement;
  private ctx: CanvasRenderingContext2D;
  private back: HTMLCanvasElement;
  private bctx: CanvasRenderingContext2D;
  private host: MapView2DHost;
  private scale = 1; private tx = 0; private ty = 0;
  private dirty: Bbox | null = null; private dirtyAll = true;
  private panning = false; private painting = false;
  private lastClient = { x: 0, y: 0 };
  private cursor: { x: number; y: number } | null = null;
  private spaceDown = false;

  constructor(canvas: HTMLCanvasElement, host: MapView2DHost) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d")!;
    this.host = host;
    this.back = document.createElement("canvas");
    this.back.width = this.back.height = host.size;
    this.bctx = this.back.getContext("2d")!;
    canvas.addEventListener("pointerdown", this.onDown);
    canvas.addEventListener("pointermove", this.onMove);
    canvas.addEventListener("pointerup", this.onUp);
    canvas.addEventListener("pointerleave", () => { this.cursor = null; });
    canvas.addEventListener("wheel", this.onWheel, { passive: false });
    canvas.addEventListener("contextmenu", (e) => e.preventDefault());
    addEventListener("keydown", (e: KeyboardEvent) => { if (e.code === "Space") this.spaceDown = true; });
    addEventListener("keyup", (e: KeyboardEvent) => { if (e.code === "Space") this.spaceDown = false; });
    new ResizeObserver(() => this.resize()).observe(canvas.parentElement!);
    this.resize();
    this.fit();
    requestAnimationFrame(this.frame);
  }

  /** Swap backing canvas after a map-size change. */
  resetSize(): void {
    this.back.width = this.back.height = this.host.size;
    this.dirtyAll = true;
    this.fit();
  }

  setMode(m: ViewMode): void { this.mode = m; this.dirtyAll = true; }
  markDirty(b: Bbox | null): void {
    if (!b) { this.dirtyAll = true; return; }
    if (this.dirtyAll) return;
    this.dirty = this.dirty
      ? { x0: Math.min(this.dirty.x0, b.x0), y0: Math.min(this.dirty.y0, b.y0), x1: Math.max(this.dirty.x1, b.x1), y1: Math.max(this.dirty.y1, b.y1) }
      : { ...b };
  }

  private resize(): void {
    const p = this.canvas.parentElement!;
    const w = p.clientWidth, h = p.clientHeight;
    if (w === 0 || h === 0) return;
    this.canvas.width = w * devicePixelRatio; this.canvas.height = h * devicePixelRatio;
    this.canvas.style.width = w + "px"; this.canvas.style.height = h + "px";
  }

  fit(): void {
    const w = this.canvas.width, h = this.canvas.height;
    this.scale = Math.min(w, h) / this.host.size * 0.95;
    this.tx = (w - this.host.size * this.scale) / 2;
    this.ty = (h - this.host.size * this.scale) / 2;
  }

  /** client (css px) -> map px */
  toMap(cx: number, cy: number): { x: number; y: number } {
    const r = this.canvas.getBoundingClientRect();
    const px = (cx - r.left) * devicePixelRatio, py = (cy - r.top) * devicePixelRatio;
    return { x: (px - this.tx) / this.scale, y: (py - this.ty) / this.scale };
  }

  private onDown = (e: PointerEvent): void => {
    this.canvas.setPointerCapture(e.pointerId);
    this.lastClient = { x: e.clientX, y: e.clientY };
    if (e.button === 1 || e.button === 2 || this.spaceDown) { this.panning = true; return; }
    if (e.button === 0) {
      this.painting = true;
      const m = this.toMap(e.clientX, e.clientY);
      this.host.onStroke("start", m.x, m.y);
    }
  };
  private onMove = (e: PointerEvent): void => {
    const m = this.toMap(e.clientX, e.clientY);
    this.cursor = m;
    if (this.panning) {
      this.tx += (e.clientX - this.lastClient.x) * devicePixelRatio;
      this.ty += (e.clientY - this.lastClient.y) * devicePixelRatio;
      this.lastClient = { x: e.clientX, y: e.clientY };
    } else if (this.painting) {
      this.host.onStroke("move", m.x, m.y);
    }
  };
  private onUp = (e: PointerEvent): void => {
    if (this.painting) {
      const m = this.toMap(e.clientX, e.clientY);
      this.host.onStroke("end", m.x, m.y);
    }
    this.painting = false; this.panning = false;
  };
  private onWheel = (e: WheelEvent): void => {
    e.preventDefault();
    const r = this.canvas.getBoundingClientRect();
    const px = (e.clientX - r.left) * devicePixelRatio, py = (e.clientY - r.top) * devicePixelRatio;
    const f = Math.exp(-e.deltaY * 0.0015);
    const ns = Math.min(64, Math.max(0.02, this.scale * f));
    const k = ns / this.scale;
    this.tx = px - (px - this.tx) * k;
    this.ty = py - (py - this.ty) * k;
    this.scale = ns;
  };

  /** Re-rasterize dirty pixels into the backing canvas. */
  private flush(): void {
    let b: Bbox | null = null;
    if (this.dirtyAll) b = { x0: 0, y0: 0, x1: this.host.size, y1: this.host.size };
    else if (this.dirty) b = this.dirty;
    if (!b) return;
    this.dirtyAll = false; this.dirty = null;
    const w = b.x1 - b.x0, h = b.y1 - b.y0;
    if (w <= 0 || h <= 0) return;
    const img = this.bctx.createImageData(w, h);
    const d = img.data, size = this.host.size;
    if (this.mode === "tiles") {
      const t = this.host.tiles;
      for (let y = 0; y < h; y++) {
        let src = (b.y0 + y) * size + b.x0, dst = y * w * 4;
        for (let x = 0; x < w; x++, src++, dst += 4) {
          const c = TILE_COLORS[t[src]] ?? TILE_COLORS[0];
          d[dst] = c[0]; d[dst + 1] = c[1]; d[dst + 2] = c[2]; d[dst + 3] = 255;
        }
      }
    } else {
      const hm = this.host.height, t = this.host.tiles, inv = 255 / (H_MAX - H_MIN);
      const g = 1 - TILE_GHOST;
      for (let y = 0; y < h; y++) {
        let src = (b.y0 + y) * size + b.x0, dst = y * w * 4;
        for (let x = 0; x < w; x++, src++, dst += 4) {
          const v = Math.max(0, Math.min(255, (hm[src] - H_MIN) * inv)) * g;
          const c = TILE_COLORS[t[src]] ?? TILE_COLORS[0];
          d[dst] = (v + c[0] * TILE_GHOST) | 0;
          d[dst + 1] = (v + c[1] * TILE_GHOST) | 0;
          d[dst + 2] = (v + c[2] * TILE_GHOST) | 0;
          d[dst + 3] = 255;
        }
      }
    }
    this.bctx.putImageData(img, b.x0, b.y0);
  }

  private frame = (): void => {
    requestAnimationFrame(this.frame);
    this.flush();
    const ctx = this.ctx;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = "#0d1620";
    ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
    ctx.imageSmoothingEnabled = this.scale < 1;
    ctx.setTransform(this.scale, 0, 0, this.scale, this.tx, this.ty);
    ctx.drawImage(this.back, 0, 0);
    ctx.lineWidth = 1 / this.scale;
    ctx.strokeStyle = "rgba(255,255,255,0.25)";
    ctx.strokeRect(0, 0, this.host.size, this.host.size);
    if (this.cursor) {
      ctx.beginPath();
      ctx.arc(this.cursor.x, this.cursor.y, this.brushRadius, 0, Math.PI * 2);
      ctx.lineWidth = 1.5 / this.scale;
      ctx.strokeStyle = "rgba(255,255,255,0.9)";
      ctx.stroke();
      ctx.strokeStyle = "rgba(0,0,0,0.6)";
      ctx.lineWidth = 0.75 / this.scale;
      ctx.stroke();
    }
  };
}
