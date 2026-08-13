import type { City } from '../cities';
import { CONFIG } from '../config';
import type { InputState } from '../input';
import type { CameraController, CameraState, ViewportInfo } from './CameraController';
import type { FollowTarget } from './FollowCameraController';

// Follows the target like the follow cam, but picks its own zoom: close to a
// city it zooms in, out in open water it pulls back, easing between the two.
// The wheel is ignored — the zoom is the whole point of the mode.
//
// It also exposes speedScale(), which main.ts feeds to the boat so travelling
// zoomed-in is slower (the world goes by at a saner screen speed).
export class DynamicZoomCameraController implements CameraController {
  readonly name = 'dynamic';
  private state: CameraState = { centerX: 0, centerY: 0, zoom: CONFIG.dynamicZoom.farZoom };
  private minZoom = 0;

  constructor(private target: FollowTarget, private cities: City[]) {}

  setMinZoom(z: number) { this.minZoom = z; }

  enable(_state: CameraState): void {
    // Snap straight to the zoom this position calls for rather than easing in
    // from whatever the previous mode was looking at.
    this.state = {
      centerX: this.target.x,
      centerY: this.target.y,
      zoom: this.targetZoom(),
    };
  }

  disable(): void {}

  getState(): CameraState {
    return this.state;
  }

  /** Distance from the target to the nearest city centre, in world units. */
  private nearestCityDistance(): number {
    let best = Infinity;
    for (const city of this.cities) {
      const dx = city.location.x - this.target.x;
      const dy = city.location.y - this.target.y;
      const d2 = dx * dx + dy * dy;
      if (d2 < best) best = d2;
    }
    return Math.sqrt(best);
  }

  /** Zoom this position wants: nearZoom inside nearDistance, farZoom past farDistance. */
  private targetZoom(): number {
    const c = CONFIG.dynamicZoom;
    const d = this.nearestCityDistance();
    const t = clamp01((d - c.nearDistance) / (c.farDistance - c.nearDistance));
    return c.nearZoom + (c.farZoom - c.nearZoom) * t;
  }

  /**
   * Boat max-speed multiplier for the current zoom: 1 at farZoom, slowFactor
   * at nearZoom. Read by main.ts only while this mode is active.
   */
  speedScale(): number {
    const c = CONFIG.dynamicZoom;
    const t = clamp01((this.state.zoom - c.farZoom) / (c.nearZoom - c.farZoom));
    return 1 + (c.slowFactor - 1) * t;
  }

  update(dt: number, input: InputState, _view: ViewportInfo): void {
    const s = this.state;

    // Frame-rate independent exponential ease; smoothing is the time constant
    // in seconds, so bigger = laggier.
    const k = 1 - Math.exp(-dt / Math.max(CONFIG.dynamicZoom.smoothing, 1e-4));
    const goal = Math.min(Math.max(this.targetZoom(), this.minZoom), CONFIG.maxZoom);
    s.zoom += (goal - s.zoom) * k;

    s.centerX = this.target.x;
    s.centerY = this.target.y;

    input.consumeFrameDeltas();
  }
}

function clamp01(v: number): number {
  return Math.min(Math.max(v, 0), 1);
}
