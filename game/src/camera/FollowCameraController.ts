import { CONFIG } from '../config';
import type { InputState } from '../input';
import type { CameraController, CameraState, ViewportInfo } from './CameraController';

export interface FollowTarget {
  x: number;
  y: number;
}

// Follow camera: locked to a target's position (target stays screen-centered).
// Wheel still zooms, but there's no cursor anchoring — the center is not ours
// to move. main.ts may still clamp the center at the world edges.
export class FollowCameraController implements CameraController {
  readonly name = 'follow';
  private state: CameraState = { centerX: 0, centerY: 0, zoom: CONFIG.boat.followZoom };
  private minZoom = 0;

  constructor(private target: FollowTarget) {}

  setMinZoom(z: number) { this.minZoom = z; }

  enable(state: CameraState): void {
    // Keep whatever zoom we had; the position snaps to the target.
    this.state = { centerX: this.target.x, centerY: this.target.y, zoom: this.state.zoom || state.zoom };
  }

  disable(): void {}

  getState(): CameraState {
    return this.state;
  }

  update(_dt: number, input: InputState, _view: ViewportInfo): void {
    const s = this.state;

    if (input.wheelDelta !== 0) {
      s.zoom = Math.min(
        Math.max(s.zoom * Math.exp(-input.wheelDelta * CONFIG.zoomWheelSensitivity), this.minZoom),
        CONFIG.maxZoom,
      );
    }

    s.centerX = this.target.x;
    s.centerY = this.target.y;

    input.consumeFrameDeltas();
  }
}
