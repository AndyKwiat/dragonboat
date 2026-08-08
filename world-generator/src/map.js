import * as THREE from 'three';
import { MAP_CONFIG } from './config.js';
import { generateMapData } from './geometry.js';

// Terrain indices for export
export const TERRAIN = {
  OCEAN:     0,
  BEACH:     1,
  GRASSLAND: 2,
  FOREST:    3,
  MOUNTAIN:  4,
  RIVER:     5,
  CANAL:     6,
  CITY:      7,
};

function indexColor(idx) {
  return new THREE.Color(idx / 255, 0, 0);
}

// Convert outline points [{x,y}] to a THREE.Shape
function outlineToShape(points) {
  const shape = new THREE.Shape();
  points.forEach((p, i) => i === 0 ? shape.moveTo(p.x, p.y) : shape.lineTo(p.x, p.y));
  return shape;
}

// Build a triangle-strip mesh from river/tributary points [{x,y,width}]
function buildRiverGeometry(points) {
  const vertices = [];
  const indices = [];
  for (let i = 0; i < points.length; i++) {
    const p = points[i];
    const halfW = p.width / 2;
    let dx, dy;
    if (i < points.length - 1) {
      dx = points[i + 1].x - p.x;
      dy = points[i + 1].y - p.y;
    } else {
      dx = p.x - points[i - 1].x;
      dy = p.y - points[i - 1].y;
    }
    const len = Math.sqrt(dx * dx + dy * dy);
    const nx = -dy / len;
    const ny = dx / len;
    vertices.push(p.x + nx * halfW, p.y + ny * halfW, 0);
    vertices.push(p.x - nx * halfW, p.y - ny * halfW, 0);
    if (i < points.length - 1) {
      const vi = i * 2;
      indices.push(vi, vi + 1, vi + 2);
      indices.push(vi + 1, vi + 3, vi + 2);
    }
  }
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
  geo.setIndex(indices);
  return geo;
}

// Build a quad geometry for a straight canal
function buildCanalGeometry(pA, pB, width) {
  const halfW = width / 2;
  const dx = pB.x - pA.x;
  const dy = pB.y - pA.y;
  const len = Math.sqrt(dx * dx + dy * dy);
  const nx = -dy / len;
  const ny = dx / len;
  const vertices = new Float32Array([
    pA.x + nx * halfW, pA.y + ny * halfW, 0,
    pA.x - nx * halfW, pA.y - ny * halfW, 0,
    pB.x + nx * halfW, pB.y + ny * halfW, 0,
    pB.x - nx * halfW, pB.y - ny * halfW, 0,
  ]);
  const geo = new THREE.BufferGeometry();
  geo.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
  geo.setIndex([0, 1, 2, 1, 3, 2]);
  return geo;
}

// Create the island shader material (handles both display and index modes)
function createIslandMaterial(baseRadius, indexMode, colors) {
  const uniforms = { uBaseRadius: { value: baseRadius } };
  let colorBlock;

  if (indexMode) {
    uniforms.uColorA = { value: indexColor(TERRAIN.FOREST) };
    uniforms.uColorB = { value: indexColor(TERRAIN.GRASSLAND) };
    colorBlock = `
      uniform vec3 uColorA;
      uniform vec3 uColorB;
    `;
  } else {
    uniforms.uColorA = { value: new THREE.Color(...colors.forest) };
    uniforms.uColorB = { value: new THREE.Color(...colors.grassland) };
    colorBlock = `
      uniform vec3 uColorA;
      uniform vec3 uColorB;
    `;
  }

  return new THREE.ShaderMaterial({
    uniforms,
    vertexShader: `
      varying vec2 vPos;
      void main() {
        vPos = position.xy;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      }
    `,
    fragmentShader: `
      uniform float uBaseRadius;
      ${colorBlock}
      varying vec2 vPos;

      highp float hash(vec2 co) {
        highp float a = sin(dot(co, vec2(12.9898, 78.233))) * 43758.5453;
        a = fract(a);
        a = fract(a * 7.1312 + cos(dot(co, vec2(269.5, 183.3))) * 32187.1523);
        return a;
      }

      void main() {
        float dist = length(vPos);
        float forestProb = 1.0 - (dist / uBaseRadius);
        forestProb = clamp(forestProb * 0.8 + 0.1, 0.1, 0.9);
        float r = hash(gl_FragCoord.xy + vec2(0.347, 0.729));
        vec3 color = r < forestProb ? uColorA : uColorB;
        gl_FragColor = vec4(color, 1.0);
      }
    `,
  });
}

// Pick the right color: index color for export, display color otherwise
function mat(terrainType, displayColor, indexMode) {
  return new THREE.MeshBasicMaterial({
    color: indexMode ? indexColor(terrainType) : displayColor,
  });
}

// Main entry point: generate data and render to scene
export function createMap(scene, { indexMode = false } = {}) {
  const config = MAP_CONFIG;
  const data = generateMapData(config);
  const center = config.size / 2;
  const { colors } = config;

  // Z-layers
  const Z = { ocean: 0, beach: 0.05, island: 0.1, river: 0.15, canal: 0.16, mountain: 0.2, city: 0.25 };

  // Ocean
  const oceanGeo = new THREE.PlaneGeometry(config.size, config.size);
  const ocean = new THREE.Mesh(oceanGeo, mat(TERRAIN.OCEAN, colors.ocean, indexMode));
  ocean.position.set(center, center, Z.ocean);
  scene.add(ocean);

  // Beach
  const beachShape = outlineToShape(data.beachOutline);
  const beach = new THREE.Mesh(
    new THREE.ShapeGeometry(beachShape),
    mat(TERRAIN.BEACH, colors.beach, indexMode),
  );
  beach.position.set(center, center, Z.beach);
  scene.add(beach);

  // Island
  const islandShape = outlineToShape(data.islandOutline);
  const islandMat = createIslandMaterial(data.island.radius, indexMode, colors);
  const island = new THREE.Mesh(new THREE.ShapeGeometry(islandShape), islandMat);
  island.position.set(center, center, Z.island);
  scene.add(island);

  // Rivers + tributaries
  const riverMat = mat(TERRAIN.RIVER, colors.ocean, indexMode);
  for (const points of data.allWaterwayPoints) {
    const mesh = new THREE.Mesh(buildRiverGeometry(points), riverMat);
    mesh.position.set(center, center, Z.river);
    scene.add(mesh);
  }

  // Lakes
  for (const lake of data.lakeData) {
    const shape = outlineToShape(lake.outline);
    const mesh = new THREE.Mesh(new THREE.ShapeGeometry(shape), riverMat);
    mesh.position.set(center + lake.center.x, center + lake.center.y, Z.river);
    scene.add(mesh);
  }

  // Canals
  const canalMat = mat(TERRAIN.CANAL, colors.canal, indexMode);
  for (const canal of data.canalData) {
    const mesh = new THREE.Mesh(buildCanalGeometry(canal.pA, canal.pB, canal.width), canalMat);
    mesh.position.set(center, center, Z.canal);
    scene.add(mesh);
  }

  // Mountains
  const mtShape = outlineToShape(data.mountainOutline);
  const mt = new THREE.Mesh(
    new THREE.ShapeGeometry(mtShape),
    mat(TERRAIN.MOUNTAIN, colors.mountain, indexMode),
  );
  mt.position.set(center, center, Z.mountain);
  scene.add(mt);

  // Cities
  const cityMat = mat(TERRAIN.CITY, colors.city, indexMode);
  for (const city of data.cityPositions) {
    const mesh = new THREE.Mesh(
      new THREE.PlaneGeometry(data.cities.size, data.cities.size),
      cityMat,
    );
    mesh.position.set(center + city.x, center + city.y, Z.city);
    scene.add(mesh);
  }
}
