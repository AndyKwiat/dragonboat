// three.js port of the Rust/Bevy voxel demo in ../src — same seed, same
// terrain algorithm, same entity counts — for an FPS / iteration-speed
// comparison. Kept deliberately close to the Rust structure.

import * as THREE from "three";

// ---------------------------------------------------------------------------
// config.rs
// ---------------------------------------------------------------------------
const WORLD_WIDTH = 256;
const WORLD_DEPTH = 256;
const WORLD_SEED = 12345n;
const TARGET_VISIBLE_VOXELS = 200.0;
const TERRAIN_MAX_HEIGHT = 8;
const WATER_LEVEL = 1;
const TERRAIN_CHUNK_SIZE = 16;
const MOVER_COUNT = 30000;
const MOVER_MIN_SPEED = 2.0;
const MOVER_MAX_SPEED = 6.0;
const TREE_COUNT = 220;
const ROCK_COUNT = 60;
const HOUSE_COUNT = 360;
const CAMERA_AZIMUTH_DEGREES = 45.0;
const CAMERA_ELEVATION_DEGREES = 38.0;

// ---------------------------------------------------------------------------
// world.rs — SplitMix64 + value noise, u64 semantics via BigInt
// ---------------------------------------------------------------------------
const U64 = (1n << 64n) - 1n;

function splitmix64(state) {
  state.v = (state.v + 0x9e3779b97f4a7c15n) & U64;
  let z = state.v;
  z = ((z ^ (z >> 30n)) * 0xbf58476d1ce4e5b9n) & U64;
  z = ((z ^ (z >> 27n)) * 0x94d049bb133111ebn) & U64;
  return z ^ (z >> 31n);
}

function hash01(seed, x, z) {
  const xs = (BigInt.asUintN(64, BigInt(x)) * 0x517cc1b727220a95n) & U64;
  const zs = (BigInt.asUintN(64, BigInt(z)) * 0x2545f4914f6cdd1dn) & U64;
  const s = { v: seed ^ xs ^ zs };
  return Number(splitmix64(s) >> 40n) / 2 ** 24;
}

const smoothstep = (t) => t * t * (3 - 2 * t);

function valueNoise(seed, x, z) {
  const x0 = Math.floor(x), z0 = Math.floor(z);
  const tx = smoothstep(x - x0), tz = smoothstep(z - z0);
  const a = hash01(seed, x0, z0);
  const b = hash01(seed, x0 + 1, z0);
  const c = hash01(seed, x0, z0 + 1);
  const d = hash01(seed, x0 + 1, z0 + 1);
  const top = a + (b - a) * tx;
  const bottom = c + (d - c) * tx;
  return top + (bottom - top) * tz;
}

function fbm(seed, x, z) {
  let total = 0, amplitude = 1, frequency = 1, norm = 0;
  for (let octave = 0n; octave < 3n; octave++) {
    total += valueNoise(seed ^ (octave * 0x9e37n), x * frequency, z * frequency) * amplitude;
    norm += amplitude;
    amplitude *= 0.5;
    frequency *= 2;
  }
  return total / norm;
}

// TerrainType enum
const WATER = 0, SAND = 1, GRASS = 2, DRY_GRASS = 3, EARTH = 4, ROCK = 5;

function generateWorld(seed) {
  const heights = new Uint8Array(WORLD_WIDTH * WORLD_DEPTH);
  const types = new Uint8Array(WORLD_WIDTH * WORLD_DEPTH);
  const variants = new Uint8Array(WORLD_WIDTH * WORLD_DEPTH);
  for (let z = 0; z < WORLD_DEPTH; z++) {
    for (let x = 0; x < WORLD_WIDTH; x++) {
      const base = fbm(seed, x * 0.02, z * 0.02);
      const hills = fbm(seed ^ 0xabcdn, x * 0.008, z * 0.008);
      let h = base * 3.5 + hills * 6.0 - 1.2;

      const lake = Math.sqrt((x - 70) ** 2 + (z - 170) ** 2);
      if (lake < 42) h -= (1 - lake / 42) * 4;

      let height = Math.min(Math.max(Math.round(h), 0), TERRAIN_MAX_HEIGHT);
      const variantRoll = hash01(seed ^ 0xc0ffeen, x, z);
      const moisture = fbm(seed ^ 0x5eedn, x * 0.015, z * 0.015);

      let type;
      if (height <= WATER_LEVEL) { type = WATER; height = WATER_LEVEL; }
      else if (height === WATER_LEVEL + 1) type = SAND;
      else if (height >= TERRAIN_MAX_HEIGHT - 1) type = ROCK;
      else if (moisture < 0.35) type = DRY_GRASS;
      else if (variantRoll > 0.93) type = EARTH;
      else type = GRASS;

      const i = z * WORLD_WIDTH + x;
      heights[i] = height;
      types[i] = type;
      variants[i] = Math.floor(variantRoll * 255);
    }
  }
  return { heights, types, variants };
}

const world = generateWorld(WORLD_SEED);
const cellHeight = (x, z) => world.heights[z * WORLD_WIDTH + x];
const heightAt = (x, z) => {
  const xi = Math.min(Math.max(Math.floor(x), 0), WORLD_WIDTH - 1);
  const zi = Math.min(Math.max(Math.floor(z), 0), WORLD_DEPTH - 1);
  return cellHeight(xi, zi);
};
const isWater = (x, z) => {
  const xi = Math.min(Math.max(Math.floor(x), 0), WORLD_WIDTH - 1);
  const zi = Math.min(Math.max(Math.floor(z), 0), WORLD_DEPTH - 1);
  return world.types[zi * WORLD_WIDTH + xi] === WATER;
};

// ---------------------------------------------------------------------------
// terrain.rs — colors + chunk meshing
// ---------------------------------------------------------------------------
const BASE_COLORS = {
  [WATER]: [0.16, 0.32, 0.55],
  [SAND]: [0.76, 0.7, 0.5],
  [GRASS]: [0.26, 0.46, 0.21],
  [DRY_GRASS]: [0.5, 0.48, 0.24],
  [EARTH]: [0.45, 0.36, 0.24],
  [ROCK]: [0.52, 0.52, 0.5],
};

function cellColor(type, height, variant) {
  const v = variant / 255;
  const h = height / TERRAIN_MAX_HEIGHT;
  const [r, g, b] = BASE_COLORS[type];
  const shade = 0.92 + v * 0.16 + h * 0.12;
  return [r * shade, g * shade, b * shade];
}

const neighborHeight = (x, z) =>
  x < 0 || z < 0 || x >= WORLD_WIDTH || z >= WORLD_DEPTH ? 0 : cellHeight(x, z);

function buildChunkGeometry(cx, cz) {
  const positions = [], normals = [], colors = [], indices = [];
  const quad = (verts, normal, color) => {
    const base = positions.length / 3;
    for (const v of verts) positions.push(...v);
    for (let i = 0; i < 4; i++) { normals.push(...normal); colors.push(...color); }
    indices.push(base, base + 1, base + 2, base, base + 2, base + 3);
  };

  const x0 = cx * TERRAIN_CHUNK_SIZE, z0 = cz * TERRAIN_CHUNK_SIZE;
  for (let z = z0; z < Math.min(z0 + TERRAIN_CHUNK_SIZE, WORLD_DEPTH); z++) {
    for (let x = x0; x < Math.min(x0 + TERRAIN_CHUNK_SIZE, WORLD_WIDTH); x++) {
      const i = z * WORLD_WIDTH + x;
      const h = world.heights[i];
      const top = cellColor(world.types[i], h, world.variants[i]);
      const side = top.map((c) => c * 0.72);

      quad(
        [[x, h, z], [x, h, z + 1], [x + 1, h, z + 1], [x + 1, h, z]],
        [0, 1, 0], top,
      );

      const sides = [
        [-1, 0, [-1, 0, 0]], [1, 0, [1, 0, 0]],
        [0, -1, [0, 0, -1]], [0, 1, [0, 0, 1]],
      ];
      for (const [dx, dz, normal] of sides) {
        const nh = neighborHeight(x + dx, z + dz);
        if (nh >= h) continue;
        const lo = nh, hi = h;
        let verts;
        if (dx === -1) verts = [[x, hi, z], [x, lo, z], [x, lo, z + 1], [x, hi, z + 1]];
        else if (dx === 1) verts = [[x + 1, hi, z + 1], [x + 1, lo, z + 1], [x + 1, lo, z], [x + 1, hi, z]];
        else if (dz === -1) verts = [[x + 1, hi, z], [x + 1, lo, z], [x, lo, z], [x, hi, z]];
        else verts = [[x, hi, z + 1], [x, lo, z + 1], [x + 1, lo, z + 1], [x + 1, hi, z + 1]];
        quad(verts, normal, side);
      }
    }
  }

  const geo = new THREE.BufferGeometry();
  geo.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  geo.setAttribute("normal", new THREE.Float32BufferAttribute(normals, 3));
  geo.setAttribute("color", new THREE.Float32BufferAttribute(colors, 3));
  geo.setIndex(indices);
  return geo;
}

// ---------------------------------------------------------------------------
// Scene / camera.rs
// ---------------------------------------------------------------------------
const scene = new THREE.Scene();
scene.background = new THREE.Color().setRGB(0.1, 0.12, 0.16, THREE.SRGBColorSpace);

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(innerWidth, innerHeight);
renderer.setPixelRatio(devicePixelRatio);
renderer.shadowMap.enabled = true;
document.body.appendChild(renderer.domElement);

const center = new THREE.Vector3(WORLD_WIDTH * 0.5, 0, WORLD_DEPTH * 0.5);
const azimuth = THREE.MathUtils.degToRad(CAMERA_AZIMUTH_DEGREES);
const elevation = THREE.MathUtils.degToRad(CAMERA_ELEVATION_DEGREES);
const viewportHeight = TARGET_VISIBLE_VOXELS * Math.sin(elevation);

const camera = new THREE.OrthographicCamera();
function frameCamera() {
  const aspect = innerWidth / innerHeight;
  camera.top = viewportHeight / 2;
  camera.bottom = -viewportHeight / 2;
  camera.left = (-viewportHeight * aspect) / 2;
  camera.right = (viewportHeight * aspect) / 2;
  camera.near = -600;
  camera.far = 1200;
  camera.updateProjectionMatrix();
}
frameCamera();
const offset = new THREE.Vector3(
  Math.cos(elevation) * Math.cos(azimuth),
  Math.sin(elevation),
  Math.cos(elevation) * Math.sin(azimuth),
).multiplyScalar(400);
camera.position.copy(center).add(offset);
camera.lookAt(center);

addEventListener("resize", () => {
  renderer.setSize(innerWidth, innerHeight);
  frameCamera();
});

// Lights (bevy: ambient 220 + sun 11000 lux with shadow maps)
scene.add(new THREE.AmbientLight(new THREE.Color().setRGB(0.85, 0.9, 1.0, THREE.SRGBColorSpace), 0.55));
const sun = new THREE.DirectionalLight(0xffffff, 2.4);
sun.position.copy(center).add(new THREE.Vector3(100, 200, 40));
sun.target.position.copy(center);
sun.castShadow = true;
sun.shadow.mapSize.set(2048, 2048);
const half = WORLD_WIDTH * 0.75;
Object.assign(sun.shadow.camera, { left: -half, right: half, top: half, bottom: -half, near: 1, far: 600 });
scene.add(sun, sun.target);

// Terrain chunks — one mesh per 16x16 chunk, shared vertex-color material.
const terrainMat = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 1.0, metalness: 0 });
for (let cz = 0; cz < WORLD_DEPTH / TERRAIN_CHUNK_SIZE; cz++) {
  for (let cx = 0; cx < WORLD_WIDTH / TERRAIN_CHUNK_SIZE; cx++) {
    const mesh = new THREE.Mesh(buildChunkGeometry(cx, cz), terrainMat);
    mesh.receiveShadow = true;
    scene.add(mesh);
  }
}

// ---------------------------------------------------------------------------
// Decorations (terrain.rs spawn_decorations) — same RNG stream & rules
// ---------------------------------------------------------------------------
const srgb = (r, g, b) => new THREE.Color().setRGB(r, g, b, THREE.SRGBColorSpace);
const rand01 = (rng) => Number(splitmix64(rng) >> 40n) / 2 ** 24;

const decoRng = { v: WORLD_SEED ^ 0xdec00001n };
const stdMat = (color) => new THREE.MeshStandardMaterial({ color, roughness: 1.0, metalness: 0 });

function instanced(geo, mat, count) {
  const m = new THREE.InstancedMesh(geo, mat, count);
  m.castShadow = true;
  m.receiveShadow = true;
  scene.add(m);
  return m;
}
const tmpMat = new THREE.Matrix4();
const setInst = (mesh, i, x, y, z) => mesh.setMatrixAt(i, tmpMat.makeTranslation(x, y, z));

// Trees
const trunks = instanced(new THREE.BoxGeometry(0.35, 1.0, 0.35), stdMat(srgb(0.35, 0.24, 0.14)), TREE_COUNT);
const canopies = instanced(new THREE.BoxGeometry(1.8, 1.6, 1.8), new THREE.MeshStandardMaterial({ roughness: 1.0, metalness: 0 }), TREE_COUNT);
const canopyColors = [srgb(0.16, 0.38, 0.16), srgb(0.22, 0.45, 0.18), srgb(0.3, 0.48, 0.14)];
{
  let placed = 0, attempts = 0;
  while (placed < TREE_COUNT && attempts < TREE_COUNT * 30) {
    attempts++;
    const x = Number(splitmix64(decoRng) % BigInt(WORLD_WIDTH)) + 0.5;
    const z = Number(splitmix64(decoRng) % BigInt(WORLD_DEPTH)) + 0.5;
    const i = Math.floor(z) * WORLD_WIDTH + Math.floor(x);
    if (world.types[i] !== GRASS) continue;
    const variant = world.variants[i];
    if ((variant * 7 + Math.floor(x / 16) * 13 + Math.floor(z / 16) * 29) % 5 > 1) continue;
    const y = world.heights[i];
    const colorIdx = Number(splitmix64(decoRng) % 3n);
    setInst(trunks, placed, x, y + 0.5, z);
    setInst(canopies, placed, x, y + 1.8, z);
    canopies.setColorAt(placed, canopyColors[colorIdx]);
    placed++;
  }
  trunks.count = canopies.count = placed;
}

// Rocks
const rocks = instanced(new THREE.BoxGeometry(1.0, 0.7, 1.2), stdMat(srgb(0.45, 0.45, 0.44)), ROCK_COUNT);
{
  let placed = 0, attempts = 0;
  while (placed < ROCK_COUNT && attempts < ROCK_COUNT * 30) {
    attempts++;
    const x = Number(splitmix64(decoRng) % BigInt(WORLD_WIDTH)) + 0.5;
    const z = Number(splitmix64(decoRng) % BigInt(WORLD_DEPTH)) + 0.5;
    const i = Math.floor(z) * WORLD_WIDTH + Math.floor(x);
    const t = world.types[i];
    if (t !== ROCK && t !== EARTH && t !== DRY_GRASS) continue;
    setInst(rocks, placed, x, world.heights[i] + 0.35, z);
    placed++;
  }
  rocks.count = placed;
}

// Houses
const walls = instanced(new THREE.BoxGeometry(3.0, 2.5, 4.0), stdMat(srgb(0.75, 0.68, 0.55)), HOUSE_COUNT);
const roofs = instanced(new THREE.BoxGeometry(3.4, 0.8, 4.4), stdMat(srgb(0.6, 0.25, 0.18)), HOUSE_COUNT);
{
  let placed = 0, attempts = 0;
  while (placed < HOUSE_COUNT && attempts < HOUSE_COUNT * 60) {
    attempts++;
    const x = Number(splitmix64(decoRng) % BigInt(WORLD_WIDTH - 8)) + 4.0;
    const z = Number(splitmix64(decoRng) % BigInt(WORLD_DEPTH - 8)) + 4.0;
    const i = Math.floor(z) * WORLD_WIDTH + Math.floor(x);
    if (world.types[i] !== GRASS) continue;
    const y = world.heights[i];
    setInst(walls, placed, x, y + 1.25, z);
    setInst(roofs, placed, x, y + 2.9, z);
    placed++;
  }
  walls.count = roofs.count = placed;
}

// ---------------------------------------------------------------------------
// Movers (movers.rs) — same RNG stream, spawn rules, movement rules
// ---------------------------------------------------------------------------
const BODY_HEIGHT = 0.9;
const PALETTE = [
  srgb(0.85, 0.25, 0.2), srgb(0.2, 0.45, 0.85), srgb(0.9, 0.75, 0.2),
  srgb(0.85, 0.85, 0.88), srgb(0.3, 0.25, 0.35),
];

const bodies = instanced(
  new THREE.BoxGeometry(0.5, BODY_HEIGHT, 0.35),
  new THREE.MeshStandardMaterial({ roughness: 0.9, metalness: 0 }),
  MOVER_COUNT,
);
const heads = instanced(
  new THREE.BoxGeometry(0.4, 0.4, 0.4),
  new THREE.MeshStandardMaterial({ color: srgb(0.87, 0.72, 0.58), roughness: 0.9, metalness: 0 }),
  MOVER_COUNT,
);

const mx = new Float32Array(MOVER_COUNT);
const mz = new Float32Array(MOVER_COUNT);
const my = new Float32Array(MOVER_COUNT);
const mHeading = new Float32Array(MOVER_COUNT);
const mSpeed = new Float32Array(MOVER_COUNT);
const mRetarget = new Float32Array(MOVER_COUNT);
const mRng = new Array(MOVER_COUNT);

{
  const rng = { v: WORLD_SEED ^ 0x0ddba115n };
  let spawned = 0;
  while (spawned < MOVER_COUNT) {
    const x = rand01(rng) * (WORLD_WIDTH - 2) + 1;
    const z = rand01(rng) * (WORLD_DEPTH - 2) + 1;
    if (isWater(x, z)) continue;
    mx[spawned] = x;
    mz[spawned] = z;
    my[spawned] = heightAt(x, z);
    mHeading[spawned] = rand01(rng) * Math.PI * 2;
    mSpeed[spawned] = MOVER_MIN_SPEED + rand01(rng) * (MOVER_MAX_SPEED - MOVER_MIN_SPEED);
    mRng[spawned] = splitmix64(rng);
    mRetarget[spawned] = 1.0 + rand01(rng) * 4.0;
    bodies.setColorAt(spawned, PALETTE[spawned % PALETTE.length]);
    spawned++;
  }
}

const quatTmp = new THREE.Quaternion();
const posTmp = new THREE.Vector3();
const sclTmp = new THREE.Vector3(1, 1, 1);
const yAxis = new THREE.Vector3(0, 1, 0);

function moveMovers(dt) {
  const margin = 2, maxX = WORLD_WIDTH - margin, maxZ = WORLD_DEPTH - margin;
  for (let i = 0; i < MOVER_COUNT; i++) {
    mRetarget[i] -= dt;
    if (mRetarget[i] <= 0) {
      const rng = { v: mRng[i] };
      mHeading[i] += (rand01(rng) - 0.5) * Math.PI;
      mRetarget[i] = 1.5 + rand01(rng) * 4.0;
      mRng[i] = rng.v;
    }

    const step = mSpeed[i] * dt;
    let nextX = mx[i] + Math.cos(mHeading[i]) * step;
    let nextZ = mz[i] + Math.sin(mHeading[i]) * step;

    let bounced = false;
    if (nextX < margin || nextX > maxX) { mHeading[i] = Math.PI - mHeading[i]; bounced = true; }
    if (nextZ < margin || nextZ > maxZ) { mHeading[i] = -mHeading[i]; bounced = true; }
    if (!bounced && isWater(nextX, nextZ)) { mHeading[i] += Math.PI; bounced = true; }
    if (bounced) { nextX = mx[i]; nextZ = mz[i]; }

    mx[i] = nextX;
    mz[i] = nextZ;
    my[i] += (heightAt(nextX, nextZ) - my[i]) * Math.min(dt * 12, 1);

    quatTmp.setFromAxisAngle(yAxis, -mHeading[i]);
    posTmp.set(nextX, my[i] + BODY_HEIGHT * 0.5, nextZ);
    bodies.setMatrixAt(i, tmpMat.compose(posTmp, quatTmp, sclTmp));
    posTmp.y = my[i] + BODY_HEIGHT + 0.2;
    heads.setMatrixAt(i, tmpMat.compose(posTmp, quatTmp, sclTmp));
  }
  bodies.instanceMatrix.needsUpdate = true;
  heads.instanceMatrix.needsUpdate = true;
}

// ---------------------------------------------------------------------------
// diagnostics.rs — overlay + periodic console fps log
// ---------------------------------------------------------------------------
const overlay = document.getElementById("overlay");
let fpsSmoothed = null;
let nextLog = 0;

function updateDiagnostics(dt, elapsed) {
  const fps = 1 / dt;
  fpsSmoothed = fpsSmoothed === null ? fps : fpsSmoothed + (fps - fpsSmoothed) * 0.05;
  overlay.textContent =
    `JS -> three.js -> WebGL2\n` +
    `FPS: ${fpsSmoothed.toFixed(0)}\n` +
    `Frame: ${(dt * 1000).toFixed(1)} ms\n` +
    `Movers: ${MOVER_COUNT}\n` +
    `World: ${WORLD_WIDTH} x ${WORLD_DEPTH}\n` +
    `Visible scale: ~${TARGET_VISIBLE_VOXELS.toFixed(0)} voxels`;
  if (elapsed >= nextLog) {
    nextLog = elapsed + 5;
    console.info(`fps: ${fpsSmoothed.toFixed(0)} (movers: ${MOVER_COUNT})`);
  }
}

console.info(
  `renderer initialized: ${renderer.capabilities.isWebGL2 ? "WebGL2" : "WebGL1"} ` +
    `(${performance.now().toFixed(0)} ms since navigation)`,
);

let last = performance.now();
renderer.setAnimationLoop(() => {
  const now = performance.now();
  const dt = Math.min((now - last) / 1000, 0.1);
  last = now;
  moveMovers(dt);
  updateDiagnostics(dt, now / 1000);
  renderer.render(scene, camera);
});
