import * as THREE from 'three';
import type { CameraController, ViewportInfo } from './camera/CameraController';
import { applyToCamera, clampCamera, minZoomFor, screenToGround, worldToScreen } from './camera/CameraController';
import { FollowCameraController } from './camera/FollowCameraController';
import { FreeCameraController } from './camera/FreeCameraController';
import { createBoat } from './boat';
import { crewSpeedMultiplier } from './crew';
import { CITIES } from './cities';
import { createCityLabels } from './cityLabels';
import { createCityPopup } from './cityPopup';
import { CONFIG } from './config';
import { loadDmap } from './dmap';
import { createHud } from './hud';
import { createClock } from './clock';
import { createGameTime, timeScaleForZoom } from './gameTime';
import { createInput } from './input';
import { createMinimap } from './minimap';
import { createPlayer } from './player';
import { createRations, createWages } from './upkeep';
import { createTerrainMesh, gridOpacityFor, worldSize } from './terrain';
import { createTerrainChunks } from './terrainChunks';

const map = await loadDmap(CONFIG.mapUrl);
const { w: worldW, h: worldH } = worldSize(map);

const canvas = document.querySelector<HTMLCanvasElement>('#viewport')!;
const renderer = new THREE.WebGLRenderer({ canvas, antialias: false });
renderer.setPixelRatio(window.devicePixelRatio);
renderer.setClearColor(CONFIG.clearColor);

const scene = new THREE.Scene();
// Everything lives in this group, scaled -1 in y: local coords are world
// coords (y-down) with z as height, and three's y-up scene sees world -y as
// "north" so the pitched camera looks up the screen. three flips face winding
// for negatively-scaled objects, so meshes render normally.
const world = new THREE.Group();
world.scale.y = -1;
scene.add(world);
// The flat 2D terrain plane is the ground (and the water); raised columns
// are streamed on top of it in chunks.
const terrain = createTerrainMesh(map);
world.add(terrain.mesh);
const chunks = createTerrainChunks(map);
world.add(chunks.group);

const camera = new THREE.PerspectiveCamera(CONFIG.camera3d.fovDeg, 1, 0.1, 1000);

const view: ViewportInfo = { widthPx: window.innerWidth, heightPx: window.innerHeight };
const input = createInput(canvas);

const boat = createBoat();
world.add(boat.mesh);

// Camera modes: added to this record and switched via switchMode(), which
// hands the current state across.
const freeCamera = new FreeCameraController();
const followCamera = new FollowCameraController(boat);
const controllers: Record<string, CameraController> = {
  free: freeCamera,
  follow: followCamera,
};
// C cycles through these in order.
const MODE_CYCLE = ['follow', 'free'];
let active: CameraController = controllers.follow!;

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
  const state = active.getState();
  state.zoom = Math.max(state.zoom, minZoom);
  clampCamera(state, view, worldW, worldH);
}
window.addEventListener('resize', resize);
resize();

// Start on the boat at the follow camera's own zoom; the state handed in is
// only the fallback the mode inherits if it has none of its own.
active.enable({
  centerX: boat.x,
  centerY: boat.y,
  zoom: CONFIG.boat.followZoom,
});

const player = createPlayer();
const time = createGameTime(CONFIG.gameTime);
const hud = createHud(worldW, worldH);
document.body.appendChild(hud.element);
const rations = createRations(player, time, CONFIG.rations);
const wages = createWages(player, time, CONFIG.wages);
const clock = createClock(time, rations, player);
document.body.appendChild(clock.element);
const minimap = createMinimap(map);
document.body.appendChild(minimap.element);
const cityLabels = createCityLabels(CITIES);
document.body.appendChild(cityLabels.element);
const cityPopup = createCityPopup(player, time);
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

  // Zoomed out the whole voyage runs in fast-forward: one real second buys
  // more game seconds, so the boat covers more ground AND the clock moves on
  // by the same factor. Read from the live camera state, so it works in any
  // mode (one frame stale, which is invisible).
  const timeScale = timeScaleForZoom(active.getState().zoom, CONFIG.gameTime.zoomTimeScale);
  const gameDt = dt * timeScale;

  // The boat is drivable in every mode that follows it; in free mode WASD
  // belongs to the camera instead.
  if (active !== freeCamera) {
    // More hands, more speed.
    boat.maxSpeedScale = crewSpeedMultiplier(player.crew, CONFIG.crew);
    // Sub-stepped: at a big scale one frame is a long stretch of game time,
    // and a single huge Euler step would cut corners on turns.
    let remaining = gameDt;
    while (remaining > 0) {
      const step = Math.min(remaining, CONFIG.boat.maxStepSeconds);
      boat.update(step, input);
      remaining -= step;
    }
    // Being under way is what burns the day: an hour of game time per real
    // second of movement, whichever direction, including coasting to a stop. A
    // boat sitting still costs nothing, and free-camera panning is free (the
    // boat isn't driven there, so its stale speed mustn't count).
    if (boat.speed !== 0) {
      time.advanceHours(CONFIG.gameTime.hoursPerSecondSailing * gameDt);
    }
  }

  active.update(dt, input, view);
  const state = active.getState();
  clampCamera(state, view, worldW, worldH);

  applyToCamera(state, view, camera);
  terrain.setGridOpacity(gridOpacityFor(state.zoom));

  // Stream terrain chunks for the ground area the camera can see: the four
  // screen corners cast onto the ground, capped so a shallow view doesn't
  // ask for the whole world.
  const maxDist = CONFIG.chunks.maxViewDistance;
  const corners = [[0, 0], [view.widthPx, 0], [0, view.heightPx], [view.widthPx, view.heightPx]] as const;
  const bounds = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity };
  const gp = { x: 0, y: 0 };
  for (const [px, py] of corners) {
    screenToGround(camera, view, px, py, maxDist, gp);
    bounds.left = Math.min(bounds.left, gp.x);
    bounds.right = Math.max(bounds.right, gp.x);
    bounds.top = Math.min(bounds.top, gp.y);
    bounds.bottom = Math.max(bounds.bottom, gp.y);
  }
  chunks.update(bounds, state.centerX, state.centerY);

  renderer.render(scene, camera);

  // City labels are DOM on top of the canvas; hit-testing is ours, so the
  // hovered city has to be recomputed after the camera has settled.
  const hoveredCity = cityLabels.update(state, view, input.cursorPx, (x, y, z, out) => worldToScreen(camera, view, x, y, z, out));
  canvas.style.cursor = hoveredCity ? 'pointer' : 'default';
  if (clicked) {
    if (hoveredCity) cityPopup.show(hoveredCity);
    else cityPopup.hide();
  }
  if (escaped) cityPopup.hide();

  // Crew upkeep runs on game time, so it only bites while sailing or trading.
  // An open market panel has to be told, or it shows a stale hold and purse.
  const charged = rations.update() + wages.update();
  if (charged && cityPopup.isOpen()) cityPopup.refresh();
  clock.update();
  minimap.update(state, view);
  hud.update(state, view, minZoomFor(view, worldW, worldH), active.name, timeScale, chunks.stats());

  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
