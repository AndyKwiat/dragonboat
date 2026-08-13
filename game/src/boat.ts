import * as THREE from 'three';
import { CONFIG } from './config';
import type { InputState } from './input';

// The player boat: a brown rectangle that drives around the world. World
// coords are y-down, so a heading of 0 points +x (right) and increasing
// heading turns clockwise on screen — the same sense as the mesh's
// rotation.z, since the mesh lives in the same y-down frame.
export interface Boat {
  mesh: THREE.Mesh;
  x: number;
  y: number;
  heading: number; // radians
  speed: number;   // world units/sec, signed (negative = reversing)
  /** Multiplier on max speed, set by the active camera mode (1 = normal). */
  maxSpeedScale: number;
  update(dt: number, input: InputState): void;
  syncMesh(): void;
}

export function createBoat(): Boat {
  const c = CONFIG.boat;
  const geometry = new THREE.PlaneGeometry(c.length, c.width);
  const material = new THREE.MeshBasicMaterial({ color: c.color, side: THREE.DoubleSide });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.z = 1; // above the terrain plane at z=0

  const boat: Boat = {
    mesh,
    x: c.startX,
    y: c.startY,
    heading: c.startHeading,
    speed: 0,
    maxSpeedScale: 1,

    update(dt, input) {
      const forward = input.keys.has('KeyW') || input.keys.has('ArrowUp');
      const back = input.keys.has('KeyS') || input.keys.has('ArrowDown');
      const left = input.keys.has('KeyA') || input.keys.has('ArrowLeft');
      const right = input.keys.has('KeyD') || input.keys.has('ArrowRight');

      // Accelerate / brake, then apply friction when no throttle input.
      if (forward) boat.speed += c.accel * dt;
      else if (back) boat.speed -= c.accel * dt;
      else {
        const drop = c.friction * dt;
        boat.speed = Math.abs(boat.speed) <= drop ? 0 : boat.speed - Math.sign(boat.speed) * drop;
      }
      const scale = boat.maxSpeedScale;
      boat.speed = Math.min(Math.max(boat.speed, -c.maxReverseSpeed * scale), c.maxSpeed * scale);

      // Steering authority scales with how fast we're moving: a dead boat
      // doesn't spin in place.
      const turn = (right ? 1 : 0) - (left ? 1 : 0);
      if (turn !== 0 && boat.speed !== 0) {
        const authority = Math.min(Math.abs(boat.speed) / c.turnFullSpeed, 1);
        boat.heading += turn * c.turnRate * authority * Math.sign(boat.speed) * dt;
      }

      boat.x += Math.cos(boat.heading) * boat.speed * dt;
      boat.y += Math.sin(boat.heading) * boat.speed * dt;
      boat.syncMesh();
    },

    syncMesh() {
      mesh.position.x = boat.x;
      mesh.position.y = boat.y;
      mesh.rotation.z = boat.heading;
    },
  };

  boat.syncMesh();
  return boat;
}
