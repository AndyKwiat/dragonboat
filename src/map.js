import * as THREE from 'three';

// Simple smooth noise: sum a few sine waves at different frequencies
function noisyRadius(baseRadius, angle, seed) {
  const variation = baseRadius * 0.15;
  return baseRadius + variation * (
    0.5  * Math.sin(angle * 3 + seed) +
    0.3  * Math.sin(angle * 5 + seed * 2.3) +
    0.2  * Math.sin(angle * 7 + seed * 4.1)
  );
}

function makeIrregularShape(baseRadius, segments, seed) {
  const shape = new THREE.Shape();
  for (let i = 0; i <= segments; i++) {
    const angle = (i / segments) * Math.PI * 2;
    const r = noisyRadius(baseRadius, angle, seed);
    const x = Math.cos(angle) * r;
    const y = Math.sin(angle) * r;
    if (i === 0) shape.moveTo(x, y);
    else shape.lineTo(x, y);
  }
  return shape;
}

export function createMap(scene) {
  // Blue ocean background
  const bgGeometry = new THREE.PlaneGeometry(1000, 1000);
  const bgMaterial = new THREE.MeshBasicMaterial({ color: 0x1a6dd4 });
  const bg = new THREE.Mesh(bgGeometry, bgMaterial);
  bg.position.set(500, 500, 0);
  scene.add(bg);

  const baseRadius = 350;
  const segments = 256;
  const seed = 1.0;

  // Island fill
  const islandShape = makeIrregularShape(baseRadius, segments, seed);
  const islandGeometry = new THREE.ShapeGeometry(islandShape);
  const islandMaterial = new THREE.MeshBasicMaterial({ color: 0x4a9e4a });
  const island = new THREE.Mesh(islandGeometry, islandMaterial);
  island.position.set(500, 500, 0.1);
  scene.add(island);

  // Mountain blob in the center
  const mountainRadius = 120;
  const mountainSeed = 7.5;
  const mountainShape = makeIrregularShape(mountainRadius, segments, mountainSeed);
  const mountainGeometry = new THREE.ShapeGeometry(mountainShape);
  const mountainMaterial = new THREE.MeshBasicMaterial({ color: 0x888888 });
  const mountain = new THREE.Mesh(mountainGeometry, mountainMaterial);
  mountain.position.set(500, 500, 0.2);
  scene.add(mountain);

  // Rivers flowing from mountains to the sea
  const oceanColor = 0x1a6dd4;
  const rivers = [
    { angle: 0.4,  maxWidth: 18, curveSeed: 1.2, tributaries: true },
    { angle: 1.3,  maxWidth: 8,  curveSeed: 3.7 },
    { angle: 2.1,  maxWidth: 14, curveSeed: 5.1, tributaries: true },
    { angle: 3.0,  maxWidth: 4,  curveSeed: 2.4 },
    { angle: 3.9,  maxWidth: 20, curveSeed: 8.3, tributaries: true },
    { angle: 5.0,  maxWidth: 6,  curveSeed: 6.6 },
    { angle: 5.8,  maxWidth: 12, curveSeed: 4.0 },
  ];

  const riverMaterial = new THREE.MeshBasicMaterial({ color: oceanColor });

  function buildRiverPoints(startR, endR, angle, maxWidth, curveSeed, steps, widthGrows) {
    const points = [];
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      const r = startR + (endR - startR) * t;
      // More aggressive meandering
      const wobble = Math.sin(t * 12 + curveSeed) * 25 +
                     Math.sin(t * 18 + curveSeed * 2.3) * 15 +
                     Math.sin(t * 7 + curveSeed * 0.7) * 20;
      const x = Math.cos(angle) * r - Math.sin(angle) * wobble;
      const y = Math.sin(angle) * r + Math.cos(angle) * wobble;
      const width = widthGrows
        ? Math.max(2, maxWidth * t)
        : Math.max(2, maxWidth * (1 - t * 0.5));
      points.push({ x, y, width });
    }
    return points;
  }

  function renderRiverMesh(points, zOffset) {
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
    const mesh = new THREE.Mesh(geo, riverMaterial);
    mesh.position.set(500, 500, zOffset);
    scene.add(mesh);
    return mesh;
  }

  // Build all river points first so canals can reference them
  const allRiverPoints = [];

  for (const river of rivers) {
    const steps = 100;
    const startR = noisyRadius(mountainRadius, river.angle, mountainSeed);
    const endR = noisyRadius(baseRadius, river.angle, seed) + 5;

    const points = buildRiverPoints(startR, endR, river.angle, river.maxWidth, river.curveSeed, steps, true);
    allRiverPoints.push(points);
    renderRiverMesh(points, 0.15);

    // Add a lake on the second river (index 1, angle 1.3)
    if (river.angle === 1.3) {
      const lakeIdx = Math.floor(0.5 * points.length);
      const lakeCenter = points[lakeIdx];
      const lakeRadius = 30;
      const lakeSeed = 19.4;
      const lakeShape = makeIrregularShape(lakeRadius, 128, lakeSeed);
      const lakeGeo = new THREE.ShapeGeometry(lakeShape);
      const lakeMesh = new THREE.Mesh(lakeGeo, riverMaterial);
      lakeMesh.position.set(500 + lakeCenter.x, 500 + lakeCenter.y, 0.15);
      scene.add(lakeMesh);
    }

    // Tributaries for the 3 biggest rivers
    if (river.tributaries) {
      const tribConfigs = [
        { joinT: 0.4, side: 1,  length: 80, seed: river.curveSeed + 11 },
        { joinT: 0.6, side: -1, length: 100, seed: river.curveSeed + 23 },
      ];
      for (const trib of tribConfigs) {
        const joinIdx = Math.floor(trib.joinT * points.length);
        const joinPoint = points[joinIdx];
        const prevP = points[Math.max(0, joinIdx - 1)];
        const nextP = points[Math.min(points.length - 1, joinIdx + 1)];
        const rdx = nextP.x - prevP.x;
        const rdy = nextP.y - prevP.y;
        const branchAngle = Math.atan2(rdy, rdx) + trib.side * 1.1;
        const bx = Math.cos(branchAngle);
        const by = Math.sin(branchAngle);

        const tribSteps = 40;
        const tribPoints = [];
        for (let i = 0; i <= tribSteps; i++) {
          const t = i / tribSteps;
          const dist = t * trib.length;
          const wobble = Math.sin(t * 8 + trib.seed) * 10 +
                         Math.sin(t * 13 + trib.seed * 1.7) * 6;
          const px = -by;
          const py = bx;
          const x = joinPoint.x + bx * dist + px * wobble;
          const y = joinPoint.y + by * dist + py * wobble;
          const width = Math.max(2, joinPoint.width * 0.6 * (1 - t * 0.7));
          tribPoints.push({ x, y, width });
        }
        renderRiverMesh(tribPoints, 0.15);
      }
    }
  }

  // Canals - straight waterways connecting bigger rivers (darker blue)
  const canalColor = 0x135ba1;
  const canalMaterial = new THREE.MeshBasicMaterial({ color: canalColor });
  const canals = [
    { riverA: 0, tA: 0.55, riverB: 1, tB: 0.5,  width: 2 },  // river 0 <-> river 1
    { riverA: 2, tA: 0.6,  riverB: 3, tB: 0.55, width: 2 },  // river 2 <-> river 3
    { riverA: 4, tA: 0.5,  riverB: 5, tB: 0.5,  width: 2 },  // river 4 <-> river 5
  ];

  for (const canal of canals) {
    const pA = allRiverPoints[canal.riverA][Math.floor(canal.tA * allRiverPoints[canal.riverA].length)];
    const pB = allRiverPoints[canal.riverB][Math.floor(canal.tB * allRiverPoints[canal.riverB].length)];
    const halfW = canal.width / 2;

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
    const indices = [0, 1, 2, 1, 3, 2];
    const canalGeo = new THREE.BufferGeometry();
    canalGeo.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
    canalGeo.setIndex(indices);
    const canalMesh = new THREE.Mesh(canalGeo, canalMaterial);
    canalMesh.position.set(500, 500, 0.16);
    scene.add(canalMesh);
  }

  // Sandy beach border (drawn as a slightly larger shape behind the island)
  const beachShape = makeIrregularShape(baseRadius + 2, segments, seed);
  const beachGeometry = new THREE.ShapeGeometry(beachShape);
  const beachMaterial = new THREE.MeshBasicMaterial({ color: 0xdec882 });
  const beach = new THREE.Mesh(beachGeometry, beachMaterial);
  beach.position.set(500, 500, 0.05);
  scene.add(beach);
}
