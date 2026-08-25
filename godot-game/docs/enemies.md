# Enemies: the hostile fleet

Files: `src/enemies/EnemyShips.cs` (the whole system), wired in `Main.cs`
only. See `docs/combat.md` for the cannon/projectile layer it drives.

## Rules

- **Fleet**: `EnemyShips.Count` (20 — 1/5 of the old 100, matching the
  world's 20 cities) ships, spawned at launch on random water cells that
  have land within `MaxLandDistanceCells` (20 cells, i.e. ~20 m) — so they
  lurk in rivers and coastal water, never the open ocean — and
  never within `MinSpawnDistance` (400) of the player's start, each with
  a random fixed heading. They never move and never turn.
- **Same hardware as the player**: hull dimensions and hit box from
  `BoatConfig`, guns from `CombatConfig.PlayerMounts`, ballistics/reload
  from the shared `CombatConfig` knobs — so a cannon tweak on the T panel
  retunes every ship in the world, the player's included. The hull is the
  player model tinted red (`EnemyShips.HullColor`).
- **AI**: "click on the player's boat as fast as possible". Every update,
  the best loaded cannon that bears on the boat and has the physical range
  fires at its position — exactly the player's click path
  (`CannonBattery.BestCannon` → `Projectiles.Fire`). A ship whose cannons
  can't bear (wrong side, bad angle) just stays silent until the boat
  moves into an arc.
- **Logic gating**: a ship's logic (reload ticking, firing) runs only
  while it is within `UpdateRangeFactor` (1.5) × the cannons' physical
  range (`MuzzleSpeed²/Gravity`) of the player boat. Nothing updates while
  the terrain streamer is past its first LOD (`TerrainChunks` step > 1,
  i.e. zoomed far out) or while the world is paused (city menu open).
- **Damage**: ships are `ICombatTarget`s — the player's balls hit them
  (red hull flash, same `HitMargin`), and `HitsToSink` (3) hits sinks one:
  a wide splash ring and `KillHonour` (50) honour for the player, paid the
  moment she goes down. The wreck then respawns fresh (full health, new
  random heading) at a new valid spawn point outside the camera's current
  ground view, so the fleet never shrinks — the off-screen constraint is
  dropped only when the whole map is on screen. Enemy balls can also hit
  *other* enemy ships that stray into a line of fire.

- **Combat camera**: the player's first shot in follow mode with a ship
  in cannon range flips the camera to the edge cam; leaving range flips
  it back (see docs/world.md, "combat camera").

## Verifying

Scripted runs: `--start=x,y` next to a known ship position makes it open
fire (the run's final line counts its shots); `--fire=dx,dy@frame` three
times at a ship's offset sinks it. Positions are random per run — for a
deterministic test, temporarily seed the `Random` handed to
`EnemyShips.Init` in `Main.cs`.
