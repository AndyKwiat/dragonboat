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

function clearScene(s) {
  while (s.children.length > 0) {
    const obj = s.children[0];
    if (obj.geometry) obj.geometry.dispose();
    if (obj.material) obj.material.dispose();
    s.remove(obj);
  }
}

function render() {
  clearScene(scene);
  createMap(scene);
  renderer.render(scene, camera);
}

render();

// Export map as binary .dmap
function exportMap() {
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

  // Extract terrain index from red channel, flip vertically
  const terrainData = new Uint8Array(MAP_SIZE * MAP_SIZE);
  for (let y = 0; y < MAP_SIZE; y++) {
    for (let x = 0; x < MAP_SIZE; x++) {
      const srcRow = MAP_SIZE - 1 - y;
      const srcIdx = (srcRow * MAP_SIZE + x) * 4;
      terrainData[y * MAP_SIZE + x] = pixels[srcIdx];
    }
  }

  // Build binary: magic "DMAP" + uint32 width + uint32 height + terrain bytes
  const headerSize = 12;
  const buffer = new ArrayBuffer(headerSize + terrainData.length);
  const view = new DataView(buffer);
  view.setUint8(0, 0x44); // D
  view.setUint8(1, 0x4D); // M
  view.setUint8(2, 0x41); // A
  view.setUint8(3, 0x50); // P
  view.setUint32(4, MAP_SIZE, true);
  view.setUint32(8, MAP_SIZE, true);
  new Uint8Array(buffer, headerSize).set(terrainData);

  // Download
  const blob = new Blob([buffer], { type: 'application/octet-stream' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = 'map.dmap';
  a.click();
  URL.revokeObjectURL(url);

  rt.dispose();
  clearScene(indexScene);
  indexRenderer.dispose();
}

document.getElementById('export-btn').addEventListener('click', exportMap);

// HMR: re-render when any map-related module changes
if (import.meta.hot) {
  import.meta.hot.accept(['./map.js', './config.js', './geometry.js'], () => {
    render();
  });
}
