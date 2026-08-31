/** App entry: 2D paint editor (left = 3D view-only, right = 2D map).
 *  Painting mutates the shared height/tile arrays in place; the 3D view
 *  re-uploads them ~1s after the last edit (debounced) so painting never
 *  waits on the 3D side. */
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { Tile } from "./core/types";
import { HEIGHT_MIN, circleBbox, fillHeight, fillTiles, paintHeight, paintTiles, smoothHeight } from "./core/paint";
import type { Bbox } from "./core/paint";
import { History, StrokeRecorder, markerEntry } from "./core/history";
import type { HistoryEntry, PaintLayer } from "./core/history";
import { resampleHeight, resampleTiles } from "./core/resample";
import { DEFAULT_MARKER_RADIUS, MAX_TAG_LENGTH, cloneMarkers, hitMarker, makeMarker, normalizeTag, resampleMarkers } from "./core/markers";
import type { Marker } from "./core/markers";
import { decodeTerrain, encodeTerrain, gunzip, gzip, isGzip } from "./core/fileFormat";
import { TerrainRenderer } from "./render/terrainRenderer";
import { MapView2D } from "./ui/mapView2d";
import { buildToolbar } from "./ui/toolbar";
import type { ToolState } from "./ui/toolbar";

const MAP_SIZE = 8192;
const BASE_HEIGHT = HEIGHT_MIN; // flat start / "clear" level
const WORLD_SIZE = 1024;
const DEBOUNCE_3D_MS = 1000;


class App {
  size = MAP_SIZE;
  height!: Float32Array;
  tiles!: Uint8Array;
  markers: Marker[] = [];
  tool: ToolState = { mode: "tiles", tile: 2, tileBrush: 80, heightBrush: 160, heightTool: "raise", markerRadius: DEFAULT_MARKER_RADIUS, strength: 30, size: MAP_SIZE, voxel: false };
  history = new History();
  map!: MapView2D;
  toolbar!: ReturnType<typeof buildToolbar>;
  // 3D
  scene = new THREE.Scene();
  camera = new THREE.PerspectiveCamera(55, 1, 1, 20000);
  gl = new THREE.WebGLRenderer({ antialias: true });
  controls!: OrbitControls;
  terrain = new TerrainRenderer(WORLD_SIZE);
  private dirty3dAt = 0;
  private dirty3d = false;
  // stroke state
  private rec: StrokeRecorder | null = null;
  private last: { x: number; y: number } | null = null;
  // marker drag state
  private mdrag: { idx: number; before: Marker[]; start: { x: number; y: number }; moved: boolean } | null = null;

  constructor() {
    // start flat: BASE_HEIGHT everywhere, all water
    this.height = new Float32Array(this.size * this.size).fill(BASE_HEIGHT);
    this.tiles = new Uint8Array(this.size * this.size).fill(Tile.Water);

    // ---- 3D (view only) ----
    const view3d = document.getElementById("view3d")!;
    view3d.appendChild(this.gl.domElement);
    this.gl.setPixelRatio(Math.min(2, devicePixelRatio));
    this.scene.background = new THREE.Color(0x0d1620);
    this.scene.add(this.terrain.group);
    this.camera.position.set(0, 620, 720);
    this.controls = new OrbitControls(this.camera, this.gl.domElement);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI / 2 - 0.02;
    this.controls.minDistance = 30; this.controls.maxDistance = 4000;
    this.controls.screenSpacePanning = false; // pan slides along the ground
    // Orbit around whatever is in the middle of the viewport (at y = 0), not
    // the map center: re-anchor the target before every rotate/pan/zoom.
    const dom = this.gl.domElement;
    dom.addEventListener("pointerdown", () => this.anchorOrbitTarget());
    dom.addEventListener("wheel", () => this.anchorOrbitTarget(), { passive: true });
    new ResizeObserver(() => this.resize3d()).observe(view3d);
    this.resize3d();
    this.upload3d();

    // ---- 2D editor ----
    const app = this;
    this.map = new MapView2D(document.getElementById("map2d") as HTMLCanvasElement, {
      get size() { return app.size; }, get tiles() { return app.tiles; }, get height() { return app.height; }, get markers() { return app.markers; },
      onStroke: (k, x, y) => this.onStroke(k, x, y),
      onHover: (x, y) => { if (this.tool.mode === "markers" && !this.mdrag) this.map.hover = hitMarker(this.markers, x, y, this.map.minMarkerHit); },
    });
    this.toolbar = buildToolbar(document.getElementById("toolbar")!, this.tool, {
      onModeChange: () => { this.map.setMode(this.tool.mode); this.syncBrush(); },
      onUndo: () => this.undo(),
      onRedo: () => this.redo(),
      onClear: () => this.clear(),
      onBrushChange: () => this.syncBrush(),
      onResize: (n) => this.resize(n),
      onVoxelChange: () => this.terrain.setVoxel(this.tool.voxel),
      onSave: () => void this.save(),
      onSaveAs: () => void this.saveAs(),
      onOpen: () => void this.open(),
      onLoad: (f) => void this.load(f),
    });
    addEventListener("keydown", (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "s") { e.preventDefault(); void this.save(); }
    });
    this.syncBrush();
    this.toolbar.setHistory(false, false);
    addEventListener("keydown", (e: KeyboardEvent) => {
      const mod = e.metaKey || e.ctrlKey;
      if (mod && e.key.toLowerCase() === "z") { e.preventDefault(); e.shiftKey ? this.redo() : this.undo(); }
      else if (mod && e.key.toLowerCase() === "y") { e.preventDefault(); this.redo(); }
      else if (e.key === "[") this.nudgeBrush(-4);
      else if (e.key === "]") this.nudgeBrush(4);
    });
    this.loop();
  }

  /** Move controls.target to where the viewport-center ray hits y = 0,
   *  without moving the camera. */
  private anchorOrbitTarget(): void {
    const dir = new THREE.Vector3(0, 0, -1).applyQuaternion(this.camera.quaternion);
    if (dir.y >= -1e-4) return; // looking at/above the horizon: keep current target
    const t = -this.camera.position.y / dir.y;
    const hit = this.camera.position.clone().addScaledVector(dir, t);
    this.controls.target.copy(hit);
  }

  private resize3d(): void {
    const el = document.getElementById("view3d")!;
    const w = el.clientWidth, h = el.clientHeight;
    if (!w || !h) return;
    this.gl.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  private get brushRadius(): number {
    return this.tool.mode === "tiles" ? this.tool.tileBrush : this.tool.mode === "height" ? this.tool.heightBrush : this.tool.markerRadius;
  }
  private syncBrush(): void { this.map.brushRadius = this.brushRadius; }
  private nudgeBrush(d: number): void {
    if (this.tool.mode === "tiles") this.tool.tileBrush = Math.max(1, Math.min(800, this.tool.tileBrush + d));
    else if (this.tool.mode === "height") this.tool.heightBrush = Math.max(1, Math.min(800, this.tool.heightBrush + d));
    else this.tool.markerRadius = Math.max(1, Math.min(800, this.tool.markerRadius + d));
    this.toolbar.sync(); this.syncBrush();
  }
  private get layer(): PaintLayer { return this.tool.mode === "tiles" ? "tiles" : "height"; }
  private arr(layer: PaintLayer) { return layer === "tiles" ? this.tiles : this.height; }

  // ---------- painting ----------
  private onStroke(kind: "start" | "move" | "end", x: number, y: number): void {
    if (this.tool.mode === "markers") { this.onMarkerStroke(kind, x, y); return; }
    if (kind === "start") {
      this.rec = new StrokeRecorder(this.layer, this.arr(this.layer), this.size);
      this.last = null;
      this.stampPath(x, y);
    } else if (kind === "move") {
      if (this.rec) this.stampPath(x, y);
    } else {
      if (this.rec) { this.stampPath(x, y); this.endStroke(); }
    }
  }

  /** Stamp along the segment from the last point, spaced by ~r/4. */
  private stampPath(x: number, y: number): void {
    const r = this.brushRadius;
    if (!this.last) { this.stamp(x, y); this.last = { x, y }; return; }
    const dx = x - this.last.x, dy = y - this.last.y;
    const dist = Math.hypot(dx, dy);
    const spacing = Math.max(1, r * 0.25);
    if (dist < spacing) return;
    const n = Math.floor(dist / spacing);
    for (let i = 1; i <= n; i++) {
      const t = (i * spacing) / dist;
      this.stamp(this.last.x + dx * t, this.last.y + dy * t);
    }
    const t = (n * spacing) / dist;
    this.last = { x: this.last.x + dx * t, y: this.last.y + dy * t };
  }

  private stamp(x: number, y: number): void {
    const r = this.brushRadius;
    const b = circleBbox(x, y, r, this.size);
    if (!b) return;
    this.rec!.touch(b.x0, b.y0, b.x1, b.y1);
    let dirty: Bbox | null = null;
    if (this.tool.mode === "tiles") {
      dirty = paintTiles(this.tiles, this.size, x, y, r, this.tool.tile);
    } else {
      const s = this.tool.strength;
      if (this.tool.heightTool === "raise") dirty = paintHeight(this.height, this.size, x, y, r, s * 0.12);
      else if (this.tool.heightTool === "lower") dirty = paintHeight(this.height, this.size, x, y, r, -s * 0.12);
      else dirty = smoothHeight(this.height, this.size, x, y, r, s / 100);
    }
    this.map.markDirty(dirty);
    this.touch3d();
  }

  // ---------- markers ----------
  /** Click empty space = place (prompts for a tag); click a marker = edit its
   *  tag (empty = delete); drag a marker = move. All undoable. */
  private onMarkerStroke(kind: "start" | "move" | "end", x: number, y: number): void {
    if (kind === "start") {
      const idx = hitMarker(this.markers, x, y, this.map.minMarkerHit);
      this.mdrag = { idx, before: cloneMarkers(this.markers), start: { x, y }, moved: false };
      this.map.hover = idx;
      return;
    }
    if (!this.mdrag) return;
    const d = this.mdrag;
    if (d.idx >= 0) {
      // drag-to-move; a small wobble still counts as a click
      if (Math.hypot(x - d.start.x, y - d.start.y) * this.map.zoom > 3) d.moved = true;
      if (d.moved) { const m = this.markers[d.idx]; m.x = x; m.y = y; }
    }
    if (kind !== "end") return;
    this.mdrag = null;
    if (d.idx < 0) {
      if (x < 0 || y < 0 || x >= this.size || y >= this.size) return;
      const raw = prompt(`Marker tag (1–${MAX_TAG_LENGTH} chars):`, "");
      if (raw === null) return;
      const tag = normalizeTag(raw);
      if (tag === null) { alert(`Tag must be 1–${MAX_TAG_LENGTH} characters.`); return; }
      this.markers.push(makeMarker(d.start.x, d.start.y, this.tool.markerRadius, tag));
    } else if (!d.moved) {
      const m = this.markers[d.idx];
      const raw = prompt("Edit tag (empty to delete):", m.tag);
      if (raw === null) return;
      const tag = normalizeTag(raw);
      if (tag === null) this.markers.splice(d.idx, 1);
      else m.tag = tag;
    }
    this.commitMarkers(d.before);
    this.map.hover = hitMarker(this.markers, x, y, this.map.minMarkerHit);
  }

  private commitMarkers(before: Marker[]): void {
    const e = markerEntry(before, this.markers);
    if (e) this.history.push(e);
    this.toolbar.setHistory(this.history.canUndo, this.history.canRedo);
  }

  private endStroke(): void {
    const entry = this.rec?.commit() ?? null;
    this.rec = null; this.last = null;
    if (entry) this.history.push(entry);
    this.toolbar.setHistory(this.history.canUndo, this.history.canRedo);
  }

  private applyHistory(entry: HistoryEntry | null): void {
    if (!entry) return;
    if (entry.layer === "markers") {
      this.map.hover = -1;
      if (this.tool.mode !== "markers") { this.tool.mode = "markers"; this.toolbar.sync(); this.map.setMode("markers"); this.syncBrush(); }
      this.toolbar.setHistory(this.history.canUndo, this.history.canRedo);
      return;
    }
    // dirty bbox = union of chunk rects
    const perRow = this.size / 64;
    let b: Bbox | null = null;
    for (const c of entry.chunks) {
      const cx = (c.idx % perRow) * 64, cy = Math.floor(c.idx / perRow) * 64;
      const cb = { x0: cx, y0: cy, x1: cx + 64, y1: cy + 64 };
      b = b ? { x0: Math.min(b.x0, cb.x0), y0: Math.min(b.y0, cb.y0), x1: Math.max(b.x1, cb.x1), y1: Math.max(b.y1, cb.y1) } : cb;
    }
    // switch the 2D view to the layer being restored so the change is visible
    if (entry.layer !== this.tool.mode) { this.tool.mode = entry.layer; this.toolbar.sync(); this.map.setMode(entry.layer); this.syncBrush(); }
    this.map.markDirty(b);
    this.touch3d();
    this.toolbar.setHistory(this.history.canUndo, this.history.canRedo);
  }
  private get historyTarget() { return { tiles: this.tiles, height: this.height, markers: this.markers }; }
  undo(): void { this.applyHistory(this.history.undo(this.historyTarget, this.size)); }
  redo(): void { this.applyHistory(this.history.redo(this.historyTarget, this.size)); }

  /** Fill the whole map: current tile (tiles mode), flat 0 (height mode) or
   *  remove every marker (markers mode). Undoable. */
  clear(): void {
    if (this.tool.mode === "markers") {
      if (this.markers.length && !confirm(`Remove all ${this.markers.length} markers?`)) return;
      const before = cloneMarkers(this.markers);
      this.markers.length = 0;
      this.map.hover = -1;
      this.commitMarkers(before);
      return;
    }
    const rec = new StrokeRecorder(this.layer, this.arr(this.layer), this.size);
    rec.touch(0, 0, this.size, this.size);
    if (this.tool.mode === "tiles") fillTiles(this.tiles, this.tool.tile);
    else fillHeight(this.height, BASE_HEIGHT);
    const entry = rec.commit();
    if (entry) this.history.push(entry);
    this.map.markDirty(null);
    this.touch3d();
    this.toolbar.setHistory(this.history.canUndo, this.history.canRedo);
  }

  /** Change map resolution, resampling both layers (bilinear/average for
   *  heights, nearest/mode for tiles). History is cleared: chunk indices
   *  are resolution-specific. */
  resize(size: number): void {
    if (size === this.size || (size & (size - 1)) !== 0) return;
    this.height = resampleHeight(this.height, this.size, size);
    this.tiles = resampleTiles(this.tiles, this.size, size);
    this.markers = resampleMarkers(this.markers, this.size, size);
    this.size = size;
    this.tool.size = size;
    this.history.clear();
    this.toolbar.sync();
    this.toolbar.setHistory(false, false);
    this.map.resetSize();
    this.upload3d();
  }

  // ---------- save / load ----------
  /** Handle of the file this map was last opened from / saved to, when the
   *  browser supports the File System Access API. */
  private fileHandle: FileSystemFileHandle | null = null;
  private fileName = `map-${MAP_SIZE}.terrain`;

  /** Serialize the map, gzipped when the browser can. */
  private async encode(): Promise<Uint8Array> {
    const raw = encodeTerrain({ size: this.size, worldSize: WORLD_SIZE, height: this.height, tiles: this.tiles, markers: this.markers });
    return typeof CompressionStream !== "undefined" ? await gzip(raw) : raw;
  }

  /** Write over the file we opened / saved last; falls back to Save As. */
  async save(): Promise<void> {
    if (!this.fileHandle) return this.saveAs();
    this.toolbar.set3dStatus("saving…");
    try {
      const bytes = await this.encode();
      const w = await this.fileHandle.createWritable();
      await w.write(bytes as unknown as BufferSource);
      await w.close();
      this.toolbar.set3dStatus(`saved ${this.fileName} (${(bytes.length / 1048576).toFixed(1)} MB)`);
    } catch (err) {
      this.toolbar.set3dStatus(`save failed: ${(err as Error).message}`);
    }
  }

  /** Ask for a destination (defaulting to .terrain) and write there. */
  async saveAs(): Promise<void> {
    const bytes = await this.encode();
    const picker = (window as any).showSaveFilePicker as
      | ((o: unknown) => Promise<FileSystemFileHandle>) | undefined;
    if (picker) {
      let handle: FileSystemFileHandle;
      try {
        handle = await picker({
          suggestedName: this.fileName,
          types: [{ description: "Terrain map", accept: { "application/octet-stream": [".terrain"] } }],
        });
      } catch { return; } // user cancelled
      this.toolbar.set3dStatus("saving…");
      try {
        const w = await handle.createWritable();
        await w.write(bytes as unknown as BufferSource);
        await w.close();
        this.fileHandle = handle;
        this.fileName = handle.name;
        this.toolbar.set3dStatus(`saved ${this.fileName} (${(bytes.length / 1048576).toFixed(1)} MB)`);
      } catch (err) {
        this.toolbar.set3dStatus(`save failed: ${(err as Error).message}`);
      }
      return;
    }
    // No picker API: plain download, extension baked into the name.
    const blob = new Blob([bytes as BlobPart], { type: "application/octet-stream" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = this.fileName;
    document.body.appendChild(a); // some browsers ignore clicks on detached anchors
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
    this.toolbar.set3dStatus(`saved ${(bytes.length / 1048576).toFixed(1)} MB`);
  }

  /** Open a .terrain file, keeping its handle so Save can write back to it. */
  async open(): Promise<void> {
    const picker = (window as any).showOpenFilePicker as
      | ((o: unknown) => Promise<FileSystemFileHandle[]>) | undefined;
    if (!picker) { this.toolbar.pickFile(); return; }
    let handle: FileSystemFileHandle;
    try {
      [handle] = await picker({
        multiple: false,
        types: [{ description: "Terrain map", accept: { "application/octet-stream": [".terrain", ".terrain.gz"] } }],
      });
    } catch { return; } // user cancelled
    await this.load(await handle.getFile(), handle);
  }

  /** Replace the whole map with a .terrain file (raw or gzipped). Undoable
   *  history is dropped because the resolution may change. */
  async load(file: File, handle: FileSystemFileHandle | null = null): Promise<void> {
    try {
      let bytes: Uint8Array = new Uint8Array(await file.arrayBuffer());
      if (isGzip(bytes)) bytes = await gunzip(bytes);
      const t = decodeTerrain(bytes);
      this.height = t.height as Float32Array<ArrayBuffer>;
      this.tiles = t.tiles as Uint8Array<ArrayBuffer>;
      this.markers = t.markers;
      this.map.hover = -1;
      this.size = t.size;
      this.tool.size = t.size;
      this.history.clear();
      this.toolbar.sync();
      this.toolbar.setHistory(false, false);
      this.map.resetSize();
      this.upload3d();
      this.fileHandle = handle;
      this.fileName = file.name;
      this.toolbar.set3dStatus(`loaded ${file.name} (${t.size}², ${t.markers.length} markers)`);
    } catch (err) {
      alert(`Could not load ${file.name}: ${(err as Error).message}`);
    }
  }

  // ---------- 3D sync ----------
  private touch3d(): void {
    this.dirty3d = true; this.dirty3dAt = performance.now();
    this.toolbar.set3dStatus("3D: pending…");
  }
  private upload3d(): void {
    this.terrain.setOutput({ size: this.size, worldSize: WORLD_SIZE, height: this.height, tiles: this.tiles });
    this.dirty3d = false;
    this.toolbar?.set3dStatus("3D: up to date");
  }

  private loop = (): void => {
    requestAnimationFrame(this.loop);
    if (this.dirty3d && performance.now() - this.dirty3dAt > DEBOUNCE_3D_MS) this.upload3d();
    this.controls.update();
    this.terrain.update(this.camera);
    this.gl.render(this.scene, this.camera);
  };
}

(window as any).app = new App();
