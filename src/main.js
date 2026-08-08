import * as THREE from 'three';
import { createMap, TERRAIN } from './map.js';

const MAP_SIZE = 1000;
const container = document.getElementById('map-container');

const scene = new THREE.Scene();

const camera = new THREE.OrthographicCamera(0, MAP_SIZE, MAP_SIZE, 0, 0.1, 10);
camera.position.z = 1;

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(MAP_SIZE, MAP_SIZE);
renderer.setPixelRatio(window.devicePixelRatio);
container.appendChild(renderer.domElement);

createMap(scene);

renderer.render(scene, camera);

function clearScene(s) {
  while (s.children.length > 0) {
    const obj = s.children[0];
    if (obj.geometry) obj.geometry.dispose();
    if (obj.material) obj.material.dispose();
    s.remove(obj);
  }
}

// Export map as binary
function exportMap() {
  // Render index pass to an offscreen target at 1:1 pixel ratio
  const indexScene = new THREE.Scene();
  const indexRenderer = new THREE.WebGLRenderer({ antialias: false });
  indexRenderer.setSize(MAP_SIZE, MAP_SIZE);
  indexRenderer.setPixelRatio(1);

  createMap(indexScene, { indexMode: true });

  const rt = new THREE.WebGLRenderTarget(MAP_SIZE, MAP_SIZE);
  indexRenderer.setRenderTarget(rt);
  indexRenderer.render(indexScene, camera);

  const pixels = new Uint8Array(MAP_SIZE * MAP_SIZE * 4);
  indexRenderer.readRenderTargetPixels(rt, 0, 0, MAP_SIZE, MAP_SIZE, pixels);

  // Extract terrain index from red channel of each pixel
  // WebGL reads bottom-to-top, so we flip vertically
  const terrainData = new Uint8Array(MAP_SIZE * MAP_SIZE);
  for (let y = 0; y < MAP_SIZE; y++) {
    for (let x = 0; x < MAP_SIZE; x++) {
      const srcRow = MAP_SIZE - 1 - y; // flip
      const srcIdx = (srcRow * MAP_SIZE + x) * 4;
      const redValue = pixels[srcIdx]; // red channel holds terrain index
      terrainData[y * MAP_SIZE + x] = redValue;
    }
  }

  // Build binary: magic "DMAP" + uint32 width + uint32 height + terrain bytes
  const headerSize = 4 + 4 + 4;
  const buffer = new ArrayBuffer(headerSize + terrainData.length);
  const view = new DataView(buffer);

  // Magic bytes "DMAP"
  view.setUint8(0, 0x44); // D
  view.setUint8(1, 0x4D); // M
  view.setUint8(2, 0x41); // A
  view.setUint8(3, 0x50); // P

  // Dimensions (little-endian)
  view.setUint32(4, MAP_SIZE, true);
  view.setUint32(8, MAP_SIZE, true);

  // Terrain data
  const bodyView = new Uint8Array(buffer, headerSize);
  bodyView.set(terrainData);

  // Download
  const blob = new Blob([buffer], { type: 'application/octet-stream' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'map.dmap';
  a.click();
  URL.revokeObjectURL(url);

  // Cleanup
  rt.dispose();
  clearScene(indexScene);
  indexRenderer.dispose();
}

document.getElementById('export-btn').addEventListener('click', exportMap);

// HMR: re-render when map module changes
if (import.meta.hot) {
  import.meta.hot.accept('./map.js', (newModule) => {
    clearScene(scene);
    newModule.createMap(scene);
    renderer.render(scene, camera);
  });
}
