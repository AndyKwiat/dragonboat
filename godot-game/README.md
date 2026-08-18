# dragonboat/godot-game

Godot 4.7 (.NET / C#) port of `../game`, the three.js dragonboat game. Same
world, same rules, same look: an isometric (or 45° perspective) view of the
world-generator map, a box boat you drive around, cities with markets, crew,
rations, wages and a game clock. It exists to compare the two engines side by
side, so behaviour and appearance track `../game` — when one changes, port it
to the other.

## Running

```bash
./build.sh          # dotnet build — after any code change (~1 s incremental)
./run.sh            # Godot --path .  (extra args pass through, e.g. ./run.sh -- --screenshot=x.png)
./buildAndRun.sh    # both
```

Requires the Godot 4.7 **.NET** build (`/Applications/Godot_mono.app`; the
regular build cannot run C#) and a .NET SDK (8+; the project targets net8.0
with roll-forward).

Controls are the web version's: WASD/arrows drive the boat (follow mode) or
pan (free mode), wheel zooms, drag pans in free mode, **C** cycles camera
mode, **V** cycles the camera rig (isometric / perspective), click a city
name for its market, Esc closes it.

### Scripted runs / screenshots

User args after `--` drive a run without a human, which is how changes are
verified (there is no test suite — look at the screenshots):

```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path . -- --screenshot=screenshots/iso.png --frames=120
$G --path . -- --screenshot=screenshots/city.png --mode=free --center=11301,18085 --city=Chenglu
$G --path . -- --screenshot=screenshots/far.png --zoom=0.15 --rig=perspective
$G --path . -- --screenshot=screenshots/drive.png --frames=240 --hold=W,D
```

| flag | effect |
|---|---|
| `--screenshot[=path]` | save the viewport after `--frames` (default 90) frames, then quit |
| `--frames=N` | frames to render before the screenshot |
| `--zoom=Z` | starting zoom, screen px per world unit |
| `--rig=isometric\|perspective` | starting camera rig |
| `--mode=free\|follow` | starting camera mode |
| `--center=x,y` | focus point in world units (free mode) |
| `--city=Name` | open that city's popup |
| `--hold=W,D` | keys held down for the whole run |
| `--size=WxH` | window size |

`screenshots/` and `*.png` are git-ignored.

## Layout

```
project.godot          1280×800 window, hidpi off (viewport px == window points,
                       so "px" means the same as CSS px in ../game), clear colour
Main.tscn              one Node3D with src/Main.cs
data/                  map.dmap, cities.json, products.json — copies of ../game's
                       (public/map.dmap, cities.json, products.json). Regenerate
                       there and copy over.
shaders/               terrain_plane.gdshader (map texture + tile grid),
                       terrain_chunk.gdshader (vertex-coloured columns)
src/Config.cs          every tunable, mirroring ../game/src/config.ts
src/DMap.cs            .dmap parser (terrain indices 0 ocean … 7 city)
src/GameData.cs        cities.json / products.json loader (System.Text.Json)
src/GameTime.cs        30-day months, 24h clock; only moves when the player acts
src/Crew.cs Player.cs Upkeep.cs Market.cs   rules, ported 1:1 from ../game/src
src/InputState.cs      key set / pressed-this-frame / drag / wheel / click, in
                       _UnhandledInput so the GUI (popup) can eat events first
src/CameraRig.cs       CameraState {CenterX, CenterY, Zoom} + rigs (projection,
                       pitch, yaw), placement, project/unproject helpers, clamp
src/CameraControllers.cs  follow (locked to the boat) and free (pan/drag/zoom)
src/Boat.cs            box mesh + arcade physics
src/TerrainPlane.cs    the flat 2D map as a textured plane at height 0 (= water)
src/TerrainChunks.cs   streamed raised-terrain chunk meshes with LOD
src/ui/                Fonts (system Menlo / Helvetica), Hud, Clock, Minimap,
                       CityLabels, CityPopup — Godot Controls on a CanvasLayer
src/Main.cs            wiring, the per-frame loop, the scripted-run flags
```

## Coordinates and camera

World coordinates are the map's: (0,0) top-left, x right, **y down**, in world
units (`TileScale` = 20 per map cell, so 20000×20000). In Godot space that is
`X = x, Z = y, Y = height` (`CameraRig.WorldToGodot`), so north (world -y) is
-Z and no axis flipping is needed anywhere: dmap rows go straight into the
texture, `boat.Rotation.Y = -heading` (Godot turns +X towards -Z, world
headings turn +X towards +Z/south).

`zoom` is screen px per world unit at the camera's focus point. A **rig**
(`Config.Rigs`) is orthographic or perspective plus pitch/yaw; `Apply` puts the
camera behind the focus along the rig's ground `Forward` looking down at the
pitch, so screen-up is `Forward`. Orthographic size = viewport height / zoom;
perspective distance is derived from zoom and the fov. Free-camera pan/drag
and zoom anchoring go through `ScreenDeltaToGround` (yaw-rotated, vertical
foreshortened by sin(pitch)); labels use `Camera3D.UnprojectPosition`; the
chunk streamer casts the four screen corners onto the ground with
`ProjectRayOrigin/Normal`.

## Rendering notes

- Colours: the config hex colours are stored raw (map texture bytes, chunk
  vertex colours) and converted sRGB→linear in the shaders, so the on-screen
  values equal the hex values, like the web version's raw shader / minimap. The
  ground grid is mixed in sRGB space on purpose to match the web look.
- Everything is `unshaded`; column sides are pre-darkened (`SideShadeX/Y`, in
  linear space like three.js). No lights, no environment; 4x MSAA on the 3D scene.
- Terrain: the plane is one draw call; chunks are one `ArrayMesh` each,
  bounded by `MaxVisibleChunks` via the LOD step (majority terrain per block).
  Built nearest-first inside `BuildBudgetMs` per frame, LRU-evicted past
  `MaxCachedChunks`. Water cells emit nothing — the plane is the water.
- Text uses `SystemFont`s (Menlo for `monospace`, Helvetica Neue for
  `system-ui`); sizes are the web version's CSS px.
- The city popup measures its content for a few frames after opening /
  resizing (wrapped labels only know their height after layout) and sizes
  itself to 62vw clamped to 520–900 px, at most 80vh, scrolling beyond that.

## Conventions

C# with `Nullable` on, 4-space indent, one class per file, `partial` only where
Godot needs it (Node subclasses). Tunables go in `Config.cs`. Keep the rules
files (`GameTime`, `Crew`, `Player`, `Upkeep`, `Market`) free of Godot types so
they stay a straight port of `../game/src`.
