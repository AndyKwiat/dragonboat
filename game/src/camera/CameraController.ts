import * as THREE from 'three';
import { CONFIG } from '../config';
import type { InputState } from '../input';

// World coordinates: (0,0) top-left, y down. zoom = screen px per world unit
// at the camera's focus point on the ground.
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

const PITCH = (CONFIG.camera3d.pitchDeg * Math.PI) / 180;
const FOV = (CONFIG.camera3d.fovDeg * Math.PI) / 180;

// The camera looks down at the ground at PITCH, so ground distances along the
// screen's vertical axis are foreshortened by this factor at the focus point.
export const GROUND_Y_SCALE = Math.sin(PITCH);

// Zoom at which the whole world fits on screen (approximately: the frustum is
// a trapezoid on the ground, this uses the focus-plane scale).
export function minZoomFor(view: ViewportInfo, worldW: number, worldH: number): number {
  return Math.min(view.widthPx / worldW, view.heightPx / worldH);
}

// Approximate view rectangle in world coords for a state (exact at the focus
// row; the perspective view is really a trapezoid, wider at the far edge).
export function viewRect(state: CameraState, view: ViewportInfo) {
  const halfW = view.widthPx / (2 * state.zoom);
  const halfH = view.heightPx / (2 * state.zoom * GROUND_Y_SCALE);
  return {
    left: state.centerX - halfW,
    top: state.centerY - halfH,
    right: state.centerX + halfW,
    bottom: state.centerY + halfH,
  };
}

// Keep the focus point inside the world; if the view is larger than the world
// on an axis, lock that axis centered (letterboxing).
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

// Distance from the camera to its focus point for a zoom: the focus plane
// must show heightPx / zoom world units top to bottom.
export function focusDistanceFor(state: CameraState, view: ViewportInfo): number {
  return view.heightPx / (2 * state.zoom) / Math.tan(FOV / 2);
}

// Push a CameraState into the perspective camera. Scene coords are world
// coords with y negated (see main.ts: the world group is scaled by -1 in y so
// the y-down world sits in three's y-up scene with z as height). The camera
// sits "south" of the focus (larger world y) looking north and down at PITCH,
// so screen-up is world -y, exactly like the 2D version.
export function applyToCamera(state: CameraState, view: ViewportInfo, camera: THREE.PerspectiveCamera): void {
  const d = focusDistanceFor(state, view);
  const fx = state.centerX;
  const fy = -state.centerY;
  camera.position.set(fx, fy - d * Math.cos(PITCH), d * Math.sin(PITCH));
  camera.up.set(0, 0, 1);
  camera.lookAt(fx, fy, 0);

  const aspect = view.widthPx / view.heightPx;
  const near = Math.max(0.1, d * 0.02);
  const far = d * 4 + 100;
  if (camera.fov !== CONFIG.camera3d.fovDeg || camera.aspect !== aspect || camera.near !== near || camera.far !== far) {
    camera.fov = CONFIG.camera3d.fovDeg;
    camera.aspect = aspect;
    camera.near = near;
    camera.far = far;
    camera.updateProjectionMatrix();
  }
  camera.updateMatrixWorld();
}

const tmpVec = new THREE.Vector3();

// World point (x, y, height z) -> screen px. Returns null if behind the camera.
export function worldToScreen(
  camera: THREE.PerspectiveCamera, view: ViewportInfo,
  x: number, y: number, z: number, out: { x: number; y: number },
): boolean {
  tmpVec.set(x, -y, z).project(camera);
  if (tmpVec.z > 1) return false;
  out.x = (tmpVec.x + 1) / 2 * view.widthPx;
  out.y = (1 - tmpVec.y) / 2 * view.heightPx;
  return true;
}

// Screen px -> world point on the ground plane (z = 0). Rays that miss the
// ground (or hit past maxDistance) are clamped to maxDistance along the ray.
export function screenToGround(
  camera: THREE.PerspectiveCamera, view: ViewportInfo,
  px: number, py: number, maxDistance: number, out: { x: number; y: number },
): void {
  const ndcX = (px / view.widthPx) * 2 - 1;
  const ndcY = 1 - (py / view.heightPx) * 2;
  tmpVec.set(ndcX, ndcY, 0.5).unproject(camera).sub(camera.position).normalize();
  const origin = camera.position;
  let t = maxDistance;
  if (tmpVec.z < -1e-6) t = Math.min(t, -origin.z / tmpVec.z);
  out.x = origin.x + tmpVec.x * t;
  out.y = -(origin.y + tmpVec.y * t);
}
