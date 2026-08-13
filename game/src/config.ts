// World units per map cell — change this to resize the whole world.
export const TILE_SCALE = 20;

export const CONFIG = {
  mapUrl: '/map.dmap',
  maxZoom: 20,                 // screen px per world unit
  zoomWheelSensitivity: 0.0015,
  keyPanPxPerSec: 600,         // keyboard pan speed in screen px/sec (constant feel at any zoom)
  clearColor: 0x111111,
  grid: {
    fadeMinPxPerTile: 8,       // grid hidden below this many screen px per tile
    fadeMaxPxPerTile: 24,      // grid fully visible above this
    maxAlpha: 0.35,
    color: 0x444444,
  },
  boat: {
    startX: 12445,
    startY: 18727,
    startHeading: -Math.PI / 2, // pointing "up" the screen (world -y)
    length: 10,                 // world units along the heading
    width: 4,
    color: 0x7a4a1e,
    maxSpeed: 160,               // world units/sec
    maxReverseSpeed: 20,
    accel: 40,                  // world units/sec^2
    friction: 25,               // deceleration with no throttle, units/sec^2
    turnRate: Math.PI * 0.75,   // rad/sec at or above turnFullSpeed
    turnFullSpeed: 20,          // speed at which steering is at full authority
    followZoom: 5,              // screen px per world unit when follow cam starts
  },
  minimap: {
    sizePx: 240,
    margin: 12,
    rectColor: '#ffffff',
  },
  // Indexed by terrain value: ocean, beach, grassland, forest, mountain, river, canal, city
  terrainColors: [0x1a6dd4, 0xdec882, 0x4a9e4a, 0x266626, 0x888888, 0x1a6dd4, 0x135ba1, 0xcc2222],
};
