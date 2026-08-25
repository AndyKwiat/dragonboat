# godot-game

Godot 4.7 (.NET / C#) isometric sail-trade-fight game. `README.md` is the
full map of the repo; each system has a doc in `docs/*.md` — read the one
for the system you're touching, and keep it (and the README) updated when
rules change.

## Build / run / verify

- `./build.sh` compiles (dotnet). `./run.sh` runs — needs
  `/Applications/Godot_mono.app`, and always run from this directory.
- `dotnet test tests` covers the Godot-free core; everything else is
  verified with scripted runs + screenshots:
  `./run.sh -- --screenshot=x.png --frames=120 --size=2560x1600` plus the
  other test hooks (`--city`, `--start=x,y,deg`, `--hold=W`,
  `--press=Key@frame`, `--seed=N` for a reproducible world, `--set`,
  `--mode`, `--center`, `--zoom`, `--dump-tweaks`, `--honour`, `--loan`,
  `--bank`, `--vip`, `--deliver`, `--monastery`, `--monk`, `--alms`; see
  `ParseArgs` in `src/Main.cs`). The run's final printed line (boat position/heading/speed,
  shots/hits) is useful for asserting movement without pixels.
- The window is hidpi: screenshots come out at 2× the logical size. A plain
  `./run.sh` fills the screen; `--screenshot`/`--size` keep the small
  1280×800 window, so scripted runs stay predictable.

## Conventions

- Tunables: constants in `src/core/Config.cs`; live-tunable knobs are
  `[Tweak]` statics in `BoatConfig`/`CombatConfig`/`Currents`. Code defaults
  are the source of truth; the in-game dump button writes `tweaks.dump`
  (gitignored), which is re-applied at every launch — when the user asks to
  "apply my tweaks", fold that file's lines into the config defaults.
- Keep `src/trading/*`, `src/banking/*` and the other rules files free of
  Godot types.
- A new gameplay system gets its own `src/<name>/` folder and
  `docs/<name>.md`, wired up only in `Main.cs`; trading and combat never
  reach into each other.
- `game-config.yml` (terrain file, city count, current density) is required
  at launch and has no code defaults; `data/products.json` and `data/city-names.json` are
  canonical. Cities are generated on the coast every run (`CityPlacer`);
  maps are `.terrain` files painted in `../terrain-gen`. One map cell = one
  world unit.
- `dotnet test tests` runs the xunit suite over the Godot-free core
  (config, terrain parsing, city placement, docking, bank loans, VIP and
  monk missions); it
  compiles those files straight from `src/`, so keep them free of Godot
  types — and add a new rules file to `tests/Tests.csproj` when you write
  one.
- Commit messages start with `godot-game: `.
