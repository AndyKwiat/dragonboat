# The boat

Files: `src/boat/BoatConfig.cs` (knobs), `src/boat/Boat.cs` (physics,
lean, land collision, hit box), `src/boat/Hulls.cs` +
`src/boat/BoatCollisions.cs` (hull against hull), `src/boat/BoatModel.cs` (the mesh),
`src/boat/Sailing.cs` (the Godot-free wind and crew-stamina rules). The
boat is shared ground: trading cares about its speed (crew multiplier) and
position (cities); combat mounts guns on it and shoots at it. Neither side
should need to read the other's doc.

## Frame and model

`Boat` is a `Node3D` at `WorldToGodot(X, Y, bob)` rotated `(Roll, -Heading,
0)` — Godot's YXZ Euler order yaws about +Y first, then rolls about the
boat's own forward axis. **Vessel frame: forward +X, up +Y, starboard +Z.**
Children (the model, `combat/CannonBattery`) ride along and lean with it.

`BoatModel` is the combat prototype's boat rebuilt from primitives: a 12×2×4
box hull sitting on the water, a 45°-rotated box as the bow wedge, two
0.3×8 masts at ±2.5 along the hull, each with a triangular sail hanging aft
(double-sided). Everything is laid out on a 12-unit hull and scaled by
`BoatConfig.Length / 12`, so changing `Length` resizes the whole boat (the
gun mounts scale the same way). Materials are lit (`StandardMaterial3D`,
not unshaded) so sails and hull sides read against the flat terrain; the
light lives in `Main`. `SetHitFlash` drives the red emissive glow after a
cannonball hit.

## Handling (`Boat.Update`)

The boat is a point mass (mass 1, so every force is an acceleration in world
units/s²): `MaxForce` pushes along the heading — held W at the oars, always
under canvas — S pushes `BrakeForce` astern, the water drags with a linear + quadratic term split into the keel
and lateral axes (`LateralDragScale`× abeam), and the current
([world.md](world.md)) and the wind add their own. Top speed is whatever the
drag allows — `TerminalSpeed(sailsUp)` solves it. Steering authority ramps
with water speed over the hull up to `TurnFullSpeed`, so a boat merely
drifting on a current can't steer; `TurnRateScale` is `1/timeScale` from
`Main` so the helm feels the same in fast-forward, and `ThrustScale` is the
crew multiplier. `Main` sub-steps by `MaxStepSeconds`.

`Speed` is the scalar along the heading; `GroundSpeed` is what the boat
actually moved along its heading this step (less when the shore pushed back)
— the follow camera's look-ahead uses the latter.

## Sails up or down (SPACE)

`Boat.SailsUp` (toggled by SPACE in `Main`, `SetSails` on the boat) picks
between two ways of moving, and the model raises or strikes the canvas to
match — struck sails simply vanish, leaving the bare masts.

|  | sails up | sails down (oars) |
|---|---|---|
| thrust | always drawing; W is a no-op, S takes the way off | only while W (or S astern) is held |
| what powers it | the wind (`Wind.Drive`) — a calm goes nowhere | the crew, always at full pull |
| thrust scale | `× SailThrustScale` (0.7) | `× OarThrustScale` (1.6) |
| drag | `× SailDragScale` (0.45) | `× OarDragScale` (2) |
| rudder | `× SailTurnScale` (0.6) | `× OarTurnScale` (1) |
| wind | felt | ignored |
| crew | resting | tiring while a helm key is held |

With the defaults that is ~67 u/s under canvas against ~43 at the oars, and
an oared boat that gathers way more than twice as fast and turns markedly
harder. All six are `[Tweak]`s in the "Sailing" group.

### What the sails are worth

The rig's pull is the wind's to give: the forward thrust is scaled by
`Wind.Drive`, a deliberately generous ramp — `SailWindKneeDrive` (0.6) of
full pull by `SailWindKneeFrac` (0.15) of `SailFullWind` (30), then easing
on to 1 at a full wind, and no more above it. So 15% of a full wind already
makes ~50 u/s downwind and ~44 on a beam reach, both ahead of the oars' 43,
while a flat calm leaves the boat sitting there until the oars come out —
which is the whole point of carrying them. Measured downwind at 400 frames:
0 wind → becalmed, 1 → 15 u/s, 3 → 36, 4.5 (the knee) → 50, 10 → 60, 22 →
80, 30 → 91. Beating into a 22 still makes 49.

Note that this is the *thrust along the heading*; the wind's own push on the
hull (above) is separate and uncapped, which is why a gale still adds speed
past `SailFullWind`. The HUD's `drive N%` line is the ramp's output, and the
dial's arrow fills out on the same scale.

A boat left alone under canvas therefore keeps sailing — and keeps burning
the calendar, since `Main` advances game time whenever the hull is moving.
Strike the sails (or hold S) to stop. Oars also steer with no way on: a rudder needs water flowing past it, but a
crew can back one side and pull the other, so under oars the authority never
drops below `OarStandingTurn` (0.5) of full and the boat turns bow-first even
dead in the water. Under canvas it still has to be moving. Verify with
`--start=5330,2763,180 --sails=down --hold=D --frames=200`: heading swings
180° → 273° on the spot.

### Wind

`Wind` (`src/boat/Sailing.cs`, Godot-free and unit-tested) is one force
blowing **towards** `BearingDeg` (0 = east/+x, 90 = south/+y, 180 = west),
wandering `WindSwingDeg` either side of `WindBearingDeg` as a sine of game
hours over `WindSwingHours` — deterministic, so a `--seed` run repeats.
`Main` owns the instance, feeds it game hours and re-reads the tweaks each
frame; only a boat under canvas is given it.

What the boat catches depends on where it points: `SailFactor` is
`cos(bearing − heading)` running downwind (1 dead downwind, 0 on a beam
reach) and `WindBeatingScale ×` that when beating into it. The force always
acts **along the wind's own direction**, so a quartering wind sets the boat
sideways and a headwind shoves it astern. Measured, sailing due west with a
22-force wind: 85 u/s downwind, 63 on a beam reach, 55 beating.

### Crew stamina

`CrewStamina` (same file) is the rowers: `Level` 1 → 0 over
`RowHoursToEmpty` game hours (72 ≈ 3 game days) of unbroken rowing, back to
full over `RestHoursToFull` (36). Rowing means **sails down and a helm key
held**; sailing, or oars shipped with no order given, rests them. Running
out costs nothing yet — the bar (`ui/StaminaBar.cs`, top centre) just sits
at 0 and flashes. The wind gets its own arrow bottom left
(`ui/WindDial.cs`) — both are in [ui.md](ui.md). `--sails=up|down` and `--stamina=0..1` set the state for a
scripted run, and the run's final line prints both.

## Lean and bob

The prototype's roll: `Roll` eases (time constant `LeanSmoothSec`, 0 =
instant) towards `-turn × LeanAngle × speedFrac`, i.e. the boat heels
**outward** from the turn, more at speed. The hull also bobs
`BobAmplitude × sin(BobRadPerSec × t)` vertically. All four are `[Tweak]`s in
the "Boat lean" group.

## Shadow

A fake blob shadow (`world/BlobShadow.cs`): a dark ellipse of the hull's
footprint × `ShadowScale`, flat on the water at the boat's world position —
a *sibling* node, not a child, so it stays flat while the hull rolls and
bobs. `ShadowScale` must stay comfortably above 1: at the isometric angle
the hull covers a same-sized ellipse completely. Raised shoreline cells
simply occlude whatever overhangs them. `ShadowStrength`/`ShadowScale` are
`[Tweak]`s ("Shadows" group).

## Land collision

The boat cannot enter non-water cells (anything but ocean/river/canal —
see [world.md](world.md)). After each physics step `CollideWithLand` models
the hull as three circles of radius `Width/2 × CollisionRadiusScale` at
−L/3, 0, +L/3 along the heading and runs each through
`Terrain.ResolveCircle`, moving the whole boat by the push. Because the push
only removes the motion *into* the shore, the motion *along* it survives —
the boat slides along a beach rather than stopping dead, zig-zagging along
the stair-stepped cell edges of a diagonal coast. While in contact, speed
decays by `CollisionDrag × into` per second where `into` is how squarely the
bow is moving into the land (1 = head-on, 0 = grazing): a head-on hit dies in
a fraction of a second and parks the bow exactly at the cell edge; a glancing
one carries on. Holding W against a wall settles at `Accel / (into ×
CollisionDrag)`; the boat doesn't move but `GroundSpeed` is 0 so the camera
stays put.

Verify with `--start=3300,15829,0 --hold=W --frames=700` (head-on into the
beach east of the start: stops at x≈3474) and `--start=3300,15700,40
--hold=W --frames=900` (slides south-east along it).

## Hull against hull

Files: `src/boat/Hulls.cs` (the geometry, Godot-free and tested),
`src/boat/BoatCollisions.cs` (applying it), run from `Main._Process` after
every boat has moved.

Boats do not pass through each other. The shape is **the same three circles
the shore is resolved against** — no second notion of where a hull is — and
the resolution is the same too: overlapping hulls are parted along the
deepest contact, the part of their closing speed driving them together is
taken out, and the part along the contact survives. So a boat brought
alongside another **slides down her side** instead of stopping dead, exactly
as she does against a beach.

**Size is per hull, not per game.** `Boat.HullLength` and `Boat.HullWidth`
are fields that default to `BoatConfig`'s one vessel, and everything that
measures a boat — the model, the shadow, the hit box, the collision shell —
reads them rather than the constants. `Mass` is deck area, and it decides who
gives way: a launch shoved by a merchantman moves most of the way, two of a
kind share it evenly. That is the whole of what a second class of vessel
needs; nothing in the collision code knows how big a boat is supposed to be.

| knob | | |
|---|---|---|
| `HullBounce` | 0.25 | bouncier than the shore, so boats fend each other off rather than grinding along together |
| `HullSlack` | 0.3 | a little give, so two hulls can lie alongside without shoving each other every frame |
| `RamSpeed` | 12 | closing faster than this counts as a ram rather than a nudge |

**The ramming hook.** `BoatCollisions.Rammed(a, b, closingSpeed, where)` fires
once per pair per frame, and only above `RamSpeed`. Nothing is subscribed to
it — a ram currently flashes both hulls (`Boat.Jolt`) and costs nothing else.
That is where damage goes when you want it. A scripted run's last line counts
them as `rams N`, so it can be asserted on.

Every hull afloat is in the pass — the player and every AI boat, in the world
as much as on a bench. One thing it deliberately does not do: a very fast hull
could in principle pass through a thin one between frames, which no boat in
the game is near doing.

## Hit box

`Boat : ICombatTarget` — `ContainsPoint` is the hull box `[-L/2, L/2] × [0,
Height] × [-W/2, W/2]` in the vessel frame grown by `CombatConfig.HitMargin`;
`TakeHit` counts `HitsTaken` and flashes the hull. Enemy boats will do the
same; see [combat.md](combat.md).
