// Shared input aggregation so camera controllers don't each own listeners.
// Deltas accumulate between frames; the active controller reads them in
// update() and then calls consumeFrameDeltas().

export interface InputState {
  keys: Set<string>;
  dragging: boolean;
  dragDeltaPx: { x: number; y: number };
  wheelDelta: number;
  cursorPx: { x: number; y: number };
  consumeFrameDeltas(): void;
}

export function createInput(canvas: HTMLCanvasElement): InputState {
  const state: InputState = {
    keys: new Set(),
    dragging: false,
    dragDeltaPx: { x: 0, y: 0 },
    wheelDelta: 0,
    cursorPx: { x: 0, y: 0 },
    consumeFrameDeltas() {
      state.dragDeltaPx.x = 0;
      state.dragDeltaPx.y = 0;
      state.wheelDelta = 0;
    },
  };

  window.addEventListener('keydown', (e) => {
    if (e.metaKey || e.ctrlKey) return;
    state.keys.add(e.code);
  });
  window.addEventListener('keyup', (e) => state.keys.delete(e.code));
  window.addEventListener('blur', () => state.keys.clear());

  let lastDrag: { x: number; y: number } | null = null;
  canvas.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 && e.button !== 1) return;
    canvas.setPointerCapture(e.pointerId);
    state.dragging = true;
    lastDrag = { x: e.clientX, y: e.clientY };
  });
  canvas.addEventListener('pointermove', (e) => {
    state.cursorPx.x = e.clientX;
    state.cursorPx.y = e.clientY;
    if (state.dragging && lastDrag) {
      state.dragDeltaPx.x += e.clientX - lastDrag.x;
      state.dragDeltaPx.y += e.clientY - lastDrag.y;
      lastDrag = { x: e.clientX, y: e.clientY };
    }
  });
  const endDrag = (e: PointerEvent) => {
    if (canvas.hasPointerCapture(e.pointerId)) canvas.releasePointerCapture(e.pointerId);
    state.dragging = false;
    lastDrag = null;
  };
  canvas.addEventListener('pointerup', endDrag);
  canvas.addEventListener('pointercancel', endDrag);

  canvas.addEventListener('wheel', (e) => {
    e.preventDefault();
    state.wheelDelta += e.deltaY;
    state.cursorPx.x = e.clientX;
    state.cursorPx.y = e.clientY;
  }, { passive: false });

  return state;
}
