# dragonboat/godot-game

Godot 4.7 (.NET / C#) port of `../game`, the three.js dragonboat game, plus
the boat combat from `../combat-prototype`. An isometric (or 45° perspective)
view of the world-generator map; you sail a two-masted boat between cities,
trade, feed and pay a crew — and fire its cannons at anything you click.
Behaviour and appearance of the world/trading half track `../game` (when one
changes, port it to the other); the combat half is the prototype's rules,
ported 1:1 and now the only home of that code.

**Start here, then read only the doc for the part you're changing:**

| doc | covers |
|---|---|
| [docs/world.md](docs/world.md) | map, coordinates, camera rigs/modes, terrain rendering and streaming, terrain queries |
| [docs/boat.md](docs/boat.md) | the player boat: handling, lean/bob, land collision, the 3D model |
| [docs/combat.md](docs/combat.md) | cannons, cannonballs, impact effects, targets (enemy-ready), aiming |
| [docs/trading.md](docs/trading.md) | cities, markets, crew, rations/wages, the game clock, the city popup |
| [docs/ui.md](docs/ui.md) | fonts, HUD, clock, minimap, city labels, the tweak panel |

## Running

```bash
./build.sh          # dotnet build — after any code change (~1 s incremental)
./run.sh            # Godot --path .  (extra args pass through, e.g. ./run.sh -- --screenshot=x.png)
./buildAndRun.sh    # both
```

Requires the Godot 4.7 **.NET** build (`/Applications/Godot_mono.app`; the
regular build cannot run C#) and a .NET SDK (8+; the project targets net8.0
with roll-forward). Always run from this directory: `--path .` elsewhere
opens the project picker and hangs a scripted run.

### Controls

| key | |
|---|---|
| WASD / arrows | drive the boat (follow mode) or pan (free mode) |
| wheel | zoom; drag pans in free mode |
| **click** | fire the best loaded cannon at the reticle under the cursor — or open a city's market if you clicked its name |
| C | cycle camera mode (follow / free) |
| V | cycle camera rig (isometric / perspective) |
| T | toggle the live tweak panel (sliders for every `[Tweak]` value) |
| Esc | close the city popup |

### Live tuning

Boat handling, lean, land collision, cannon ballistics and effect sizes are
`public static` fields on `BoatConfig` / `CombatConfig` tagged
`[Tweak(group, min, max, step)]` (see `src/core/Tweaks.cs`). The values in
code are the defaults and the source of truth; **T** opens a slider panel to
move them while playing, **dump** on that panel prints the current values as
C# lines (and copies them to the clipboard) so a good setting can be pasted
straight back into the config file. `--set=Name=Value` does the same from the
command line for scripted runs. Adding a knob = adding an attribute; keep
names unique across config classes.

### Scripted runs / screenshots

User args after `--` drive a run without a human, which is how changes are
verified (there is no test suite — look at the screenshots, and at the line
the run prints: boat position/heading/speed, shots and hits):

```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path . -- --screenshot=screenshots/iso.png --frames=120
$G --path . -- --screenshot=screenshots/city.png --mode=free --center=11301,18085 --city=Chenglu
$G --path . -- --screenshot=screenshots/far.png --zoom=0.15 --rig=perspective
$G --path . -- --screenshot=screenshots/drive.png --frames=240 --hold=W,D
$G --path . -- --screenshot=screenshots/shore.png --frames=900 --start=3300,15700,40 --hold=W   # slide along the beach east of the start
$G --path . -- --screenshot=screenshots/shot.png --frames=435 --zoom=4 --start=3380,15829,0 --fire=110,-10@100 --set=Gravity=60
```

| flag | effect |
|---|---|
| `--screenshot[=path]` | save the viewport after `--frames` (default 90) frames, then quit |
| `--frames=N` | frames to render before the screenshot (runs are vsynced, typically 120 fps — a 3 s cannonball flight is ~360 frames) |
| `--zoom=Z` | starting zoom, screen px per world unit |
| `--rig=isometric\|perspective` | starting camera rig |
| `--mode=free\|follow` | starting camera mode |
| `--center=x,y` | focus point in world units (free mode) |
| `--city=Name` | open that city's popup |
| `--hold=W,D` | keys held down for the whole run |
| `--start=x,y[,deg]` | put the boat here, heading in degrees (0 = east, 90 = south) |
| `--fire=dx,dy@f1,f2` | fire at the ground point dx,dy from the boat on those frames (default frame 10); only cannons that bear fire |
| `--set=Name=V,Name=V` | override `[Tweak]` values by field name |
| `--tweaks` | start with the tweak panel open |
| `--size=WxH` | window size |

`screenshots/` and `*.png` are git-ignored.

## Layout

Each subsystem has its own folder and doc; `src/Main.cs` is the only file
that knows about all of them.

```
project.godot          1280×800 window, hidpi off (viewport px == window points,
                       so "px" means the same as CSS px in ../game), clear colour
Main.tscn              one Node3D with src/Main.cs
data/                  map.dmap, cities.json, products.json — copies of ../game's
                       (public/map.dmap, cities.json, products.json). Regenerate
                       there and copy over.
shaders/               terrain_plane.gdshader (map texture + tile grid),
                       terrain_chunk.gdshader (vertex-coloured columns)
docs/                  one markdown doc per subsystem (table above)
src/Main.cs            wiring, the per-frame loop, the scripted-run flags
src/core/              Config (world/camera/trading/UI constants), DMap parser,
                       GameData (cities/products), GameTime (clock), InputState,
                       Tweaks (the [Tweak] registry behind live tuning)
src/world/             CameraRig + CameraControllers, TerrainPlane, TerrainChunks,
                       Terrain (water/height/collision queries)           → docs/world.md
src/boat/              BoatConfig, Boat (physics, lean, land collision, hittable),
                       BoatModel (hull/masts/sails)                       → docs/boat.md
src/combat/            CombatConfig, CombatSystem, Cannon, CannonBattery,
                       Projectiles, Effects, ICombatTarget                → docs/combat.md
src/trading/           Player, Crew, Upkeep, Market — rules, ported 1:1 from ../game/src
                                                                          → docs/trading.md
src/ui/                Fonts, Hud, Clock, Minimap, CityLabels, CityPopup, TweakPanel
                       — Godot Controls on a CanvasLayer                  → docs/ui.md
```

Per-frame order in `Main._Process`: key toggles (C/V/T) → boat physics in
game time (sub-stepped; skipped in free mode) → camera controller → clamp →
rig apply → terrain chunk streaming → city labels (hover/click) → combat
(aim, fire, projectiles, effects) → popup → upkeep → clock/minimap/HUD →
screenshot hook.

## Conventions

C# with `Nullable` on, 4-space indent, one class per file, `partial` only
where Godot needs it (Node subclasses). World/camera/trading/UI constants go
in `core/Config.cs`; boat and combat knobs go in their own `*Config.cs` as
`[Tweak]` statics. Keep the rules files (`GameTime`, `Crew`, `Player`,
`Upkeep`, `Market`) free of Godot types so they stay a straight port of
`../game/src`. New gameplay systems get their own `src/<name>/` folder and
`docs/<name>.md`, and are wired in `Main.cs`; don't make the trading code
reach into combat or vice versa — both talk to the boat/world, not each other.
