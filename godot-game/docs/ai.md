# AI: boats that sail themselves

Files: `src/ai/AiState.cs`, `src/ai/AiBoat.cs`, `src/ai/AiBoats.cs`,
`src/boat/HelmOrder.cs`; run it with [`./pathingTest.sh`](scenarios.md).

**Status: the bench, not the brain.** The scaffolding is in — AI boats exist,
they are real boats, they have a state, and there is a scenario that spawns
them. The one state they have is `Idle`, which does nothing. Pathing comes
next.

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
| `idle` | Lies to: sails struck, no orders given, no thrust. She goes nowhere unless the water moves her. The baseline every pathing state is measured against. |

Scenarios name the state a boat starts in (`boats: p1:idle`), so a test map
can hold several boats doing different things at once.

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
