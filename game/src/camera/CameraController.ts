import * as THREE from 'three';
import type { InputState } from '../input';

// World coordinates: (0,0) top-left, y down. zoom = screen px per world unit.
export interface CameraState {
  centerX: number;
  centerY: number;
  zoom: number;
}

export interface ViewportInfo {
  widthPx: number;
  heightPx: number;
}

// A camera mode. main.ts owns a set of these and switches between them,
// handing the current CameraState across via enable() so transitions are
// seamless. Controllers mutate their state in update(); main clamps and
// applies it afterwards.
export interface CameraController {
  readonly name: string;
  enable(state: CameraState): void;
  disable(): void;
  update(dt: number, input: InputState, view: ViewportInfo): void;
  getState(): CameraState;
}

// Zoom at which the whole world fits on screen.
export function minZoomFor(view: ViewportInfo, worldW: number, worldH: number): number {
  return Math.min(view.widthPx / worldW, view.heightPx / worldH);
}

// View rectangle in world coords for a state.
export function viewRect(state: CameraState, view: ViewportInfo) {
  const halfW = view.widthPx / (2 * state.zoom);
  const halfH = view.heightPx / (2 * state.zoom);
  return {
    left: state.centerX - halfW,
    top: state.centerY - halfH,
    right: state.centerX + halfW,
    bottom: state.centerY + halfH,
  };
}

// Keep the view inside the world; if the view is larger than the world on an
// axis, lock that axis centered (letterboxing).
export function clampCamera(state: CameraState, view: ViewportInfo, worldW: number, worldH: number): void {
  const halfW = view.widthPx / (2 * state.zoom);
  const halfH = view.heightPx / (2 * state.zoom);
  state.centerX = 2 * halfW >= worldW
    ? worldW / 2
    : Math.min(Math.max(state.centerX, halfW), worldW - halfW);
  state.centerY = 2 * halfH >= worldH
    ? worldH / 2
    : Math.min(Math.max(state.centerY, halfH), worldH - halfH);
}

// Push a CameraState into the orthographic camera. top < bottom in world
// y-down coords, so the frustum is vertically flipped on purpose.
export function applyToCamera(state: CameraState, view: ViewportInfo, camera: THREE.OrthographicCamera): void {
  const halfW = view.widthPx / (2 * state.zoom);
  const halfH = view.heightPx / (2 * state.zoom);
  camera.left = state.centerX - halfW;
  camera.right = state.centerX + halfW;
  camera.top = state.centerY - halfH;
  camera.bottom = state.centerY + halfH;
  camera.updateProjectionMatrix();
}
