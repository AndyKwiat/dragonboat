import { CONFIG } from '../config';
import type { InputState } from '../input';
import type { CameraController, CameraState, ViewportInfo } from './CameraController';

// Free camera: WASD/arrow pan, drag pan, wheel zoom anchored at the cursor.
export class FreeCameraController implements CameraController {
  readonly name = 'free';
  private state: CameraState = { centerX: 0, centerY: 0, zoom: 1 };
  private minZoom = 0;

  setMinZoom(z: number) { this.minZoom = z; }

  enable(state: CameraState): void {
    this.state = { ...state };
  }

  disable(): void {}

  getState(): CameraState {
    return this.state;
  }

  update(dt: number, input: InputState, view: ViewportInfo): void {
    const s = this.state;

    // Keyboard pan, constant screen speed regardless of zoom.
    const panPx = CONFIG.keyPanPxPerSec * dt;
    let dx = 0, dy = 0;
    if (input.keys.has('KeyA') || input.keys.has('ArrowLeft')) dx -= 1;
    if (input.keys.has('KeyD') || input.keys.has('ArrowRight')) dx += 1;
    if (input.keys.has('KeyW') || input.keys.has('ArrowUp')) dy -= 1;
    if (input.keys.has('KeyS') || input.keys.has('ArrowDown')) dy += 1;
    s.centerX += (dx * panPx) / s.zoom;
    s.centerY += (dy * panPx) / s.zoom;

    // Drag pan: world follows the cursor (screen and world are both y-down).
    s.centerX -= input.dragDeltaPx.x / s.zoom;
    s.centerY -= input.dragDeltaPx.y / s.zoom;

    // Wheel zoom toward the cursor: the world point under the cursor stays put.
    if (input.wheelDelta !== 0) {
      const oldZoom = s.zoom;
      const newZoom = Math.min(
        Math.max(oldZoom * Math.exp(-input.wheelDelta * CONFIG.zoomWheelSensitivity), this.minZoom),
        CONFIG.maxZoom,
      );
      if (newZoom !== oldZoom) {
        const px = input.cursorPx.x - view.widthPx / 2;
        const py = input.cursorPx.y - view.heightPx / 2;
        s.centerX += px / oldZoom - px / newZoom;
        s.centerY += py / oldZoom - py / newZoom;
        s.zoom = newZoom;
      }
    }

    input.consumeFrameDeltas();
  }
}
