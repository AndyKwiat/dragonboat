/** Plain-DOM toolbar for the 2D editor: mode switch, tile swatches, height
 *  tools, brush size / strength sliders, undo / redo / clear. */
import { TILE_COLORS } from "../core/types";

export type HeightTool = "raise" | "lower" | "smooth";

export interface ToolState {
  mode: "tiles" | "height" | "markers";
  tile: number;          // selected tile id
  tileBrush: number;     // radius, map px
  heightBrush: number;   // radius, map px
  heightTool: HeightTool;
  markerRadius: number;  // radius, map px, for newly placed markers
  strength: number;      // 1..100
  size: number;          // map resolution (power of two)
  voxel: boolean;        // 3D voxel look
}

export interface ToolbarCallbacks {
  onModeChange(): void;
  onUndo(): void;
  onRedo(): void;
  onClear(): void;
  onBrushChange(): void;
  onResize(size: number): void;
  onVoxelChange(): void;
  onSave(): void;
  onSaveAs(): void;
  onLoad(file: File): void;
  onOpen(): void;
}

const TILE_NAMES = ["water", "sand", "grass", "forest", "rock"];

export function buildToolbar(root: HTMLElement, st: ToolState, cb: ToolbarCallbacks) {
  root.innerHTML = `
    <div class="tb-group" id="tb-mode">
      <button data-mode="tiles" class="tb-btn">Tiles</button>
      <button data-mode="height" class="tb-btn">Height</button>
      <button data-mode="markers" class="tb-btn">Markers</button>
    </div>
    <div class="tb-group" id="tb-tiles">
      ${TILE_COLORS.map((c, i) => `<button class="swatch" data-tile="${i}" title="${TILE_NAMES[i]}" style="background:rgb(${c.join(",")})"></button>`).join("")}
      <label>size <input type="range" id="tile-size" min="1" max="800" step="1"><span id="tile-size-v"></span></label>
    </div>
    <div class="tb-group" id="tb-height">
      <button data-htool="raise" class="tb-btn">Raise</button>
      <button data-htool="lower" class="tb-btn">Lower</button>
      <button data-htool="smooth" class="tb-btn">Smooth</button>
      <label>size <input type="range" id="h-size" min="1" max="800" step="1"><span id="h-size-v"></span></label>
      <label>strength <input type="range" id="h-str" min="1" max="100" step="1"><span id="h-str-v"></span></label>
    </div>
    <div class="tb-group" id="tb-markers">
      <label>radius <input type="range" id="m-size" min="1" max="800" step="1"><span id="m-size-v"></span></label>
      <span class="tb-hint">click = place, click marker = edit (empty tag deletes), drag = move</span>
    </div>
    <div class="tb-group">
      <button id="tb-undo" class="tb-btn" title="Cmd/Ctrl+Z">Undo</button>
      <button id="tb-redo" class="tb-btn" title="Shift+Cmd/Ctrl+Z">Redo</button>
      <button id="tb-clear" class="tb-btn" title="Fill the whole map with the current tile / flatten heights / remove all markers">Clear</button>
    </div>
    <div class="tb-group">
      <label>resolution <select id="tb-res">${[512, 1024, 2048, 4096, 8192].map((n) => `<option value="${n}">${n}</option>`).join("")}</select></label>
    </div>
    <div class="tb-group">
      <label><input type="checkbox" id="tb-voxel"> voxel 3D</label>
    </div>
    <div class="tb-group">
      <button id="tb-save" class="tb-btn" title="Save over the current file (Cmd/Ctrl+S)">Save</button>
      <button id="tb-save-as" class="tb-btn" title="Write the map to a new binary .terrain file (gzipped)">Save As…</button>
      <button id="tb-load" class="tb-btn" title="Open a .terrain file">Load</button>
      <input type="file" id="tb-file" accept=".terrain,.terrain.gz,application/octet-stream" style="display:none">
    </div>
    <div class="tb-group tb-status"><span id="tb-3d">3D: up to date</span></div>
  `;
  const q = <T extends HTMLElement>(sel: string) => root.querySelector(sel) as T;
  const tileSize = q<HTMLInputElement>("#tile-size"), hSize = q<HTMLInputElement>("#h-size"), hStr = q<HTMLInputElement>("#h-str"), mSize = q<HTMLInputElement>("#m-size");

  const res = q<HTMLSelectElement>("#tb-res");
  const voxel = q<HTMLInputElement>("#tb-voxel");
  const sync = () => {
    res.value = String(st.size);
    voxel.checked = st.voxel;
    root.querySelectorAll<HTMLButtonElement>("[data-mode]").forEach((b) => b.classList.toggle("active", b.dataset.mode === st.mode));
    q("#tb-tiles").style.display = st.mode === "tiles" ? "" : "none";
    q("#tb-height").style.display = st.mode === "height" ? "" : "none";
    q("#tb-markers").style.display = st.mode === "markers" ? "" : "none";
    mSize.value = String(st.markerRadius); q("#m-size-v").textContent = String(st.markerRadius);
    root.querySelectorAll<HTMLButtonElement>("[data-tile]").forEach((b) => b.classList.toggle("active", Number(b.dataset.tile) === st.tile));
    root.querySelectorAll<HTMLButtonElement>("[data-htool]").forEach((b) => b.classList.toggle("active", b.dataset.htool === st.heightTool));
    tileSize.value = String(st.tileBrush); q("#tile-size-v").textContent = String(st.tileBrush);
    hSize.value = String(st.heightBrush); q("#h-size-v").textContent = String(st.heightBrush);
    hStr.value = String(st.strength); q("#h-str-v").textContent = String(st.strength);
  };

  root.querySelectorAll<HTMLButtonElement>("[data-mode]").forEach((b) => b.onclick = () => {
    st.mode = b.dataset.mode as ToolState["mode"]; sync(); cb.onModeChange();
  });
  root.querySelectorAll<HTMLButtonElement>("[data-tile]").forEach((b) => b.onclick = () => { st.tile = Number(b.dataset.tile); sync(); });
  root.querySelectorAll<HTMLButtonElement>("[data-htool]").forEach((b) => b.onclick = () => { st.heightTool = b.dataset.htool as HeightTool; sync(); });
  tileSize.oninput = () => { st.tileBrush = Number(tileSize.value); sync(); cb.onBrushChange(); };
  hSize.oninput = () => { st.heightBrush = Number(hSize.value); sync(); cb.onBrushChange(); };
  mSize.oninput = () => { st.markerRadius = Number(mSize.value); sync(); cb.onBrushChange(); };
  hStr.oninput = () => { st.strength = Number(hStr.value); sync(); };
  res.onchange = () => cb.onResize(Number(res.value));
  voxel.onchange = () => { st.voxel = voxel.checked; cb.onVoxelChange(); };
  const file = q<HTMLInputElement>("#tb-file");
  q("#tb-save").onclick = cb.onSave;
  q("#tb-save-as").onclick = cb.onSaveAs;
  q("#tb-load").onclick = () => cb.onOpen();
  file.onchange = () => { const f = file.files?.[0]; if (f) cb.onLoad(f); };
  q("#tb-undo").onclick = cb.onUndo;
  q("#tb-redo").onclick = cb.onRedo;
  q("#tb-clear").onclick = cb.onClear;
  sync();

  return {
    sync,
    setHistory(canUndo: boolean, canRedo: boolean) {
      (q("#tb-undo") as HTMLButtonElement).disabled = !canUndo;
      (q("#tb-redo") as HTMLButtonElement).disabled = !canRedo;
    },
    set3dStatus(text: string) { q("#tb-3d").textContent = text; },
    /** Fall back to the hidden <input type=file> when the picker API is absent. */
    pickFile() { file.value = ""; file.click(); },
  };
}
