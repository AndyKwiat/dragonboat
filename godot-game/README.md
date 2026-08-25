# dragonboat/godot-game

Godot 4.7 (.NET / C#) dragonboat game: an isometric (or 45° perspective)
view of a hand-painted terrain map; you sail a two-masted boat between cities,
trade, feed and pay a crew — and fire its cannons at anything you click.
It began as a port of a three.js prototype (since deleted) plus the boat
combat from `../combat-prototype`; this repo is now the only home of all
the rules.

**Start here, then read only the doc for the part you're changing:**

| doc | covers |
|---|---|
| [docs/world.md](docs/world.md) | map, coordinates, camera rigs/modes, terrain rendering and streaming, terrain queries |
| [docs/boat.md](docs/boat.md) | the player boat: handling, lean/bob, land collision, the 3D model |
| [docs/combat.md](docs/combat.md) | cannons, cannonballs, impact effects, targets (enemy-ready), aiming |
| [docs/trading.md](docs/trading.md) | cities, markets, crew, rations/wages, the game clock, the city popup |
| [docs/banking.md](docs/banking.md) | city banks: borrowing against honour, interest, due dates, on-time rewards and late penalties |
| [docs/ui.md](docs/ui.md) | fonts, HUD, clock, minimap, city labels, the event log, the tweak panel |

## Running

```bash
./build.sh          # dotnet build — after any code change (~1 s incremental)
./run.sh            # Godot --path .  (extra args pass through, e.g. ./run.sh -- --screenshot=x.png)
./buildAndRun.sh    # both
```

`./run.sh` with no window flags opens the game windowed at the full size of
the screen, in the top-left corner. Passing `--screenshot` or `--size` keeps
the small `project.godot` window instead (1280×800 logical), so scripted runs
and their screenshots stay a predictable size.

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
| *(sail into a city)* | opens its market too — you must leave the quay, and wait out a 5 s cooldown, before it opens again |
| Shift (hold) | fast-forward game time — ramps up while held, eases back on release |
| C | cycle camera mode (follow / edge / free) |
| V | cycle camera rig (isometric / perspective) |
| T | toggle the live tweak panel (sliders for every `[Tweak]` value) |
| Esc | close the bank window, then the city popup |

### Live tuning

Boat handling, lean, land collision, cannon ballistics and effect sizes are
`public static` fields on `BoatConfig` / `CombatConfig` tagged
`[Tweak(group, min, max, step)]` (see `src/core/Tweaks.cs`). The values in
code are the defaults and the source of truth; **T** opens a slider panel to
move them while playing, **dump** on that panel saves every value moved off
its default to `tweaks.dump` (gitignored; also console + clipboard) as C#
lines. That file is re-applied over the defaults at every launch, so dumped
tweaks persist between runs — delete it to get the defaults back, or fold
its lines into the config for keeps. `--set=Name=Value` overrides from the
command line for scripted runs (it wins over tweaks.dump). Adding a knob =
adding an attribute; keep names unique across config classes.

### Scripted runs / screenshots

User args after `--` drive a run without a human, which is how changes are
verified (`dotnet test tests` covers the Godot-free core — the config,
the .terrain parser, city placement, docking and the loan rules;
everything else is checked by scripted runs — look at the screenshots, and at the line
the run prints: boat position/heading/speed, shots and hits):

```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path . -- --screenshot=screenshots/iso.png --frames=120
$G --path . -- --screenshot=screenshots/world.png --mode=free --center=4096,4096 --zoom=0.14   # the whole 8192² map
$G --path . -- --screenshot=screenshots/city.png --city   # the start city's market (or --city=Name)
$G --path . -- --screenshot=screenshots/far.png --zoom=0.5 --rig=perspective
$G --path . -- --screenshot=screenshots/drive.png --frames=240 --hold=W,D
$G --path . -- --screenshot=screenshots/shore.png --frames=900 --hold=W          # sail out of the start harbour
$G --path . -- --screenshot=screenshots/shot.png --frames=435 --zoom=4 --fire=110,-10@100 --set=Gravity=60
$G --path . -- --screenshot=screenshots/dock.png --frames=400 --seed=7 --start=4415,5495,180 --hold=W --press=Escape@150   # sail into a city
$G --path . -- --screenshot=screenshots/bank.png --seed=7 --honour=800 --bank            # a city's bank window
$G --path . -- --screenshot=screenshots/late.png --seed=7 --honour=800 --loan=1000@4     # a loan four days overdue
```

| flag | effect |
|---|---|
| `--screenshot[=path]` | save the viewport after `--frames` (default 90) frames, then quit |
| `--frames=N` | frames to render before the screenshot (runs are vsynced, typically 120 fps — a 3 s cannonball flight is ~360 frames) |
| `--zoom=Z` | starting zoom, screen px per world unit |
| `--rig=isometric\|perspective` | starting camera rig |
| `--mode=follow\|edge\|free` | starting camera mode |
| `--center=x,y` | focus point in world units (free mode) |
| `--city[=Name]` | open the market of the city the boat starts off, or a named one (names are dealt fresh each run) |
| `--seed=N` | fixed world RNG — the same cities, sizes, names and enemy fleet every run |
| `--hold=W,D` | keys held down for the whole run |
| `--press=Key@frame,...` | one-shot key presses on those frames (`--hold` never counts as a press; `Escape` closes the market) |
| `--start=x,y[,deg]` | put the boat here, heading in degrees (0 = east, 90 = south) |
| `--fire=dx,dy@f1,f2` | fire at the ground point dx,dy from the boat on those frames (default frame 10); only cannons that bear fire |
| `--set=Name=V,Name=V` | override `[Tweak]` values by field name |
| `--honour=N` | set the player's honour (bank credit is a multiple of it) |
| `--loan=N[@D]` | borrow N from that city's bank at launch, its due date moved D days earlier (`@4` = four days late, `@-2` = due in two days) |
| `--bank` | open that city's bank window over its market |
| `--tweaks` | start with the tweak panel open |
| `--size=WxH` | window size |

`screenshots/` and `*.png` are git-ignored.

## Layout

Each subsystem has its own folder and doc; `src/Main.cs` is the only file
that knows about all of them.

```
project.godot          1280×800 window, hidpi on (Main sets the content scale
                       factor, so "px" stays a CSS-like logical px), clear colour
Main.tscn              one Node3D with src/Main.cs
game-config.yml        which terrain file to load, how many cities to
                       generate, how thick the currents run — every key
                       required, no code defaults
map1.terrain           the map, painted in ../terrain-gen (TGN1, gzipped);
                       one cell = one world unit
data/                  products.json (canonical here) and city-names.json;
                       cities themselves are generated on the coast every
                       launch (docs/trading.md)
shaders/               terrain_plane.gdshader (map texture + tile grid),
                       terrain_chunk.gdshader (vertex-coloured columns)
docs/                  one markdown doc per subsystem (table above)
tests/                 xunit tests for the Godot-free core (`dotnet test tests`)
src/Main.cs            wiring, the per-frame loop, the scripted-run flags
src/core/              Config (world/camera/trading/UI constants), GameConfig
                       (game-config.yml), TerrainMap (.terrain parser), City,
                       GameData (products + the run's cities), GameTime (clock), InputState,
                       Tweaks (the [Tweak] registry behind live tuning)
src/world/             CameraRig + CameraControllers, TerrainPlane, TerrainChunks,
                       Terrain (water/height/collision queries), CityPlacer,
                       Currents                                          → docs/world.md
src/boat/              BoatConfig, Boat (physics, lean, land collision, hittable),
                       BoatModel (hull/masts/sails)                       → docs/boat.md
src/combat/            CombatConfig, CombatSystem, Cannon, CannonBattery,
                       Projectiles, Effects, ICombatTarget                → docs/combat.md
src/enemies/           EnemyShips — the stationary hostile fleet (docs/enemies.md)
src/trading/           Player, Crew, Upkeep, Market, StoryEvents — pure rules
                                                                          → docs/trading.md
src/banking/           Loans — city banks, interest, due dates, late
                       penalties; pure rules                              → docs/banking.md
src/ui/                Fonts, Hud, Clock, Minimap, CityLabels, CityPopup,
                       BankPopup, EventLog, TweakPanel
                       — Godot Controls on a CanvasLayer                  → docs/ui.md
```

Per-frame order in `Main._Process`: key toggles (C/V/T) → boat physics in
game time (sub-stepped; skipped in free mode) → camera controller → clamp →
rig apply → terrain chunk streaming → city labels (hover/click) → combat
(aim, fire, projectiles, effects) → popups → upkeep → loans (the midnight
pass) → clock/event log/minimap/HUD → screenshot hook.

## Conventions

C# with `Nullable` on, 4-space indent, one class per file, `partial` only
where Godot needs it (Node subclasses). World/camera/trading/UI constants go
in `core/Config.cs`; boat and combat knobs go in their own `*Config.cs` as
`[Tweak]` statics. Keep the rules files (`GameTime`, `Crew`, `Player`,
`Upkeep`, `Market`, `StoryEvents`) and the world-building ones
(`GameConfig`, `TerrainMap`, `City`, `CityPlacer`) free of Godot types —
pure, testable code, and `tests/` compiles them straight from `src/`. New gameplay systems get their own `src/<name>/` folder and
`docs/<name>.md`, and are wired in `Main.cs`; don't make the trading code
reach into combat or vice versa — both talk to the boat/world, not each other.
