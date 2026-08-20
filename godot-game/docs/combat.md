# Combat: cannons, cannonballs, effects, targets

Files: `src/combat/*`. Ported from `../combat-prototype/index.html` (three.js
sandbox; its `P` object became `CombatConfig`, its per-frame loop became
`CombatSystem.Update`). The prototype's enemy boat was *not* ported — there
are no enemies yet — but the structure assumes them: any number of armed
vessels, any number of hittable targets.

## Pieces

```
CombatSystem (Node3D, one per game)          src/combat/CombatSystem.cs
 ├─ Effects      rings/bursts that fade      src/combat/Effects.cs
 ├─ Projectiles  cannonballs in flight       src/combat/Projectiles.cs
 ├─ reticle      ring under the cursor
 ├─ targets      List<ICombatTarget>         src/combat/ICombatTarget.cs
 └─ PlayerGuns → CannonBattery (child of the player Boat)
                  └─ Cannon × N              src/combat/Cannon.cs, CannonBattery.cs
CombatConfig     all knobs, [Tweak]-able     src/combat/CombatConfig.cs
```

- **`CombatConfig`** — mounts, ballistics, effect sizes/colours, reticle.
  The `[Tweak]` fields show up on the T panel (README → Live tuning).
- **`Cannon`** (`MeshInstance3D`) — a box facing its own +X, placed from a
  `CombatConfig.Mount` (forward, starboard, height offsets in hull units and a
  bearing: 0 = dead ahead, +90 = starboard) scaled to the vessel's length.
  Owns its `Reload` timer and turns tan while reloading. `OffAngle(target)`
  is the flat angle between its facing and the target; `CanBear` checks it
  against `FiringArcDeg / 2`.
- **`CannonBattery`** (`Node3D`, child of the vessel) — the vessel's guns.
  `BestCannon(target)`: the loaded cannon that bears and is most square-on,
  or null. `Vessel` is its parent — projectiles never hit the vessel that
  fired them. The player's battery is `PlayerMounts`: one bow chaser and
  three guns a side.
- **`Projectiles`** — `Fire(cannon, target)` spawns a ball at the cannon,
  with velocity from `SolveLaunch`: fixed launch angle `ArcAngleDeg`, muzzle
  speed solved so it lands on the target (`v² = g d² / (2 cos²a (d tan a −
  h))`); muzzle-flash burst; sets the cannon's reload. `Update` integrates
  under `Gravity`, then tests each ball against every target except its owner
  (`ContainsPoint` → `TakeHit`, hit burst, `Hits++`) and against the ground
  via `Terrain.HeightAt`: on water a pale **splash ring**, on land a brown
  **dirt ring** at the column top — every shot marks where it landed.
- Every ball also drags a **blob shadow** (`world/BlobShadow.cs`): a dark
  disc on the ground straight below it (water plane or column top), shrinking
  to `BallShadowMin` and fading out as the ball climbs to
  `ShadowShrinkHeight` — the height cue the flat terrain doesn't give you.
  All in the "Shadows" tweak group.
- **`Effects`** — transient unshaded meshes with a life: rings grow to
  `RingGrow ×` and fade (splash, dirt), spheres fade (muzzle, hit). Also
  owns the shared ring/sphere meshes and the unshaded-material helper.
- **`ICombatTarget`** — `TargetNode`, `ContainsPoint(p)`, `TakeHit(p)`. The
  player `Boat` implements it (hull box + `HitMargin`, red hull flash). Enemy
  boats, forts etc. implement it and `combat.AddTarget(...)`.

## Aiming and firing (player)

Each frame `CombatSystem.Update` casts the cursor through the current rig's
camera with `Terrain.RaycastGround`, so the aim point sits on the water or on
top of a hill, and parks the reticle there (`ReticlePx` screen pixels, scaled
by 1/zoom). Yellow means a loaded cannon can bear on it, grey means none
can (all reloading, or outside every firing arc, or beyond `MaxRange` when
that is > 0). A click that didn't land on a city label fires
`PlayerGuns.BestCannon(aim)`; clicking inside a UI panel never reaches the
world (`InputState` listens in `_UnhandledInput`). Firing works in both
camera modes. The reticle is hidden until the mouse has moved at all, so
scripted runs don't draw it in the corner.

Combat runs on **game time** (`gameDt`, the same fast-forward factor as the
boat), so reloads and flights stay consistent with sailing speed.

## Adding an enemy vessel (how it's meant to go)

1. Make its node implement `ICombatTarget` (hull box in its own frame, a
   `TakeHit` that flashes/damages) and `combat.AddTarget(it)`.
2. `var guns = combat.Arm(node, mounts, length)` gives it a battery.
3. Its AI calls `combat.Fire(guns, target)` when it wants to shoot — the
   same best-cannon/arc/reload rules as the player. `Projectiles` already
   skips the firing vessel and will hit the player `Boat`.
4. Keep it under `src/combat/` (or `src/enemies/` with its own doc) and out of
   the trading code.

`Projectiles.Shots/Hits` are the running tallies the HUD shows; the
prototype's "HIT!" banner hasn't been ported since nothing can be hit yet.

## Verifying

No tests; use the scripted flags (README): `--start` to place the boat,
`--fire=dx,dy@frame` to shoot at a ground point relative to it (only a
cannon that bears fires — `+60,+40` from a boat heading east is 34° off the
bow, outside both the bow and broadside arcs, so nothing happens), `--set`
to change knobs, `--frames` long enough for the flight (at `Gravity=30` a
110-unit shot takes ~3 s ≈ 360 frames at 120 fps). The run prints shots and
hits when it saves the screenshot.
