# Scenarios: stripped-down worlds for testing

Files: `src/scenarios/Scenario.cs`, `scenarios/*.yml`, `pathingTest.sh`,
wired up in `src/Main.cs`.

Some things are hard to see in the real game. Boat AI is the first of them:
the world is 8192 cells wide, cities and currents and a hostile fleet are all
moving at once, and a boat that steers itself is one grey hull among many. A
**scenario** is a launch that keeps the engine and subtracts the world — a
small hand-painted map, no cities, no currents, no fleet, and the boats under
test dropped on named spots.

```bash
./pathingTest.sh                          # the pathing scenario, windowed
./battleTest.sh                           # the battle scenario, windowed
./pathingTest.sh --screenshot=x.png --frames=300 --mode=free --center=512,512 --zoom=1
./run.sh -- --scenario=pathing            # the same thing the long way round
```

`./pathingTest.sh` passes its arguments straight through, so every scripted-run
flag in the [README](../README.md) (`--screenshot`, `--frames`, `--hold`,
`--press`, `--set`, …) works in a scenario too. **Without `--scenario` none of
this code runs**: no scenario is loaded, no AI boat is spawned, and the game
launches exactly as it always did.

## Writing one

A scenario is a file in `scenarios/`, named after the flag that loads it
(`--scenario=pathing` → `scenarios/pathing.yml`). It is the same flat
`key: value` YAML subset as `game-config.yml`, and only `terrain` is required:

| key | default | |
|---|---|---|
| `terrain` | *(required)* | the `.terrain` file to load instead of `game-config.yml`'s |
| `cities` | `true` | generate the coastal cities? `false` = no economy, no labels, no docking |
| `currents` | `true` | lay down current zones? `false` = nothing shoves a boat off its course |
| `enemyFleet` | `true` | spawn the legacy moored fleet (`src/enemies`)? |
| `player` | *(none)* | marker tag to start the player boat on; without it the boat starts off a city's shore as usual, which needs `cities: true` |
| `boats` | *(none)* | AI boats: `tag[:order[>destination]][*count][@team]` — e.g. `p1:voyage>p2, ai0:protect*3@1, p3`. A count greater than one puts a group on the marker, in line astern and spaced so hulls do not start touching; each guard stations where it actually is. |
| `ai` | `idle` | the order boats in `boats` start with when they don't name one |
| `team` | `1` | the side boats take when they don't name one; the player is always 0 |

An unknown key is an error rather than a silent default: a typo in a scenario
would otherwise quietly give you a different test from the one you wrote.
Everything else — the window, the camera, the wind, the clock, the crew, the
tweak panel — is the game's, untouched.

## Markers

Spawn points come from the map file itself. A marker is a labelled circle
painted in `../terrain-gen` and carried in the `.terrain` file's `TGN2`
marker section ([world.md](world.md)); a scenario refers to one by its tag.
`pathingTest.terrain` carries `p0`–`p3`. Only the centre is used — the radius
is the painter's business for now.

A tag the map does not carry is a fatal launch error listing the tags it does
carry: a scenario that silently spawned nothing would be a test that silently
passed. Tags need not be unique; the last one painted wins, the same rule the
file format uses for overlapping markers.

AI boats spawn with their sails struck. One with somewhere to go starts
pointing at it; one with nowhere to go faces the player's start. A
destination is a marker tag like any other, so `p1:voyage>p2` sends the boat
on `p1` to `p2` ([ai.md](ai.md)).

## The pathing scenario

`scenarios/pathing.yml` — `pathingTest.terrain` (1024², a network of rivers
through grassland), no cities, no currents, no fleet, the player on `p0`, and
AI boats on `p1` and `p2` each bound for the other's marker, so they make the
same winding passage in opposite directions and pass each other on the way.
It is the bench the boat AI ([ai.md](ai.md)) is built on.

A scripted run's last printed line ends with a line per AI boat — state,
position, heading, speed — so a pathing run can be asserted on without
looking at pixels:

```
... shots 0 hits 0; ai boats idle@(918.6, 50.1) 167° 0.0; idle@(396.2, 528.9) -136° -0.0
```

## The battle scenario

`scenarios/battle.yml` — `battleTest.terrain` (512², one lake in forest), no
cities, no currents, no legacy fleet, the player on marker `player` and one
AI boat on `ai0`. `./battleTest.sh` runs it.

Both hulls are armed and both are targets — every boat carries
`CombatConfig.PlayerMounts`. `ai0:protect*3` puts **three** boats there in line astern, each guarding the
spot it spawns on, each coming for the player when the player gets too close,
and each spreading out rather than piling onto the same bit of water
([ai.md](ai.md)).
Only a boat whose order can fight is given guns, so the idle boat in the
gunnery scenario stays unarmed and that bench's shot tally stays the
player's.

The two markers are 291 units apart and a cannon reaches
`MuzzleSpeed² / Gravity` ≈ 163, so nobody starts in range of anybody: the
first thing a fighting AI has to decide is whether to close.

## The gunnery scenario

`scenarios/gunnery.yml` — the same map with a single idle AI boat parked on
`p0`, the marker the player starts on, so a scripted shot can be aimed at a
hull that stays put. It is the regression bench for AI boats being solid to
cannon fire ([ai.md](ai.md)); the file itself carries the two commands and
the `hits 1` / `hits 0` they must print.

## Adding one

Write `scenarios/<name>.yml`, paint the markers it needs into a `.terrain`
map, and run it with `--scenario=<name>`. Add a shell shortcut like
`pathingTest.sh` if it is one you will run often, and a case in
`tests/ScenarioTests.cs` asserting the file parses and every marker it names
exists in the map — that check is what stops a scenario rotting when a map is
repainted.
