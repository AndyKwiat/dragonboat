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

// A camera rig: projection type plus the direction the camera looks from.
// Separate from camera modes (which decide *where* the focus is); main.ts
// switches rigs with V.
export interface CameraRig {
  name: string;
  camera: THREE.PerspectiveCamera | THREE.OrthographicCamera;
  pitch: number;  // radians from the ground
  yaw: number;    // radians, 0 = looking north (world -y)
  // Unit vectors on the ground (world xy): screen-up and screen-right.
  forward: { x: number; y: number };
  right: { x: number; y: number };
  // Ground distances along screen-vertical are foreshortened by this at the focus.
  groundYScale: number;
}

export function createRig(name: string): CameraRig {
  const cfg = CONFIG.camera3d.rigs[name];
  if (!cfg) throw new Error(`Unknown camera rig ${name}`);
  const pitch = (cfg.pitchDeg * Math.PI) / 180;
  const yaw = (cfg.yawDeg * Math.PI) / 180;
  const camera = cfg.projection === 'perspective'
    ? new THREE.PerspectiveCamera(cfg.fovDeg, 1, 0.1, 1000)
    : new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 1000);
  return {
    name,
    camera,
    pitch,
    yaw,
    // yaw 0: forward = north (0,-1), right = east (1,0); yaw turns them clockwise on the map.
    forward: { x: Math.sin(yaw), y: -Math.cos(yaw) },
    right: { x: Math.cos(yaw), y: Math.sin(yaw) },
    groundYScale: Math.sin(pitch),
  };
}

// Screen px delta -> ground delta (world units) at the focus point.
export function screenDeltaToGround(rig: CameraRig, dxPx: number, dyPx: number, zoom: number, out: { x: number; y: number }): void {
  const rx = dxPx / zoom;
  const fy = -dyPx / (zoom * rig.groundYScale);   // screen-down is backwards along forward
  out.x = rig.right.x * rx + rig.forward.x * fy;
  out.y = rig.right.y * rx + rig.forward.y * fy;
}

// Zoom at which the whole world fits on screen (approximately: the frustum's
// ground footprint is a trapezoid/rotated rect, this uses the focus-plane scale).
export function minZoomFor(view: ViewportInfo, worldW: number, worldH: number): number {
  return Math.min(view.widthPx / worldW, view.heightPx / worldH);
}

// Approximate axis-aligned view rectangle in world coords for a state (exact
// only for the top-down case; used by the HUD readout and minimap).
export function viewRect(state: CameraState, view: ViewportInfo, rig?: CameraRig) {
  const halfW = view.widthPx / (2 * state.zoom);
  const halfH = view.heightPx / (2 * state.zoom * (rig?.groundYScale ?? 1));
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

// Distance from the camera to its focus point. Perspective: the focus plane
// must show heightPx / zoom world units top to bottom. Orthographic: any
// distance works, so a fixed one that keeps the world inside near/far.
export function focusDistanceFor(rig: CameraRig, state: CameraState, view: ViewportInfo): number {
  if (rig.camera instanceof THREE.PerspectiveCamera) {
    return view.heightPx / (2 * state.zoom) / Math.tan((rig.camera.fov * Math.PI) / 360);
  }
  return CONFIG.camera3d.orthoDistance;
}

// Push a CameraState into the rig's camera. Scene coords are world coords
// with y negated (see main.ts: the world group is scaled by -1 in y so the
// y-down world sits in three's y-up scene with z as height). The camera sits
// behind the focus (opposite the rig's forward direction) looking along
// forward and down at pitch, so screen-up is forward on the ground.
export function applyToCamera(rig: CameraRig, state: CameraState, view: ViewportInfo): void {
  const d = focusDistanceFor(rig, state, view);
  const back = d * Math.cos(rig.pitch);
  const fx = state.centerX;
  const fy = state.centerY;
  const camera = rig.camera;
  camera.position.set(fx - rig.forward.x * back, -(fy - rig.forward.y * back), d * Math.sin(rig.pitch));
  camera.up.set(0, 0, 1);
  camera.lookAt(fx, -fy, 0);

  if (camera instanceof THREE.PerspectiveCamera) {
    const aspect = view.widthPx / view.heightPx;
    const near = Math.max(0.1, d * 0.02);
    const far = d * 4 + 100;
    if (camera.aspect !== aspect || camera.near !== near || camera.far !== far) {
      camera.aspect = aspect;
      camera.near = near;
      camera.far = far;
      camera.updateProjectionMatrix();
    }
  } else {
    const halfW = view.widthPx / (2 * state.zoom);
    const halfH = view.heightPx / (2 * state.zoom);
    if (camera.left !== -halfW || camera.top !== halfH || camera.far !== d * 3) {
      camera.left = -halfW;
      camera.right = halfW;
      camera.top = halfH;
      camera.bottom = -halfH;
      camera.near = 1;
      camera.far = d * 3;
      camera.updateProjectionMatrix();
    }
  }
  camera.updateMatrixWorld();
}

const tmpA = new THREE.Vector3();
const tmpB = new THREE.Vector3();

// World point (x, y, height z) -> screen px. Returns false if behind the camera.
export function worldToScreen(
  camera: THREE.Camera, view: ViewportInfo,
  x: number, y: number, z: number, out: { x: number; y: number },
): boolean {
  tmpA.set(x, -y, z).project(camera);
  if (tmpA.z > 1) return false;
  out.x = (tmpA.x + 1) / 2 * view.widthPx;
  out.y = (1 - tmpA.y) / 2 * view.heightPx;
  return true;
}

// Screen px -> world point on the ground plane (z = 0). Rays that miss the
// ground (or hit past maxDistance) are clamped to maxDistance along the ray.
export function screenToGround(
  camera: THREE.Camera, view: ViewportInfo,
  px: number, py: number, maxDistance: number, out: { x: number; y: number },
): void {
  const ndcX = (px / view.widthPx) * 2 - 1;
  const ndcY = 1 - (py / view.heightPx) * 2;
  // Two-point unproject works for both projections (a perspective camera's
  // rays fan out from its position; an orthographic camera's are parallel).
  tmpA.set(ndcX, ndcY, -1).unproject(camera);
  tmpB.set(ndcX, ndcY, 1).unproject(camera).sub(tmpA).normalize();
  let t = maxDistance;
  if (tmpB.z < -1e-6) t = Math.min(t, -tmpA.z / tmpB.z);
  out.x = tmpA.x + tmpB.x * t;
  out.y = -(tmpA.y + tmpB.y * t);
}
