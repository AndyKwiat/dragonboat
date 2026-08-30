# World: map, coordinates, camera, terrain

Files: `src/core/TerrainMap.cs`, `src/core/GameConfig.cs`, `src/core/Config.cs`,
`src/world/*`,
`shaders/*`. Nothing here knows about trading or combat; both of those ask
the world questions through `Terrain` and `CameraRig`.

## The map

`game-config.yml` (repo root) is read first (`src/core/GameConfig.cs`, a flat
`key: value` parser). Every key is required — none has a code default:

```yml
terrainFile: map1.terrain   # which map to load
currentDensity: 100         # currents per map area (100 = 3600 on an 8192² map)
smallCities: 24             # how many cities of each size to generate —
mediumCities: 12            # the mix is the file's, there is no ratio in
largeCities: 4              # the code (CityPlacer.SizesFor)
priceEvents: 20             # story events held at a time, by kind — supply
vipEvents: 4                # shocks and VIP offers; 0 = none (trading.md)
```

The terrain file is painted in `../terrain-gen` and exported in its `TGN`
format (spec: `../terrain-gen/docs/FILE_FORMAT.md`). The 4-byte magic *is* the
version: `TGN1` is uint32 LE size (cells per side, a power of two), uint32
flags, float32 worldSize, then size² float32 heights and size² uint8 tile ids,
row-major top to bottom; `TGN2` is byte-identical up to there and appends a
marker section. The whole file is usually gzipped (`TerrainMap.Parse` gunzips
transparently) and both versions load. The heightmap is **ignored for now**;
only the tile ids are used.

**Markers** (`TGN2`) are labelled circles the painter drops on the map: a
centre and radius in cells — the same coordinate space as the tiles, so also
world units — and a free-text tag of 1…100 characters. They are not rasterized
into the tile grid; `TerrainMap.Markers` holds them in file order (later ones
paint on top) and `MarkerAt(x, y)` returns the topmost circle covering a
point, or null. A `TGN1` file loads with an empty marker list. The game
itself does not read markers; test scenarios spawn boats on them
([scenarios.md](scenarios.md)).

Tile ids: 0 water, 1 sand, 2 grass, 3 forest, 4 rock — plus 5 city, which is
not in the file: `CityPlacer` stamps it into the loaded map at launch (see
[trading.md](trading.md)). `TerrainMap.TerrainAt` returns water outside the map.

**One cell is one world unit (~1 m)**, so `map1.terrain` (8192²) is an
8192×8192 world — there is no tile scale any more. Distances in the config
(cannon range, current zones, city footprints) are all in cells.

**Water** is tile 0 only (`Terrain.IsWaterIndex`). Everything else is land
and rises out of the water by `Config.TerrainHeights[id]` — small steps at
this resolution (sand 0.3, grass 0.8, forest 1, rock 1.4, city 2.4).

## Coordinates and camera

World coordinates are the map's: (0,0) top-left, x right, **y down**, in
world units. In Godot space that is `X = x, Z = y, Y = height`
(`CameraRig.WorldToGodot`), so north (world -y) is -Z and no axis flipping is
needed anywhere: map rows go straight into the texture, `boat.Rotation.Y =
-heading` (Godot turns +X towards -Z, world headings turn +X towards +Z/south).

`zoom` is screen px per world unit at the camera's focus point. A **rig**
(`Config.Rigs`, cycled with V) is orthographic or perspective plus pitch/yaw;
`CameraRig.Apply` puts the camera behind the focus along the rig's ground
`Forward` looking down at the pitch, so screen-up is `Forward`. Orthographic
size = viewport height / zoom; perspective distance is derived from zoom and
the fov. A camera **mode** (`CameraControllers.cs`, cycled with C) decides
where the focus is: `follow` locks to the boat with a speed-scaled look-ahead
(driven by `Boat.GroundSpeed`, the boat's actual progress, so a hull held by
the shore stays centred), capped in screen px so the boat always keeps
`FollowLookAheadMarginPx` of screen at every edge — a full crew outruns
`MaxSpeed`, which used to push the boat clean off screen; `edge` holds the view still and lets the boat move
across the screen, panning only — and only just enough — when the boat would
enter the `EdgeCamMarginPx` band at a screen edge; `free` pans/drags/zooms
(the boat isn't drivable in free mode). The **combat camera** (in
`Main._Process`): firing a shot in follow mode with an enemy ship inside
cannon range auto-switches to `edge` — the better cam for a fight — and
drifting out of range (with 1.2x slack against flapping) switches back to
`follow`. Free cam is never touched, and a manual mode change (C) cancels
the auto-return. Free-camera pan/drag and
zoom anchoring go through `ScreenDeltaToGround` (yaw-rotated, vertical
foreshortened by sin(pitch)); labels use `Camera3D.UnprojectPosition`; the
chunk streamer casts the four screen corners onto the ground with
`ProjectRayOrigin/Normal`.

## Terrain queries (`world/Terrain.cs`)

A data-only wrapper over `TerrainMap` for gameplay:

- `IsWater(x, y)`, `TerrainAt`, `HeightAt(x, y)` — cell lookups at a world
  point (a cell is a unit square, so the cell is just the floor of the point).
- `ResolveCircle(ref x, ref y, r, out normal)` — pushes a circle out of every
  non-water cell it overlaps (closest-point-on-square vs circle, iterated for
  inside corners) and returns the total push. The boat's hull is three of
  these (see [boat.md](boat.md)); anything else that must stay on water can
  use it too.
- `RaycastGround(origin, dir, max, out hit)` — where a camera ray meets the
  water plane or the top of a raised column (step-and-bisect through the
  band of column heights). Aiming uses it so clicking on a hill targets the
  hilltop, not the water plane underneath.

## Currents

`world/Currents.cs` (a test feature): arrows scattered on water cells, **all
flowing south** (world +y). Each owns a `ZoneLength` × `ZoneWidth` rectangle
ahead of it that pushes anything inside along the flow at `Strength`, fading
at the edges; overlapping zones add up.

How many arrows is `game-config.yml`'s required `currentDensity`, scaled by
the map's area (`GameConfig.CurrentCount`): 100 is the baseline —
`CurrentsAtBaseDensity` (3600) arrows on an 8192² map — so 200 is twice as
thick, 10 a tenth, 0 none, and the same density gives the same look on a map
of any size.

## Blob shadows (`world/BlobShadow.cs`)

There are no real shadows (the terrain is unshaded); anything that needs one
uses `BlobShadow`, a flat dark translucent disc the caller positions on the
ground each frame (unit disc mesh; set Position/Scale/Rotation, `SetStrength`
to fade). The boat parks an ellipse on the water under its hull; cannonballs
carry a disc that shrinks and fades with height (`BlobShadow.HeightT`).

## Rendering notes

- Colours: the config hex colours are stored raw (map texture bytes, chunk
  vertex colours) and converted sRGB→linear in the shaders, so the on-screen
  values equal the hex values, like the web version's raw shader / minimap.
  The ground grid is mixed in sRGB space on purpose to match the web look.
- Terrain is `unshaded`; column sides are pre-darkened (`SideShadeX/Y`, in
  linear space like three.js). There is one `DirectionalLight3D` and a
  colour ambient (`WorldEnvironment`) in `Main` — they only affect the lit
  materials of the boat, guns and cannonballs, never the terrain. 4x MSAA.
- Terrain: the plane (`TerrainPlane`, height 0 = water) is one draw call;
  chunks (`TerrainChunks`) are one `ArrayMesh` each, bounded by
  `MaxVisibleChunks` via the LOD step (majority terrain per block). Built
  nearest-first inside `BuildBudgetMs` per frame, LRU-evicted past
  `MaxCachedChunks`. Water cells emit nothing — the plane is the water.
  Gameplay (collision, ballistics) always uses the raw map, not the LOD.
  `MaxLodStep` is 64, enough for an 8192-cell map to fit in
  `MaxVisibleChunks` when the whole world is on screen.
- The map texture (one texel per cell) is built once by `TerrainPlane` and
  shared with the minimap — at 8192² that is ~200 MB of RGB8, the biggest
  single allocation in the game.
