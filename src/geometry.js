// Pure data generation — no THREE.js imports.
// Produces arrays of points and shapes that the renderer consumes.

export function noisyRadius(baseRadius, angle, seed) {
  const variation = baseRadius * 0.15;
  return baseRadius + variation * (
    0.5  * Math.sin(angle * 3 + seed) +
    0.3  * Math.sin(angle * 5 + seed * 2.3) +
    0.2  * Math.sin(angle * 7 + seed * 4.1)
  );
}

// Returns array of {x, y} points forming an irregular closed shape
export function generateIrregularOutline(baseRadius, segments, seed) {
  const points = [];
  for (let i = 0; i <= segments; i++) {
    const angle = (i / segments) * Math.PI * 2;
    const r = noisyRadius(baseRadius, angle, seed);
    points.push({ x: Math.cos(angle) * r, y: Math.sin(angle) * r });
  }
  return points;
}

// Returns array of {x, y, width} points along a meandering river
export function generateRiverPoints(startR, endR, angle, maxWidth, curveSeed, steps) {
  const points = [];
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const r = startR + (endR - startR) * t;
    const wobble = Math.sin(t * 12 + curveSeed) * 25 +
                   Math.sin(t * 18 + curveSeed * 2.3) * 15 +
                   Math.sin(t * 7 + curveSeed * 0.7) * 20;
    points.push({
      x: Math.cos(angle) * r - Math.sin(angle) * wobble,
      y: Math.sin(angle) * r + Math.cos(angle) * wobble,
      width: Math.max(2, maxWidth * t),
    });
  }
  return points;
}

// Returns array of {x, y, width} points for a tributary branching off a river
export function generateTributaryPoints(riverPoints, trib) {
  const joinIdx = Math.floor(trib.joinT * riverPoints.length);
  const joinPoint = riverPoints[joinIdx];
  const prevP = riverPoints[Math.max(0, joinIdx - 1)];
  const nextP = riverPoints[Math.min(riverPoints.length - 1, joinIdx + 1)];
  const rdx = nextP.x - prevP.x;
  const rdy = nextP.y - prevP.y;
  const branchAngle = Math.atan2(rdy, rdx) + trib.side * 1.1;
  const bx = Math.cos(branchAngle);
  const by = Math.sin(branchAngle);

  const steps = 40;
  const points = [];
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const dist = t * trib.length;
    const wobble = Math.sin(t * 8 + trib.seed) * 10 +
                   Math.sin(t * 13 + trib.seed * 1.7) * 6;
    points.push({
      x: joinPoint.x + bx * dist + (-by) * wobble,
      y: joinPoint.y + by * dist + bx * wobble,
      width: Math.max(2, joinPoint.width * 0.6 * (1 - t * 0.7)),
    });
  }
  return points;
}

// Returns {pA, pB} endpoints for a canal connecting two rivers
export function getCanalEndpoints(mainRiverPoints, canal) {
  const pA = mainRiverPoints[canal.riverA][Math.floor(canal.tA * mainRiverPoints[canal.riverA].length)];
  const pB = mainRiverPoints[canal.riverB][Math.floor(canal.tB * mainRiverPoints[canal.riverB].length)];
  return { pA, pB };
}

// Binary-split interleave: picks middle first, then quarters, etc.
function interleave(arr) {
  if (arr.length <= 1) return arr;
  const result = [];
  const queue = [[0, arr.length - 1]];
  while (queue.length > 0) {
    const [lo, hi] = queue.shift();
    if (lo > hi) continue;
    const mid = Math.floor((lo + hi) / 2);
    result.push(arr[mid]);
    if (lo < mid) queue.push([lo, mid - 1]);
    if (mid < hi) queue.push([mid + 1, hi]);
  }
  return result;
}

// Build candidate city positions along a waterway's points
function buildWaterwayCandidates(points, offset) {
  const candidates = [];
  const start = Math.floor(points.length * 0.1);
  const end = Math.floor(points.length * 0.95);
  for (let i = start; i < end; i++) {
    const p = points[i];
    const next = points[Math.min(i + 1, points.length - 1)];
    const prev = points[Math.max(i - 1, 0)];
    const dx = next.x - prev.x;
    const dy = next.y - prev.y;
    const len = Math.sqrt(dx * dx + dy * dy);
    if (len === 0) continue;
    const nx = -dy / len;
    const ny = dx / len;
    const side = ((i * 7 + Math.floor(p.x * 13)) % 2 === 0) ? 1 : -1;
    candidates.push({
      x: p.x + nx * side * (p.width / 2 + offset),
      y: p.y + ny * side * (p.width / 2 + offset),
    });
  }
  return candidates;
}

// Build candidate city positions along a canal
function buildCanalCandidates(pA, pB, canalWidth, offset, sampleCount) {
  const dx = pB.x - pA.x;
  const dy = pB.y - pA.y;
  const len = Math.sqrt(dx * dx + dy * dy);
  const nx = -dy / len;
  const ny = dx / len;
  const candidates = [];
  for (let i = 0; i <= sampleCount; i++) {
    const t = i / sampleCount;
    candidates.push({
      x: pA.x + dx * t + nx * (canalWidth / 2 + offset),
      y: pA.y + dy * t + ny * (canalWidth / 2 + offset),
    });
  }
  return candidates;
}

// Place cities using round-robin across all waterways
export function generateCityPositions(allWaterwayPoints, canalEndpoints, config) {
  const { count, minSpacing, riverOffset, canalCandidates } = config;

  // Build candidates per waterway
  const candidatesPerWaterway = allWaterwayPoints.map(
    pts => buildWaterwayCandidates(pts, riverOffset)
  );

  // Add canal candidates
  for (const { pA, pB, width } of canalEndpoints) {
    candidatesPerWaterway.push(
      buildCanalCandidates(pA, pB, width, riverOffset, canalCandidates)
    );
  }

  // Interleave each waterway's candidates for even spatial distribution
  const interleaved = candidatesPerWaterway.map(c => interleave(c));

  // Round-robin placement
  const cities = [];
  const minDistSq = minSpacing * minSpacing;
  const cursors = interleaved.map(() => 0);
  let placed = true;
  while (cities.length < count && placed) {
    placed = false;
    for (let ri = 0; ri < interleaved.length && cities.length < count; ri++) {
      const candidates = interleaved[ri];
      while (cursors[ri] < candidates.length) {
        const cp = candidates[cursors[ri]];
        cursors[ri]++;
        const tooClose = cities.some(c => {
          const ddx = c.x - cp.x;
          const ddy = c.y - cp.y;
          return ddx * ddx + ddy * ddy < minDistSq;
        });
        if (tooClose) continue;
        cities.push(cp);
        placed = true;
        break;
      }
    }
  }
  return cities;
}

// Generate all map data from config. Returns a plain object with all geometry.
export function generateMapData(config) {
  const { island, mountains, rivers, lakes, canals, cities } = config;

  // Island + beach outlines
  const islandOutline = generateIrregularOutline(island.radius, island.segments, island.seed);
  const beachOutline = generateIrregularOutline(island.radius + island.beachWidth, island.segments, island.seed);

  // Mountain outline
  const mountainOutline = generateIrregularOutline(mountains.radius, island.segments, mountains.seed);

  // Rivers (main + tributaries tracked separately)
  const mainRiverPoints = [];
  const allWaterwayPoints = [];  // rivers + tributaries, for city placement

  for (const river of rivers) {
    const startR = noisyRadius(mountains.radius, river.angle, mountains.seed);
    const endR = noisyRadius(island.radius, river.angle, island.seed) + 5;
    const points = generateRiverPoints(startR, endR, river.angle, river.maxWidth, river.curveSeed, 100);
    mainRiverPoints.push(points);
    allWaterwayPoints.push(points);

    if (river.tributaries) {
      for (const trib of river.tributaries) {
        const tribPoints = generateTributaryPoints(points, trib);
        allWaterwayPoints.push(tribPoints);
      }
    }
  }

  // Lakes
  const lakeData = lakes.map(lake => {
    const riverPts = mainRiverPoints[lake.riverIndex];
    const idx = Math.floor(lake.position * riverPts.length);
    const center = riverPts[idx];
    const outline = generateIrregularOutline(lake.radius, 128, lake.seed);
    return { center, outline };
  });

  // Canals
  const canalData = canals.map(canal => {
    const { pA, pB } = getCanalEndpoints(mainRiverPoints, canal);
    return { pA, pB, width: canal.width };
  });

  // Cities
  const cityPositions = generateCityPositions(
    allWaterwayPoints,
    canalData,
    cities,
  );

  return {
    islandOutline,
    beachOutline,
    mountainOutline,
    mainRiverPoints,
    allWaterwayPoints,
    lakeData,
    canalData,
    cityPositions,
    island,
    mountains,
    cities,
  };
}
