# World: map, coordinates, camera, terrain

Files: `src/core/DMap.cs`, `src/core/Config.cs`, `src/world/*`,
`shaders/*`. Nothing here knows about trading or combat; both of those ask
the world questions through `Terrain` and `CameraRig`.

## The map

`data/map.dmap` is exported by `../world-generator` (format spec:
`../world-generator/map-format.md`): `DMAP` magic, uint32 LE width, uint32
LE height, then width×height uint8 terrain indices, row-major, top to bottom.
Indices: 0 ocean, 1 beach, 2 grassland, 3 forest, 4 mountain, 5 river,
6 canal, 7 city. `DMap.TerrainAt` returns ocean outside the map. Each cell
is `Config.TileScale` (20) world units, so the 1000×1000 map is a
20000×20000 world; nothing else hardcodes the size.

**Water** is ocean, river and canal (`Terrain.IsWaterIndex`) — the blue and
dark-blue cells. Everything else is land and rises out of the water by
`Config.TerrainHeights[index]` (beach 3, grass 8, forest 10, mountain 14,
city 24).

## Coordinates and camera

World coordinates are the map's: (0,0) top-left, x right, **y down**, in
world units. In Godot space that is `X = x, Z = y, Y = height`
(`CameraRig.WorldToGodot`), so north (world -y) is -Z and no axis flipping is
needed anywhere: dmap rows go straight into the texture, `boat.Rotation.Y =
-heading` (Godot turns +X towards -Z, world headings turn +X towards +Z/south).

`zoom` is screen px per world unit at the camera's focus point. A **rig**
(`Config.Rigs`, cycled with V) is orthographic or perspective plus pitch/yaw;
`CameraRig.Apply` puts the camera behind the focus along the rig's ground
`Forward` looking down at the pitch, so screen-up is `Forward`. Orthographic
size = viewport height / zoom; perspective distance is derived from zoom and
the fov. A camera **mode** (`CameraControllers.cs`, cycled with C) decides
where the focus is: `follow` locks to the boat with a speed-scaled look-ahead
(driven by `Boat.GroundSpeed`, the boat's actual progress, so a hull held by
the shore stays centred); `free` pans/drags/zooms. Free-camera pan/drag and
zoom anchoring go through `ScreenDeltaToGround` (yaw-rotated, vertical
foreshortened by sin(pitch)); labels use `Camera3D.UnprojectPosition`; the
chunk streamer casts the four screen corners onto the ground with
`ProjectRayOrigin/Normal`.

## Terrain queries (`world/Terrain.cs`)

A data-only wrapper over `DMap` for gameplay:

- `IsWater(x, y)`, `TerrainAt`, `HeightAt(x, y)` — cell lookups at a world point.
- `ResolveCircle(ref x, ref y, r, out normal)` — pushes a circle out of every
  non-water cell it overlaps (closest-point-on-square vs circle, iterated for
  inside corners) and returns the total push. The boat's hull is three of
  these (see [boat.md](boat.md)); anything else that must stay on water can
  use it too.
- `RaycastGround(origin, dir, max, out hit)` — where a camera ray meets the
  water plane or the top of a raised column (step-and-bisect through the
  band of column heights). Aiming uses it so clicking on a hill targets the
  hilltop, not the water plane underneath.

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
