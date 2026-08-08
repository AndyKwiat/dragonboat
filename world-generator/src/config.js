// All tunable map parameters in one place.
// Change values here — the map updates via HMR.

export const MAP_CONFIG = {
  size: 1000,

  island: {
    radius: 430,
    seed: 1.0,
    segments: 256,
    beachWidth: 2,
  },

  mountains: {
    radius: 120,
    seed: 7.5,
  },

  rivers: [
    { angle: 0.4,  maxWidth: 18, curveSeed: 1.2, tributaries: [
      { joinT: 0.4, side: 1,  length: 80, seed: 12.2 },
      { joinT: 0.6, side: -1, length: 100, seed: 24.2 },
    ]},
    { angle: 1.3,  maxWidth: 8,  curveSeed: 3.7 },
    { angle: 2.1,  maxWidth: 14, curveSeed: 5.1, tributaries: [
      { joinT: 0.4, side: 1,  length: 80, seed: 16.1 },
      { joinT: 0.6, side: -1, length: 100, seed: 28.1 },
    ]},
    { angle: 3.0,  maxWidth: 4,  curveSeed: 2.4 },
    { angle: 3.9,  maxWidth: 20, curveSeed: 8.3, tributaries: [
      { joinT: 0.4, side: 1,  length: 80, seed: 19.3 },
      { joinT: 0.6, side: -1, length: 100, seed: 31.3 },
    ]},
    { angle: 5.0,  maxWidth: 6,  curveSeed: 6.6 },
    { angle: 5.8,  maxWidth: 12, curveSeed: 4.0 },
  ],

  lakes: [
    { riverIndex: 1, position: 0.5, radius: 30, seed: 19.4 },
    { riverIndex: 4, position: 0.4, radius: 25, seed: 42.7 },
  ],

  canals: [
    { riverA: 0, tA: 0.55, riverB: 6, tB: 0.5,  width: 2 },
    { riverA: 2, tA: 0.6,  riverB: 3, tB: 0.5,  width: 2 },
    { riverA: 4, tA: 0.45, riverB: 5, tB: 0.55, width: 2 },
  ],

  cities: {
    count: 100,
    size: 5,
    minSpacing: 12,
    riverOffset: 3,       // distance from river edge
    canalCandidates: 3,   // sample points per canal
  },

  colors: {
    ocean:     0x1a6dd4,
    beach:     0xdec882,
    grassland: [0.29, 0.62, 0.29],
    forest:    [0.15, 0.40, 0.15],
    mountain:  0x888888,
    canal:     0x135ba1,
    city:      0xcc2222,
  },
};
