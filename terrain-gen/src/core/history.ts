/** Chunked undo/redo for brush strokes. A stroke captures each 64x64 chunk's
 *  "before" the first time it touches it; "after" is captured on commit.
 *  Undo/redo swap chunk contents back in. Memory-capped, oldest dropped. */

export const CHUNK = 64;

export type PaintLayer = "tiles" | "height";
type Buf = Uint8Array | Float32Array;

interface ChunkPair { idx: number; before: Buf; after: Buf }
export interface StrokeEntry { layer: PaintLayer; chunks: ChunkPair[]; bytes: number }

function copyChunk(arr: Buf, size: number, chunkIdx: number): Buf {
  const perRow = size / CHUNK;
  const cx = (chunkIdx % perRow) * CHUNK, cy = Math.floor(chunkIdx / perRow) * CHUNK;
  const out = arr instanceof Uint8Array ? new Uint8Array(CHUNK * CHUNK) : new Float32Array(CHUNK * CHUNK);
  for (let y = 0; y < CHUNK; y++) {
    const row = (cy + y) * size + cx;
    out.set(arr.subarray(row, row + CHUNK), y * CHUNK);
  }
  return out;
}

function pasteChunk(arr: Buf, size: number, chunkIdx: number, data: Buf): void {
  const perRow = size / CHUNK;
  const cx = (chunkIdx % perRow) * CHUNK, cy = Math.floor(chunkIdx / perRow) * CHUNK;
  for (let y = 0; y < CHUNK; y++) {
    (arr as Float32Array).set(data.subarray(y * CHUNK, (y + 1) * CHUNK) as Float32Array, (cy + y) * size + cx);
  }
}

/** Records one in-progress stroke. */
export class StrokeRecorder {
  private touched = new Map<number, Buf>();
  constructor(private layer: PaintLayer, private arr: Buf, private size: number) {}

  /** Call BEFORE mutating pixels in the given bbox. */
  touch(x0: number, y0: number, x1: number, y1: number): void {
    const perRow = this.size / CHUNK;
    const c0x = Math.max(0, Math.floor(x0 / CHUNK)), c1x = Math.min(perRow - 1, Math.floor((x1 - 1) / CHUNK));
    const c0y = Math.max(0, Math.floor(y0 / CHUNK)), c1y = Math.min(perRow - 1, Math.floor((y1 - 1) / CHUNK));
    for (let cy = c0y; cy <= c1y; cy++) for (let cx = c0x; cx <= c1x; cx++) {
      const idx = cy * perRow + cx;
      if (!this.touched.has(idx)) this.touched.set(idx, copyChunk(this.arr, this.size, idx));
    }
  }

  /** Capture "after" states; returns null if the stroke changed nothing. */
  commit(): StrokeEntry | null {
    const chunks: ChunkPair[] = [];
    let bytes = 0;
    for (const [idx, before] of this.touched) {
      const after = copyChunk(this.arr, this.size, idx);
      let same = true;
      for (let i = 0; i < before.length; i++) if (before[i] !== after[i]) { same = false; break; }
      if (same) continue;
      chunks.push({ idx, before, after });
      bytes += before.byteLength * 2;
    }
    return chunks.length ? { layer: this.layer, chunks, bytes } : null;
  }
}

export class History {
  private undoStack: StrokeEntry[] = [];
  private redoStack: StrokeEntry[] = [];
  private bytes = 0;
  constructor(private maxBytes = 256 * 1024 * 1024) {}

  push(entry: StrokeEntry): void {
    this.undoStack.push(entry);
    this.bytes += entry.bytes;
    for (const e of this.redoStack) this.bytes -= e.bytes;
    this.redoStack = [];
    while (this.bytes > this.maxBytes && this.undoStack.length > 1) {
      this.bytes -= this.undoStack.shift()!.bytes;
    }
  }

  get canUndo(): boolean { return this.undoStack.length > 0; }
  get canRedo(): boolean { return this.redoStack.length > 0; }

  undo(arrs: Record<PaintLayer, Buf>, size: number): StrokeEntry | null {
    const e = this.undoStack.pop();
    if (!e) return null;
    for (const c of e.chunks) pasteChunk(arrs[e.layer], size, c.idx, c.before);
    this.redoStack.push(e);
    return e;
  }

  redo(arrs: Record<PaintLayer, Buf>, size: number): StrokeEntry | null {
    const e = this.redoStack.pop();
    if (!e) return null;
    for (const c of e.chunks) pasteChunk(arrs[e.layer], size, c.idx, c.after);
    this.undoStack.push(e);
    return e;
  }

  clear(): void { this.undoStack = []; this.redoStack = []; this.bytes = 0; }
}
