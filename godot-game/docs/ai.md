# AI: boats that sail themselves

Files: `src/ai/AiState.cs`, `src/ai/AiBoat.cs`, `src/ai/AiBoats.cs`,
`src/ai/Helmsman.cs`, `src/ai/RouteOverlay.cs`, `src/boat/HelmOrder.cs`;
run it with [`./pathingTest.sh`](scenarios.md).

**Status: it sails a passage.** An AI boat can be given a destination and
will find its own way there across the water, round every headland, and lie
to when it arrives. What it does not do yet is anything *about* anything —
no chasing, no fleeing, no trading, no re-planning when the world changes.

```bash
./build.sh
./pathingTest.sh --nav --mode=free --center=919,50 --zoom=2
```

Free cam because the AI boats start at markers `p1`/`p2`, well away from the
player at `p0`; pan with WASD or by dragging, wheel to zoom, **N** toggles the
mesh and the routes, and **Shift** fast-forwards (a passage is ~30 game
seconds). The launch console says where each boat is bound:

```
AI boat (voyage) on marker 'p1' at 919,50, bound for 'p2' by 21 leg(s), 1234 units
```

## An AI boat is a boat

`AiBoat` extends `Boat`. Same hull, same arcade physics, same land collision,
same hit box, same top speed ([boat.md](boat.md)) — whatever the AI learns to
do, it does under the rules the player sails by, and a change to the boat's
handling changes the AI's handling with it. What it adds is small:

- an `AiState`, and
- once a frame, the **helm orders** that state wants.

Both helms end in the same place. `HelmOrder` is ahead / astern / port /
starboard — the four things a keyboard could say — and `Boat.Update` takes
one. The player's orders are read off the keys, an AI boat's come out of its
state machine, and the physics step below cannot tell which it was given.
That is the whole seam, and it is why the AI cannot cheat: there is no way
for it to move a boat except by asking for the same four things.

## States

`AiState` is the dial the AI work turns. A boat is in exactly one state, the
state alone decides the helm orders, and new behaviour arrives as a **new
state** rather than as a flag threaded through an old one.

| state | |
|---|---|
| `idle` | Lies to: sails struck, no orders given, no thrust. She goes nowhere unless the water moves her. |
| `voyage` | Makes a passage to a named destination, then falls back to `idle`. |

Scenarios name the state a boat starts in, and where a state that goes
somewhere is bound — `boats: p1:voyage>p2, p3` — so a test map can hold
several boats doing different things at once. That line is the whole
interface: retasking the boats is editing a yml file, not changing code, and
it is read at launch so it needs no rebuild.

## Making a passage

`Voyage` is three small pieces, none of which knows about the others:

1. **The route.** At spawn, `NavPath.Find` runs A* over the nav mesh
   ([nav.md](nav.md)) from the boat to its destination and string-pulls the
   result straight. Searching *nodes* is why this is cheap — the open sea is
   a handful of leaves, so a crossing is dozens of hops, not thousands. A
   destination in another body of water, or too far from any of it, is not a
   silent failure: the boat says so on the console and lies to instead.
2. **The helm.** `Helmsman` turns "I am here, pointing there, and I want to
   be over *there*" into helm orders: turn the short way round, pull ahead
   unless the target is so far off the bow that rowing would only take us
   further from it, and count a waypoint reached within a boat length. It is
   pure geometry with no Godot and no boat in it, so "does it turn the short
   way round?" is a question with a test rather than an opinion.
3. **The passage.** `AiBoat.Sail` drops every waypoint already astern — a
   fast boat can pass more than one in a frame, and the first is its own
   starting point — steers for the next, and on the last one strikes the oars
   and returns to `idle`.

Boats voyage **under oars**, sails struck. Under canvas the wind would shove
them about before they had any say in where they went, and the oars answer
the helm at any speed — including turning on the spot, which is what a river
bend needs. Teaching the AI to sail is a later state, not a flag on this one.

**What it does not do.** It plans once, at spawn, and never re-plans: nothing
on these maps moves the land, but a boat knocked off its route by a current
would follow it back rather than think again. It stops a boat length short of
its destination, by design (`Helmsman.ArriveRadius`) — aiming finer just
makes a boat circle a spot it has already reached. And it steers between
waypoints without looking at the shore: the route is clear of land by
construction, but the boat's own turning circle is not part of that promise,
so a hull can still brush a bank on a tight bend. `Boat.CollideWithLand`
catches it, exactly as it does for the player.

**Seeing it.** **N** draws the nav mesh and, on top of it, each boat's
remaining route as a yellow line with a tick at every waypoint and an orange
cross on the leg it is steering for right now (`RouteOverlay`, rebuilt every
frame while it is up). A scripted run's last line carries the same thing
without pixels:

```
ai boats voyage@(862.0, 72.3) 162° 42.7 leg 1/21, 1174 to go; ...
ai boats idle@(396.7, 541.7) 90° 0.0 arrived (22 legs); ...
```

## Solid to gunfire

An AI boat is a target like any other: `Boat` implements `ICombatTarget`, so
an `AiBoat` inherits the hull box and `TakeHit` for nothing. But a cannonball
is only ever tested against the targets the combat system has been **told
about** — `Projectiles.Update` walks `CombatSystem`'s registered list and
nothing else — so `AiBoats.Spawn` has to call `combat.AddTarget`. It once did
not, and the result was the worst kind of bug: shells passed clean through
the hull and splashed in the water beyond, which is pixel-for-pixel what a
near miss looks like, while the reticle sat happily on the boat (aiming
raycasts the *ground*, not hulls). `scenarios/gunnery.yml` is the bench that
catches it coming back.

A hit flashes the hull and counts on the HUD's `shots N hits M`. It does not
sink her: AI boats have no `HitsToSink`, no wreck and no respawn, unlike the
hostile fleet. They are a test bench, and one that vanished mid-passage would
take the pathing test with it.

Worth knowing when you try it by hand: a voyaging boat is doing 42 units a
second and a shell is most of a second in the air, so clicking *on* one lands
the ball where she was. You have to lead her. The red hostile ships never
move, which is why they feel so much easier to hit.

## The legacy fleet is not this

The hostile ships (`src/enemies/EnemyShips.cs`) are
the *dumb* boats: moored hulls that never move or turn, whose entire AI is
"fire the best loaded cannon that bears on the player". They are not
`AiBoat`s, they have no `AiState`, and nothing in `src/ai/` touches them.
That separation is deliberate and is the guarantee that the game plays
exactly as it did while the new AI is under construction: **everything in
this doc only happens inside a scenario**, and a normal launch spawns no AI
boats at all (`AiBoats.Update` walks an empty list).

When the pathing states are proven, giving the hostile fleet one is a
question worth asking — but it is a separate change, made deliberately, not
a side effect of this work.
