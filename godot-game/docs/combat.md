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
  with velocity from `SolveLaunch`: every ball leaves at **`MuzzleSpeed`**;
  the *low* launch angle that lands on the target is solved from
  `tan θ = (v² − √(v⁴ − g(g d² + 2 h v²))) / (g d)`, so farther shots arc
  higher and take longer. Range is capped by physics (≈ `MuzzleSpeed²/Gravity`
  on flat water): beyond it the discriminant goes negative and the ball is
  fired at the range-maximising angle and falls short — `WithinRange` is the
  UI's check. The angle is floored at `MinAngleDeg`, so a very close target
  is overshot: the ball sails over it. Muzzle-flash burst; sets the cannon's
  reload. `Update` integrates
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
by 1/zoom). Yellow means a loaded cannon can bear on it *and* the point is within the
fixed muzzle speed's reach from that cannon; grey means it can't be hit (all
reloading, outside every firing arc, or out of range). A click that didn't land on a city label fires
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
to change knobs, `--frames` long enough for the flight (a near-max-range shot
at the defaults is ~2.5 s of game time ≈ 300 frames at 120 fps, fewer when
zoomed-out fast-forward is in effect) — `--set=SplashLife=3` keeps the splash
around long enough to see where it landed. The run prints shots and hits when
it saves the screenshot. Useful checks: `--fire=150,0` lands on target,
`--fire=250,0` falls short at max range, `--fire=10,0` sails over.
