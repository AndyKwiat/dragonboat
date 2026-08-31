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
| [docs/boat.md](docs/boat.md) | the boat: handling, sails vs oars, wind, crew stamina, lean/bob, land and hull collision, the 3D model |
| [docs/combat.md](docs/combat.md) | cannons, cannonballs, impact effects, targets (enemy-ready), aiming |
| [docs/economy.md](docs/economy.md) | who produces what, supply-driven prices, the trade ladder, the world average, cargo cost |
| [docs/trading.md](docs/trading.md) | cities, story events, VIP deliveries, crew, rations/wages, the game clock, the city popup |
| [docs/banking.md](docs/banking.md) | city banks: borrowing against honour, interest, due dates, on-time rewards and late penalties |
| [docs/religion.md](docs/religion.md) | piety, monasteries, alms, monk errands |
| [docs/ui.md](docs/ui.md) | fonts, HUD, clock, minimap, city labels, the event log, the tweak panel |
| [docs/ai.md](docs/ai.md) | boats that sail themselves and fight: orders, states and tactics, guarding a spot, leading a shot, sides |
| [docs/nav.md](docs/nav.md) | the water nav mesh: clearance, water bodies, the quadtree, A* routes, and the N overlay |
| [docs/scenarios.md](docs/scenarios.md) | stripped-down test worlds (`./pathingTest.sh`): a test map, no cities, boats on named markers |

## Running

```bash
./build.sh          # dotnet build — after any code change (~1 s incremental)
./run.sh            # Godot --path .  (extra args pass through, e.g. ./run.sh -- --screenshot=x.png)
./buildAndRun.sh    # both
./pathingTest.sh    # the pathing test scenario instead of the game (docs/scenarios.md)
./battleTest.sh     # the battle test scenario (player + one AI boat on a lake)
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
| WASD / arrows | drive the boat (follow mode) or pan (free mode) — under sail the canvas is always drawing, so W does nothing and S takes the way off |
| **space** | sails up / sails down — canvas is always drawing, as hard as the wind allows (a calm goes nowhere), runs faster but steers lazily; oars ignore the wind, accelerate harder, top out slower and tire the crew (the bar at the top) |
| wheel | zoom; drag pans in free mode |
| **click** | fire the best loaded cannon at the reticle under the cursor — or open a city's market if you clicked its name |
| *(sail into a city)* | opens its market too — you must leave the quay, and wait out a 5 s cooldown, before it opens again |
| Shift (hold) | fast-forward game time — ramps up while held, eases back on release |
| C | cycle camera mode (follow / edge / free) |
| V | cycle camera rig (isometric / perspective) |
| T | toggle the live tweak panel (sliders for every `[Tweak]` value) |
| N | debug: draw the nav mesh over the water, and the routes the AI boats are steering along (docs/nav.md) |
| P | debug: every city label lists every product's price, % of the world average and `(P)` on the cities that produce it; `[` / `]` there narrow it to one product and step through them |
| Esc | close the bank or monastery window, then the city popup |

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
the .terrain parser, city placement, docking, scenario definitions, the
water nav mesh and the routes over it, the AI's orders, targeting, tactics,
gunnery and station keeping, hull collision geometry, the loan, passenger and monastery rules;
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
$G --path . -- --screenshot=screenshots/vip.png --seed=7 --vip=3 --deliver               # land a VIP after three days
$G --path . -- --screenshot=screenshots/jump.png --seed=7 --vip=15                       # a VIP giving up and jumping ship
$G --path . -- --screenshot=screenshots/mon.png --seed=7 --monastery                     # a monastery's window
$G --path . -- --screenshot=screenshots/monk.png --seed=7 --monk=5 --deliver             # land a monk: +1000 piety
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
| `--scenario=name` | load `scenarios/name.yml` instead of the usual world: a test map, no cities, AI boats on markers (docs/scenarios.md) |
| `--nav` | start with the nav-mesh overlay on (N toggles it) |
| `--hold=W,D` | keys held down for the whole run |
| `--press=Key@frame,...` | one-shot key presses on those frames (`--hold` never counts as a press; `Escape` closes the market) |
| `--start=x,y[,deg]` | put the boat here, heading in degrees (0 = east, 90 = south) |
| `--fire=dx,dy@f1,f2` | fire at the ground point dx,dy from the boat on those frames (default frame 10); only cannons that bear fire |
| `--set=Name=V,Name=V` | override `[Tweak]` values by field name |
| `--honour=N` | set the player's honour (bank credit is a multiple of it) |
| `--loan=N[@D]` | borrow N from that city's bank at launch, its due date moved D days earlier (`@4` = four days late, `@-2` = due in two days) |
| `--bank` | open that city's bank window over its market |
| `--vip[=days]` | take a VIP offer aboard, picked up that many days ago (the reward decays daily; at 15 they jump ship) |
| `--deliver` | with `--vip` or `--monk`, open the destination's market so they land |
| `--monastery[=Name]` | open a monastery's window over its city's market |
| `--monk[=days]` | take a monk aboard, accepted that many days ago (30 days to land him) |
| `--alms=daysAgo` | pretend that monastery last took alms then, to see the weekly cooldown |
| `--sails=up\|down` | start under canvas (default) or at the oars |
| `--stamina=0..1` | start the crew part-spent (default 1 = fresh) |
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
game-config.yml        which terrain file to load, how many small/medium/
                       large cities to generate, how thick the currents
                       run, how many story events of each kind are live —
                       every key required, no code defaults
map1.terrain           the map, painted in ../terrain-gen (TGN2, gzipped);
                       one cell = one world unit
pathingTest.terrain    a small test map for the AI bench, markers p0-p3
battleTest.terrain     a 512² lake for the battle bench, markers player/ai0
scenarios/             test-world definitions (--scenario=name), e.g.
                       pathing.yml behind ./pathingTest.sh
data/                  products.json (canonical here — each good is a staple or a
                       luxury) and city-names.json;
                       cities themselves are generated on the coast every
                       launch (docs/trading.md)
shaders/               terrain_plane.gdshader (map texture + tile grid),
                       terrain_chunk.gdshader (vertex-coloured columns)
docs/                  one markdown doc per subsystem (table above)
tests/                 xunit tests for the Godot-free core (`dotnet test tests`);
                       the economy's are SupplyTests / ProductionTests /
                       MarketDealTests over EconomyFixtures.cs
src/Main.cs            wiring, the per-frame loop, the scripted-run flags
src/core/              Config (world/camera/trading/UI constants), News (NewsEvent
                       + the whole-day clock the daily rules share), GameConfig
                       (game-config.yml), TerrainMap (.terrain parser), City,
                       Product, GameData (products + the run's cities), GameTime
                       (clock), InputState,
                       Tweaks (the [Tweak] registry behind live tuning)
src/world/             CameraRig + CameraControllers, TerrainPlane, TerrainChunks,
                       Terrain (water/height/collision queries), CityPlacer,
                       Currents                                          → docs/world.md
src/boat/              BoatConfig, Boat (physics, lean, land collision, hittable),
                       BoatModel (hull/masts/sails), Hulls + BoatCollisions
                       (hull against hull, any size, ramming hook), HelmOrder
                       (what a helm is told, from the keys or from an AI),
                       Sailing (wind + crew stamina; pure rules)          → docs/boat.md
src/combat/            CombatConfig, CombatSystem, Cannon, CannonBattery,
                       Projectiles, Effects, ICombatTarget                → docs/combat.md
src/enemies/           EnemyShips — the moored hostile fleet; the dumb
                       boats, deliberately left alone by src/ai   → docs/ai.md
src/ai/                AiOrder/ProtectOrder/VoyageOrder (missions), AiTactic/
                       StandOffTactic (how to fight), AiState, Targeting,
                       AiSteering, Helmsman, Gunnery (leading a shot),
                       AiConfig ([Tweak] knobs), AiBoat/AiBoats, RouteOverlay
                       — non-player boats that sail themselves and fight;
                       only a scenario spawns any                → docs/ai.md
src/nav/               NavConfig, NavMesh, NavMeshBuilder, NavPath (A* +
                       string-pull; all Godot-free) and NavMeshOverlay — the
                       water nav mesh, baked at load             → docs/nav.md
src/scenarios/         Scenario — scenarios/*.yml, the stripped-down test
                       worlds behind --scenario                  → docs/scenarios.md
src/trading/           the economy — Production, Supply, MarketDeal, WorldPrices,
                       Market                                          → docs/economy.md
                       everything else it trades in — Player, Crew, Upkeep,
                       StoryEvents, Vips, Docking; pure rules           → docs/trading.md
src/banking/           Loans — city banks, interest, due dates, late
                       penalties; pure rules                              → docs/banking.md
src/religion/          Monasteries — piety, alms, monk errands; pure rules
                                                                          → docs/religion.md
src/ui/                Fonts, Hud, Clock, StaminaBar, WindDial, Minimap, CityLabels, CityPopup,
                       BankPopup, MonasteryPopup, EventLog, TweakPanel
                       — Godot Controls on a CanvasLayer                  → docs/ui.md
```

Per-frame order in `Main._Process`: key toggles (C/V/T/F/N) → boat physics in
game time (sub-stepped; skipped in free mode) → camera controller → clamp →
rig apply → terrain chunk streaming → city labels (hover/click) → combat
(aim, fire, projectiles, effects) → popups → upkeep → the daily pass (loans,
VIP passengers, monasteries) → clock/event log/minimap/HUD → screenshot hook.

## Conventions

C# with `Nullable` on, 4-space indent, one class per file, `partial` only
where Godot needs it (Node subclasses). World/camera/trading/UI constants go
in `core/Config.cs`; boat and combat knobs go in their own `*Config.cs` as
`[Tweak]` statics. Keep the rules files (`GameTime`, `Crew`, `Player`,
`Upkeep`, `Supply`, `Production`, `MarketDeal`, `WorldPrices`, `Market`,
`StoryEvents`) and the world-building ones
(`GameConfig`, `TerrainMap`, `City`, `CityPlacer`, `Scenario`, `AiState`,
`NavMesh`) free of Godot types —
pure, testable code, and `tests/` compiles them straight from `src/`. Where a
rule needs something Godot-tainted, it takes a narrow interface instead of the
class (`ITrader`, which `Player` implements for `MarketDeal`) so it stays
testable. New gameplay systems get their own `src/<name>/` folder and
`docs/<name>.md`, and are wired in `Main.cs`; don't make the trading code
reach into combat or vice versa — both talk to the boat/world, not each other.
