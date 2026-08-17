// World units per map cell — change this to resize the whole world.
export const TILE_SCALE = 20;

export const CONFIG = {
  mapUrl: '/map.dmap',
  maxZoom: 20,                 // screen px per world unit
  zoomWheelSensitivity: 0.0015,
  keyPanPxPerSec: 600,         // keyboard pan speed in screen px/sec (constant feel at any zoom)
  clearColor: 0x111111,
  // 3D camera rigs, cycled with V. zoom is still screen px per world unit,
  // measured at the focus point. pitch = angle between the view direction and
  // the ground (90 = top-down); yaw = rotation of the view about the vertical
  // (0 = screen-up is world north/-y, positive turns clockwise on the map).
  camera3d: {
    defaultRig: 'isometric',
    rigCycle: ['isometric', 'perspective'],
    rigs: {
      // Classic isometric: orthographic, looking down the cube diagonal.
      isometric: { projection: 'orthographic', pitchDeg: 35.264, yawDeg: 45, fovDeg: 0 },
      perspective: { projection: 'perspective', pitchDeg: 45, yawDeg: 0, fovDeg: 45 },
    } as Record<string, { projection: 'orthographic' | 'perspective'; pitchDeg: number; yawDeg: number; fovDeg: number }>,
    orthoDistance: 30000,     // how far back the orthographic camera sits (only affects clipping)
  },
  // Column heights per terrain index (world units): water is the ground at 0,
  // land rises out of it, cities rise 3x again.
  // ocean, beach, grassland, forest, mountain, river, canal, city
  terrainHeights: [0, 3, 8, 10, 14, 0, 0, 24],
  // Terrain is streamed as chunk meshes around the camera; far out, chunks
  // switch to coarser cells (LOD) so the whole world stays cheap.
  chunks: {
    cellsPerChunk: 32,        // chunk edge in (LOD) cells
    maxVisibleChunks: 110,    // pick the coarsest-necessary LOD to stay under this
    maxCachedChunks: 360,     // built chunks kept around before evicting the oldest
    buildBudgetMs: 5,         // per-frame CPU budget for building chunk meshes
    maxLodStep: 16,           // coarsest LOD: this many map cells per column
    sideShadeX: 0.72,         // brightness of x-facing column sides
    sideShadeY: 0.55,         // ...and y-facing sides
    boundsPadCells: 2,        // extra cells loaded around the visible area
    maxViewDistance: 40000,   // world units: cap on how far the top of the screen looks
  },
  grid: {
    fadeMinPxPerTile: 8,       // grid hidden below this many screen px per tile
    fadeMaxPxPerTile: 24,      // grid fully visible above this
    maxAlpha: 0.35,
    color: 0x444444,
  },
  boat: {
    startX: 11301,
    startY: 18085,
    startHeading: -Math.PI / 2, // pointing "up" the screen (world -y)
    length: 10,                 // world units along the heading
    width: 4,
    height: 3,                  // box height; sits on the water at z=0
    color: 0x7a4a1e,
    maxSpeed: 50,               // world units/sec
    maxReverseSpeed: 20,
    accel: 40,                  // world units/sec^2
    friction: 45,               // deceleration with no throttle, units/sec^2
    turnRate: Math.PI * 0.75,   // rad/sec at or above turnFullSpeed
    turnFullSpeed: 20,          // speed at which steering is at full authority
    followZoom: 2.5,             // screen px per world unit when follow cam starts
    maxStepSeconds: 0.05,       // physics sub-step cap when time is fast-forwarded
  },
  cities: {
    // Label font size (screen px) and color per city size.
    // minZoom is screen px per world unit, below which the label is hidden so
    // the zoomed-out map doesn't drown in names. Whole-world-fits is ~0.07
    // px/u on a normal window; large cities are labelled at every zoom.
    label: {
      small: { fontPx: 11, weight: '400', color: '#e6e6e6', minZoom: 0.22 },
      medium: { fontPx: 14, weight: '600', color: '#f5f5f5', minZoom: 0.15 },
      large: { fontPx: 20, weight: '800', color: '#ffd166', minZoom: 0 },
    },
    hoverColor: '#7fe3ff',
    labelOffsetPx: 6,   // gap between the city centre and the label baseline
    hitRadiusPx: 26,    // forgiving click radius around the city centre
    hitPadPx: 10,       // ...plus this much slack around the label text itself
  },
  // Prices are all derived from products.json basePrice — see src/market.ts.
  market: {
    // Multiplier on a product's base price, by city size. The result is the
    // price the city PAYS the player (rounded up); buying costs buyMarkup more.
    sizeMultiplier: { small: 1, medium: 2, large: 3 },
    buyMarkup: 1.2,
    // A city's priceOverrides multiplier tints its market row: dearer than
    // normal reads red, cheaper reads green.
    dearColor: '#ff6b6b',
    cheapColor: '#6bd97a',
    normalColor: '#ffffff',
  },
  player: {
    startingMoney: 500,
    // What's in the hold at the start — enough rice for a few days' sailing.
    startingInventory: { RICE: 20 } as Record<string, number>,
  },
  // Game time only moves when the player acts — see src/gameTime.ts.
  gameTime: {
    startMonth: 1,
    startDay: 1,
    startHour: 8,
    startMinute: 0,
    hoursPerSecondSailing: 1,  // per real second with the boat under way
    minutesPerTransaction: 10, // per market button press
    // Sailing time runs faster when zoomed out, since the same screen covers
    // more world. Applies to travel only, never to market transactions.
    zoomTimeScale: {
      nearZoom: 3.5,   // screen px per world unit: at or above, normal speed
      farZoom: 0.37,   // at or below, farRate
      nearRate: 1,
      farRate: 2.5,
    },
    // The clock reads dimmer between these hours (wraps past midnight).
    nightStartHour: 20,
    nightEndHour: 5,
    dayColor: '#f2e9d8',
    nightColor: '#4a5568',
    fontPx: 20,
  },
  // Crew size: hired in large cities, drives upkeep and the boat's top speed.
  crew: {
    starting: 1,
    min: 1,
    max: 20,
    speedAtMin: 1,       // boat.maxSpeed multiplier with a minimum crew
    speedAtMax: 2,       // ...and at speedFullCrew; linear in between
    speedFullCrew: 10,   // speed tops out here; more hands only carry cargo
    cargoPerCrew: 100,   // hold space each crew member brings
  },
  // Keeping a crew costs rice and coin as game time passes (src/upkeep.ts).
  // Both charges are per crew member.
  rations: {
    productId: 'RICE',
    hoursPerUnit: 8,     // game hours per unit eaten, per crew member
    lowUnits: 10,        // below this the readout turns red
    lowColor: '#ff6b6b',
  },
  wages: {
    moneyPerDay: 1,      // per crew member, out of the purse every game day
  },
  // The city panel: centred, sized as a fraction of the window but never
  // narrower/shorter than the min, nor bigger than the max.
  cityPopup: {
    widthVw: 62,
    minWidthPx: 520,
    maxWidthPx: 900,
    maxHeightVh: 80,
    fontPx: 16,        // market rows
    titleFontPx: 28,
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
