# godot-game

Godot 4.7 (.NET / C#) isometric sail-trade-fight game. `README.md` is the
full map of the repo; each system has a doc in `docs/*.md` — read the one
for the system you're touching, and keep it (and the README) updated when
rules change.

## Build / run / verify

- `./build.sh` compiles (dotnet). `./run.sh` runs — needs
  `/Applications/Godot_mono.app`, and always run from this directory.
- No test suite. Verify changes with scripted runs + screenshots:
  `./run.sh -- --screenshot=x.png --frames=120 --size=2560x1600` plus the
  other test hooks (`--city`, `--start=x,y,deg`, `--hold=W`, `--set`,
  `--mode`, `--center`, `--zoom`, `--dump-tweaks`; see `ParseArgs` in
  `src/Main.cs`). The run's final printed line (boat position/heading/speed,
  shots/hits) is useful for asserting movement without pixels.
- The window is hidpi: screenshots come out at 2× the logical size.

## Conventions

- Tunables: constants in `src/core/Config.cs`; live-tunable knobs are
  `[Tweak]` statics in `BoatConfig`/`CombatConfig`/`Currents`. Code defaults
  are the source of truth; the in-game dump button writes `tweaks.dump`
  (gitignored), which is re-applied at every launch — when the user asks to
  "apply my tweaks", fold that file's lines into the config defaults.
- Keep `src/trading/*` and the rules files free of Godot types.
- A new gameplay system gets its own `src/<name>/` folder and
  `docs/<name>.md`, wired up only in `Main.cs`; trading and combat never
  reach into each other.
- `data/*.json` are canonical (city specialities are dealt at runtime);
  `data/map.dmap` comes from `../world-generator`.
- Commit messages start with `godot-game: `.
