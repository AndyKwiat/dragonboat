import { CONFIG } from '../config';
import type { InputState } from '../input';
import type { CameraController, CameraState, ViewportInfo } from './CameraController';
import { screenDeltaToGround, type CameraRig } from './CameraController';

// Free camera: WASD/arrow pan, drag pan, wheel zoom anchored at the cursor.
export class FreeCameraController implements CameraController {
  readonly name = 'free';
  private state: CameraState = { centerX: 0, centerY: 0, zoom: 1 };
  private minZoom = 0;
  private ground = { x: 0, y: 0 };

  constructor(private rig: CameraRig) {}

  setRig(rig: CameraRig) { this.rig = rig; }

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
    // Screen-space pan and drag, turned into ground movement by the rig
    // (yaw rotates it, pitch foreshortens the vertical axis).
    screenDeltaToGround(this.rig, dx * panPx - input.dragDeltaPx.x, dy * panPx - input.dragDeltaPx.y, s.zoom, this.ground);
    s.centerX += this.ground.x;
    s.centerY += this.ground.y;

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
        screenDeltaToGround(this.rig, px, py, 1, this.ground);
        s.centerX += this.ground.x * (1 / oldZoom - 1 / newZoom);
        s.centerY += this.ground.y * (1 / oldZoom - 1 / newZoom);
        s.zoom = newZoom;
      }
    }

    input.consumeFrameDeltas();
  }
}
