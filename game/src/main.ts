import * as THREE from 'three';
import type { CameraController, ViewportInfo } from './camera/CameraController';
import { applyToCamera, clampCamera, minZoomFor } from './camera/CameraController';
import { DynamicZoomCameraController } from './camera/DynamicZoomCameraController';
import { FollowCameraController } from './camera/FollowCameraController';
import { FreeCameraController } from './camera/FreeCameraController';
import { createBoat } from './boat';
import { CITIES } from './cities';
import { createCityLabels } from './cityLabels';
import { createCityPopup } from './cityPopup';
import { CONFIG } from './config';
import { loadDmap } from './dmap';
import { createHud } from './hud';
import { createInput } from './input';
import { createMinimap } from './minimap';
import { createTerrainMesh, gridOpacityFor, worldSize } from './terrain';

const map = await loadDmap(CONFIG.mapUrl);
const { w: worldW, h: worldH } = worldSize(map);

const canvas = document.querySelector<HTMLCanvasElement>('#viewport')!;
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false });
renderer.setPixelRatio(window.devicePixelRatio);
renderer.setClearColor(CONFIG.clearColor);

const scene = new THREE.Scene();
const terrain = createTerrainMesh(map);
scene.add(terrain.mesh);

const camera = new THREE.OrthographicCamera(0, 1, 0, 1, 0.1, 100);
camera.position.z = 10;

const view: ViewportInfo = { widthPx: window.innerWidth, heightPx: window.innerHeight };
const input = createInput(canvas);

const boat = createBoat();
scene.add(boat.mesh);

// Camera modes: added to this record and switched via switchMode(), which
// hands the current state across.
const freeCamera = new FreeCameraController();
const followCamera = new FollowCameraController(boat);
const dynamicCamera = new DynamicZoomCameraController(boat, CITIES);
const controllers: Record<string, CameraController> = {
  free: freeCamera,
  follow: followCamera,
  dynamic: dynamicCamera,
};
// C cycles through these in order.
const MODE_CYCLE = ['free', 'follow', 'dynamic'];
let active: CameraController = controllers.free!;

export function switchMode(name: string): void {
  const next = controllers[name];
  if (!next || next === active) return;
  const state = active.getState();
  active.disable();
  next.enable(state);
  active = next;
}

function resize() {
  view.widthPx = window.innerWidth;
  view.heightPx = window.innerHeight;
  renderer.setSize(view.widthPx, view.heightPx);
  const minZoom = minZoomFor(view, worldW, worldH);
  freeCamera.setMinZoom(minZoom);
  followCamera.setMinZoom(minZoom);
  dynamicCamera.setMinZoom(minZoom);
  const state = active.getState();
  state.zoom = Math.max(state.zoom, minZoom);
  clampCamera(state, view, worldW, worldH);
}
window.addEventListener('resize', resize);
resize();

// Start fully zoomed out, centered on the world.
active.enable({
  centerX: worldW / 2,
  centerY: worldH / 2,
  zoom: minZoomFor(view, worldW, worldH),
});

const hud = createHud(worldW, worldH);
document.body.appendChild(hud.element);
const minimap = createMinimap(map);
document.body.appendChild(minimap.element);
const cityLabels = createCityLabels(CITIES);
document.body.appendChild(cityLabels.element);
const cityPopup = createCityPopup();
document.body.appendChild(cityPopup.element);

let lastTime = performance.now();
function frame(now: number) {
  const dt = Math.min((now - lastTime) / 1000, 0.1);
  lastTime = now;

  // C toggles camera mode. WASD drives the boat in follow mode and pans the
  // camera in free mode, so only one of them consumes the keys per frame.
  if (input.pressed.has('KeyC')) {
    switchMode(MODE_CYCLE[(MODE_CYCLE.indexOf(active.name) + 1) % MODE_CYCLE.length]!);
  }
  // Latched here: the active controller clears the frame deltas in update().
  const clicked = input.clicked;
  const escaped = input.pressed.has('Escape');

  // The boat is drivable in every mode that follows it; the dynamic-zoom mode
  // also throttles its top speed based on how far it has zoomed in.
  if (active !== freeCamera) {
    boat.maxSpeedScale = active === dynamicCamera ? dynamicCamera.speedScale() : 1;
    boat.update(dt, input);
  }

  active.update(dt, input, view);
  const state = active.getState();
  clampCamera(state, view, worldW, worldH);
  applyToCamera(state, view, camera);
  terrain.setGridOpacity(gridOpacityFor(state.zoom));

  renderer.render(scene, camera);

  // City labels are DOM on top of the canvas; hit-testing is ours, so the
  // hovered city has to be recomputed after the camera has settled.
  const hoveredCity = cityLabels.update(state, view, input.cursorPx);
  canvas.style.cursor = hoveredCity ? 'pointer' : 'default';
  if (clicked) {
    if (hoveredCity) cityPopup.show(hoveredCity);
    else cityPopup.hide();
  }
  if (escaped) cityPopup.hide();

  minimap.update(state, view);
  hud.update(state, view, minZoomFor(view, worldW, worldH), active.name);

  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
