# The boat

Files: `src/boat/BoatConfig.cs` (knobs), `src/boat/Boat.cs` (physics,
lean, land collision, hit box), `src/boat/BoatModel.cs` (the mesh). The
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

Ported from `../game`: W/S accelerate/brake by `Accel`, coasting loses
`Friction` units/s², speed clamps to `[-MaxReverseSpeed, MaxSpeed ×
MaxSpeedScale]` (the crew multiplier, set by `Main` from
`Crew.SpeedMultiplier`). Steering authority ramps with speed up to
`TurnFullSpeed`; `TurnRateScale` is `1/timeScale` from `Main` so steering
feels the same in fast-forward. `Main` sub-steps by `MaxStepSeconds`.

`Speed` is the scalar along the heading; `GroundSpeed` is what the boat
actually moved along its heading this step (less when the shore pushed back)
— the follow camera's look-ahead uses the latter.

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

## Hit box

`Boat : ICombatTarget` — `ContainsPoint` is the hull box `[-L/2, L/2] × [0,
Height] × [-W/2, W/2]` in the vessel frame grown by `CombatConfig.HitMargin`;
`TakeHit` counts `HitsTaken` and flashes the hull. Enemy boats will do the
same; see [combat.md](combat.md).
