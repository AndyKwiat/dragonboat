import * as THREE from 'three';
import { CONFIG, TILE_SCALE } from './config';
import type { DMap } from './dmap';

// World coordinates are y-down with (0,0) at the map's top-left; the camera
// flips top/bottom to match, so dmap rows map straight into texture rows and
// scene positions equal world positions.

export function worldSize(map: DMap): { w: number; h: number } {
  return { w: map.width * TILE_SCALE, h: map.height * TILE_SCALE };
}

export function buildTerrainTexture(map: DMap): THREE.DataTexture {
  const pixels = new Uint8Array(map.width * map.height * 4);
  for (let i = 0; i < map.data.length; i++) {
    const color = CONFIG.terrainColors[map.data[i]!] ?? 0xff00ff;
    pixels[i * 4] = (color >> 16) & 0xff;
    pixels[i * 4 + 1] = (color >> 8) & 0xff;
    pixels[i * 4 + 2] = color & 0xff;
    pixels[i * 4 + 3] = 255;
  }
  const texture = new THREE.DataTexture(pixels, map.width, map.height, THREE.RGBAFormat);
  // No color space tag: the raw shader passes texel values straight through,
  // so the on-screen colors match the config hex values (and the minimap).
  texture.colorSpace = THREE.NoColorSpace;
  texture.magFilter = THREE.NearestFilter;
  texture.minFilter = THREE.NearestFilter;
  texture.generateMipmaps = false;
  texture.needsUpdate = true;
  return texture;
}

// Terrain plane with a shader-drawn tile grid that fades in when zoomed close.
export function createTerrainMesh(map: DMap): { mesh: THREE.Mesh; setGridOpacity(a: number): void } {
  const { w, h } = worldSize(map);
  const geometry = new THREE.PlaneGeometry(w, h);
  geometry.translate(w / 2, h / 2, 0);

  const uniforms = {
    uMap: { value: buildTerrainTexture(map) },
    uTileScale: { value: TILE_SCALE },
    uGridColor: { value: new THREE.Color(CONFIG.grid.color) },
    uGridOpacity: { value: 0 },
  };

  const material = new THREE.ShaderMaterial({
    uniforms,
    side: THREE.DoubleSide,  // the y-flipped camera reverses winding
    vertexShader: /* glsl */ `
      varying vec2 vUv;
      varying vec2 vWorldPos;
      void main() {
        vUv = uv;
        vWorldPos = position.xy;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      }
    `,
    fragmentShader: /* glsl */ `
      uniform sampler2D uMap;
      uniform float uTileScale;
      uniform vec3 uGridColor;
      uniform float uGridOpacity;
      varying vec2 vUv;
      varying vec2 vWorldPos;
      void main() {
        vec3 terrain = texture2D(uMap, vUv).rgb;
        vec2 tile = vWorldPos / uTileScale;
        vec2 g = abs(fract(tile - 0.5) - 0.5) / fwidth(tile);
        float line = 1.0 - min(min(g.x, g.y), 1.0);
        gl_FragColor = vec4(mix(terrain, uGridColor, line * uGridOpacity), 1.0);
      }
    `,
  });

  const mesh = new THREE.Mesh(geometry, material);
  return {
    mesh,
    setGridOpacity(a: number) { uniforms.uGridOpacity.value = a; },
  };
}

// Grid opacity for a given zoom (screen px per world unit).
export function gridOpacityFor(zoom: number): number {
  const pxPerTile = zoom * TILE_SCALE;
  const { fadeMinPxPerTile, fadeMaxPxPerTile, maxAlpha } = CONFIG.grid;
  const t = (pxPerTile - fadeMinPxPerTile) / (fadeMaxPxPerTile - fadeMinPxPerTile);
  return Math.min(Math.max(t, 0), 1) * maxAlpha;
}
