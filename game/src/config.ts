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
  // Dynamic-zoom camera: zoom is driven by how close the boat is to a city.
  dynamicZoom: {
    nearDistance: 60,   // world units from a city centre: at or under this, nearZoom
    farDistance: 400,   // at or beyond this from every city, farZoom
    nearZoom: 7,        // screen px per world unit
    farZoom: 2,
    smoothing: 0.7,     // easing time constant in seconds (bigger = laggier)
    slowFactor: 0.3,    // boat max-speed multiplier at nearZoom (1 at farZoom)
  },
  cities: {
    // Label font size (screen px) and color per city size.
    // minZoom is screen px per world unit, below which the label is hidden so
    // the zoomed-out map doesn't drown in names. Whole-world-fits is ~0.07
    // px/u on a normal window; large cities are labelled at every zoom.
    label: {
      small: { fontPx: 11, weight: '400', color: '#e6e6e6', minZoom: 0.35 },
      medium: { fontPx: 14, weight: '600', color: '#f5f5f5', minZoom: 0.15 },
      large: { fontPx: 20, weight: '800', color: '#ffd166', minZoom: 0 },
    },
    hoverColor: '#7fe3ff',
    labelOffsetPx: 6,   // gap between the city centre and the label baseline
    hitRadiusPx: 26,    // forgiving click radius around the city centre
    hitPadPx: 10,       // ...plus this much slack around the label text itself
  },
  minimap: {
    sizePx: 240,
    margin: 12,
    rectColor: '#ffffff',
    // Fraction of the world's width/height the minimap window shows, centred
    // on the camera. 0.5 => a quarter of the map by area; 1 => the whole map.
    worldFraction: 0.5,
  },
  // Indexed by terrain value: ocean, beach, grassland, forest, mountain, river, canal, city
  terrainColors: [0x1a6dd4, 0xdec882, 0x4a9e4a, 0x266626, 0x888888, 0x1a6dd4, 0x135ba1, 0xcc2222],
};
