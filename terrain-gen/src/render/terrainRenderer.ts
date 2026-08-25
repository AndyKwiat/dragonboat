/** Three.js terrain renderer.
 *  Heights + tile ids live in DataTextures; a quadtree of fixed 32x32 grid
 *  patches (with skirts to hide LOD cracks) is selected per frame by camera
 *  distance, and the vertex shader displaces each patch from the height
 *  texture. Mesh density is therefore independent of map resolution, so an
 *  8192 map renders as cheaply as a 512 one. Map size must be a power of 2. */
import * as THREE from "three";
import { TILE_COLORS } from "../core/types";

/** What the renderer needs: the two map arrays plus dimensions. */
export interface TerrainData {
  size: number;
  worldSize: number;
  height: Float32Array;
  tiles: Uint8Array;
}

const PATCH_RES = 32; // quads per patch side

const VERT = /* glsl */ `
  attribute vec2 aCell;   // voxel geometry: center of this vertex's cell, patch coords
  uniform sampler2D uHeight;
  uniform vec2 uOffset;   // patch origin, world xz
  uniform float uScale;   // patch size, world units
  uniform float uWorldSize;
  uniform float uMapSize;
  uniform float uSkirtFloor; // absolute height skirts drop to (below HEIGHT_MIN)
  uniform float uVoxel;   // 1 = voxel mode
  varying vec2 vUv;       // map-space uv
  varying vec3 vWorld;
  void main() {
    vec2 xz = uOffset + position.xz * uScale;
    vUv = xz / uWorldSize + 0.5;
    vec2 uvS = vUv;
    if (uVoxel > 0.5) {
      // every vertex of a cell samples the same texel (the cell center) so
      // the top is flat and the +-eps boundary verts become vertical walls
      vec2 cu = (uOffset + aCell * uScale) / uWorldSize + 0.5;
      uvS = (floor(cu * uMapSize) + 0.5) / uMapSize;
    }
    float h = texture2D(uHeight, uvS).r;
    if (position.y < -0.5) h = uSkirtFloor; // skirt vertex: drop to the floor so any LOD crack is covered
    vWorld = vec3(xz.x, h, xz.y);
    gl_Position = projectionMatrix * modelViewMatrix * vec4(vWorld, 1.0);
  }
`;

const FRAG = /* glsl */ `
  precision highp float;
  uniform sampler2D uHeight;
  uniform sampler2D uTiles;
  uniform vec3 uPalette[5];
  uniform float uTexel;     // 1 / mapSize
  uniform float uWorldSize;
  uniform vec3 uSunDir;
  uniform float uVoxel;
  varying vec2 vUv;
  varying vec3 vWorld;
  void main() {
    float idx = texture2D(uTiles, vUv).r * 255.0;
    vec3 col = uPalette[int(clamp(idx + 0.5, 0.0, 4.0))];
    vec3 n;
    if (uVoxel > 0.5) {
      // flat shading from screen-space derivatives: flat tops, shaded walls
      n = normalize(cross(dFdx(vWorld), dFdy(vWorld)));
      if (n.y < 0.0) n = -n;
    } else {
      // normal from height texture central differences
      float step = uTexel * 2.0;
      float hx1 = texture2D(uHeight, vUv + vec2(step, 0.0)).r;
      float hx0 = texture2D(uHeight, vUv - vec2(step, 0.0)).r;
      float hy1 = texture2D(uHeight, vUv + vec2(0.0, step)).r;
      float hy0 = texture2D(uHeight, vUv - vec2(0.0, step)).r;
      float ws = step * 2.0 * uWorldSize;
      n = normalize(vec3(hx0 - hx1, ws, hy0 - hy1));
    }
    float diff = max(dot(n, normalize(uSunDir)), 0.0);
    float light = 0.45 + 0.6 * diff;
    gl_FragColor = vec4(col * light, 1.0);
  }
`;

/** Grid patch from a 1D list of vertex coordinates (same for x and z), plus
 *  a skirt ring. `cellOf(i)` maps a coordinate index to its cell index so
 *  the aCell attribute can carry the cell center (used by voxel mode). */
function buildGrid(coords: number[], cellOf: (i: number) => number, cells: number): THREE.BufferGeometry {
  const n = coords.length;
  const pos: number[] = [], cell: number[] = [], idx: number[] = [];
  const center = (i: number) => (cellOf(i) + 0.5) / cells;
  for (let z = 0; z < n; z++) for (let x = 0; x < n; x++) {
    pos.push(coords[x], 0, coords[z]);
    cell.push(center(x), center(z));
  }
  for (let z = 0; z < n - 1; z++) for (let x = 0; x < n - 1; x++) {
    const a = z * n + x, b = a + 1, c = a + n, d = c + 1;
    idx.push(a, c, b, b, c, d);
  }
  // skirt ring: duplicate edge verts flagged y=-1, connect down
  const edge: number[] = [];
  for (let x = 0; x < n; x++) edge.push(x);                    // top
  for (let z = 1; z < n; z++) edge.push(z * n + (n - 1));      // right
  for (let x = n - 2; x >= 0; x--) edge.push((n - 1) * n + x); // bottom
  for (let z = n - 2; z >= 1; z--) edge.push(z * n);           // left
  const base = pos.length / 3;
  for (let i = 0; i < edge.length; i++) {
    const e = edge[i];
    pos.push(pos[e * 3], -1, pos[e * 3 + 2]);
    cell.push(cell[e * 2], cell[e * 2 + 1]);
  }
  for (let i = 0; i < edge.length; i++) {
    const a = edge[i], b = edge[(i + 1) % edge.length];
    const sa = base + i, sb = base + ((i + 1) % edge.length);
    idx.push(a, sa, b, b, sa, sb);
  }
  const g = new THREE.BufferGeometry();
  g.setAttribute("position", new THREE.Float32BufferAttribute(pos, 3));
  g.setAttribute("aCell", new THREE.Float32BufferAttribute(cell, 2));
  g.setIndex(idx);
  return g;
}

/** Smooth patch: PATCH_RES+1 evenly spaced verts per side. */
function buildPatchGeometry(): THREE.BufferGeometry {
  const coords: number[] = [];
  for (let i = 0; i <= PATCH_RES; i++) coords.push(i / PATCH_RES);
  return buildGrid(coords, (i) => Math.min(PATCH_RES - 1, i), PATCH_RES);
}

/** Voxel patch: two verts per cell per axis, at the cell boundaries pulled
 *  in by eps. Both verts of a cell sample the cell-center height (shader),
 *  so the strip between neighboring cells (2*eps wide) becomes a wall. */
function buildVoxelGeometry(): THREE.BufferGeometry {
  const eps = 0.02 / PATCH_RES;
  const coords: number[] = [];
  for (let c = 0; c < PATCH_RES; c++) {
    coords.push(c === 0 ? 0 : c / PATCH_RES + eps);
    coords.push(c === PATCH_RES - 1 ? 1 : (c + 1) / PATCH_RES - eps);
  }
  return buildGrid(coords, (i) => i >> 1, PATCH_RES);
}

interface PatchMesh extends THREE.Mesh {
  material: THREE.ShaderMaterial;
}

export class TerrainRenderer {
  readonly group = new THREE.Group();
  readonly pickPlane: THREE.Mesh;
  /** larger = more detail (patches split closer to camera) */
  lodFactor = 3.0;
  private worldSize: number;
  private mapSize = 0;
  private heightTex: THREE.DataTexture | null = null;
  private tileTex: THREE.DataTexture | null = null;
  private smoothGeometry = buildPatchGeometry();
  private voxelGeometry = buildVoxelGeometry();
  private voxel = false;
  private pool: PatchMesh[] = [];
  private used = 0;
  private wireframe = false;

  constructor(worldSize: number) {
    this.worldSize = worldSize;
    this.pickPlane = new THREE.Mesh(
      new THREE.PlaneGeometry(worldSize, worldSize),
      new THREE.MeshBasicMaterial({ visible: false }),
    );
    this.pickPlane.rotation.x = -Math.PI / 2;
    this.pickPlane.name = "pickPlane";
    this.group.add(this.pickPlane);
  }

  setOutput(out: TerrainData): void {
    const size = out.size;
    if (size !== this.mapSize) {
      this.heightTex?.dispose();
      this.tileTex?.dispose();
      this.heightTex = new THREE.DataTexture(out.height as unknown as Float32Array<ArrayBuffer>, size, size, THREE.RedFormat, THREE.FloatType);
      this.heightTex.magFilter = THREE.LinearFilter;
      this.heightTex.minFilter = THREE.LinearFilter;
      this.tileTex = new THREE.DataTexture(out.tiles as unknown as Uint8Array<ArrayBuffer>, size, size, THREE.RedFormat, THREE.UnsignedByteType);
      this.tileTex.magFilter = THREE.NearestFilter;
      this.tileTex.minFilter = THREE.NearestFilter;
      this.mapSize = size;
      for (const m of this.pool) {
        m.material.uniforms.uHeight.value = this.heightTex;
        m.material.uniforms.uTiles.value = this.tileTex;
        m.material.uniforms.uTexel.value = 1 / size;
        m.material.uniforms.uMapSize.value = size;
      }
    } else {
      this.heightTex!.image.data = out.height as unknown as Uint8ClampedArray;
      this.tileTex!.image.data = out.tiles as unknown as Uint8ClampedArray;
    }
    this.heightTex!.needsUpdate = true;
    this.tileTex!.needsUpdate = true;
  }

  private makePatch(): PatchMesh {
    const palette = TILE_COLORS.map((c) => new THREE.Vector3(c[0] / 255, c[1] / 255, c[2] / 255));
    const mat = new THREE.ShaderMaterial({
      vertexShader: VERT,
      fragmentShader: FRAG,
      wireframe: this.wireframe,
      uniforms: {
        uHeight: { value: this.heightTex },
        uTiles: { value: this.tileTex },
        uPalette: { value: palette },
        uOffset: { value: new THREE.Vector2() },
        uScale: { value: 1 },
        uWorldSize: { value: this.worldSize },
        uTexel: { value: 1 / Math.max(1, this.mapSize) },
        uMapSize: { value: Math.max(1, this.mapSize) },
        uSkirtFloor: { value: 0 },
        uVoxel: { value: this.voxel ? 1 : 0 },
        uSunDir: { value: new THREE.Vector3(0.5, 0.8, 0.35) },
      },
    });
    const mesh = new THREE.Mesh(this.voxel ? this.voxelGeometry : this.smoothGeometry, mat) as unknown as PatchMesh;
    mesh.frustumCulled = false;
    this.group.add(mesh);
    this.pool.push(mesh);
    return mesh;
  }

  /** Select LOD patches for this camera position. Call once per frame. */
  update(camera: THREE.Camera): void {
    if (!this.heightTex) return;
    this.used = 0;
    const half = this.worldSize / 2;
    const minPatch = (this.worldSize / this.mapSize) * PATCH_RES; // 1 texel per quad
    const cam = camera.position;
    const emit = (x: number, z: number, s: number) => {
      // distance from camera to patch AABB (xz), assume y near terrain
      const dx = Math.max(x - cam.x, 0, cam.x - (x + s));
      const dz = Math.max(z - cam.z, 0, cam.z - (z + s));
      const d = Math.hypot(dx, dz, cam.y * 0.6);
      if (s > minPatch && d < s * this.lodFactor) {
        const h = s / 2;
        emit(x, z, h); emit(x + h, z, h); emit(x, z + h, h); emit(x + h, z + h, h);
        return;
      }
      const mesh = this.pool[this.used] ?? this.makePatch();
      this.used++;
      mesh.visible = true;
      const u = mesh.material.uniforms;
      u.uOffset.value.set(x, z);
      u.uScale.value = s;
    };
    emit(-half, -half, this.worldSize);
    for (let i = this.used; i < this.pool.length; i++) this.pool[i].visible = false;
  }

  /** Voxel look: every map cell is a flat-topped column with vertical walls
   *  to its neighbors. Same quadtree LOD; coarser patches show bigger blocks. */
  setVoxel(v: boolean): void {
    this.voxel = v;
    for (const m of this.pool) {
      m.geometry = v ? this.voxelGeometry : this.smoothGeometry;
      m.material.uniforms.uVoxel.value = v ? 1 : 0;
    }
  }

  setWireframe(v: boolean): void {
    this.wireframe = v;
    for (const m of this.pool) m.material.wireframe = v;
  }

  get patchCount(): number { return this.used; }
}
