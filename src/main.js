import * as THREE from 'three';
import { createMap } from './map.js';

const container = document.getElementById('map-container');

const scene = new THREE.Scene();

const camera = new THREE.OrthographicCamera(0, 1000, 1000, 0, 0.1, 10);
camera.position.z = 1;

const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(1000, 1000);
renderer.setPixelRatio(window.devicePixelRatio);
container.appendChild(renderer.domElement);

createMap(scene);

renderer.render(scene, camera);

// HMR: re-render when map module changes
if (import.meta.hot) {
  import.meta.hot.accept('./map.js', (newModule) => {
    // Clear the scene
    while (scene.children.length > 0) {
      const obj = scene.children[0];
      if (obj.geometry) obj.geometry.dispose();
      if (obj.material) obj.material.dispose();
      scene.remove(obj);
    }
    newModule.createMap(scene);
    renderer.render(scene, camera);
  });
}
