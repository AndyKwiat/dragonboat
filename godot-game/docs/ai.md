# AI: boats that sail themselves

Files: `src/ai/*`, `src/boat/HelmOrder.cs`. Run it with
[`./battleTest.sh`](scenarios.md) (fighting) or
[`./pathingTest.sh`](scenarios.md) (passages).

**Status: it sails a passage, and it will fight you for a spot.** A boat can
be told to guard somewhere; it lies there, comes for you when you get too
close, stops at gunshot range, turns its broadside on and shoots — leading
the shot — and goes home when you leave. It cannot yet patrol, ram, or think
about what its friends are doing.

```bash
./build.sh
./battleTest.sh                                   # then sail at her
./pathingTest.sh --nav --mode=free --center=919,50 --zoom=2
```

Note `--mode=free` parks the player boat — the camera owns WASD in free mode,
so the player never moves and makes a sitting target. Use it to watch; use
follow mode to be shot at.

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

## Orders, states and tactics

Three tiers, and only the middle one is a state machine.

| tier | changes | what it is |
|---|---|---|
| **Order** | ~never | The standing assignment, set at spawn: `idle`, `voyage`, `protect`. Holds the mission's data — the station it guards, the destination it is bound for, how far it will chase — and every tick decides what the boat should be doing. |
| **Tactic** | never | *How this boat fights.* The order hands over to it whenever there is a target, and never tells it how. `StandOff` today. |
| **State** | seconds | What the boat is doing right now: `idle`, `sailing`, `holding`, `pursuing`, `engaging`. A plain enum, because the transitions live in the orders and tactics — there is nothing to hang on it. |

Plus **targeting**, one pass over every contact, run before any of it.

```csharp
var target = Targeting.Pick(...);                // hysteresis, sees the whole fleet
var goal   = AiPilot.Decide(situation, memory, order);   // order, or its tactic
var helm   = AiSteering.For(goal, situation);    // and that is the frame
```

**Why not a hierarchical state machine.** Nesting to arbitrary depth, with
entry/exit actions and a transition table, buys flexibility we do not need
and costs the thing we do: being able to answer "why is it doing that" by
reading one method. Why not a behaviour tree: the same leaves, more
machinery, and its real payoff — composing behaviour from data — is not on
the table for three missions. Why not one flat set of states: because
`protect` and `patrol` would each need their own pursuing, bearing and
returning, and every tactic again inside each. The split is what stops that.

**Where the line between an order and a tactic falls.** By what each one
*owns*, not by which runs first. The order owns whether there is a fight —
which contacts count, how long to keep one, how far it may be dragged from
its station, and what to do with nobody to fight. The tactic owns the boat
while a target exists, and never says "go home". That is why a rammer that
has overshot and a gunner opening the range need no new states and never
touch the order: both are just the tactic asking for a different point.

### Everything reduces to a point

A state does not produce helm orders. It produces an `AiGoal` — a point to
make for, whether to keep way on, and whether the guns are free — and
`AiSteering` turns that into a `HelmOrder` in a dozen lines. Holding station
is "make for the station, stopped". Presenting a broadside is "make for a
point abeam of the enemy, stopped". That is why every rule in here is
testable as plain geometry, with no game running.

### Adding to it

| you want | you write |
|---|---|
| patrol a route | a `PatrolOrder` — same targeting, sailing a route instead of holding |
| a journey that does something on arrival | an arrival action on `VoyageOrder` |
| a boat that rams | a `RamTactic` beside `StandOffTactic` |
| teammates not doubling up | a filter inside `Targeting.Pick`, which already sees the fleet |
| a commander handing out orders | an assignment to `boat.Order` — orders are data |

## Keeping out of each other's way

Files: `src/ai/Traffic.cs`.

Three boats with the same orders want the same piece of water: they all close
on the same enemy, so they all steer for the same point off her beam and end
up in a heap. Hulls collide now ([boat.md](boat.md)), so they no longer
overlap — but shoving each other aside every frame is not station keeping,
and it looks like what it is.

So before a boat commits to somewhere to **be**, it checks whether that water
is spoken for. Boats decide in a fixed order and each one **claims** its
goal, so the second boat avoids the first's *chosen* spot rather than its
current one — which is what stops two of them converging on a place neither
has reached yet.

Given a pivot — the enemy they are all working — a blocked boat slides
**around** it rather than backing off, keeping the range its tactic asked
for. Three boats end up spread on an arc at fighting range instead of in a
queue. With nothing to circle, it simply stops short of the obstruction.

It applies only to a goal that is a *place*. A boat that is stopped and
turning is pointing itself, not going anywhere, and must not be nudged off
the bearing it has just worked for — and for the same reason she claims her
own position then, not the bearing hint sixty units off her bow, or her
consorts would give way to a phantom and park alongside her instead.

**Being in range is not enough to stop.** A boat also needs *room*: without
that, she settles the instant the range is right, wherever she happens to
be, and never reaches the berth Traffic picked for her. Two consorts then sit
on the same bearing shooting through each other — and widening the spacing
makes it *worse*, because the berth she abandons is further away. So the
tactic keeps working until `ConsortRange` clears `AiSpacing`, with a little
hysteresis so a settled boat does not shuffle at the first nudge.

`AiSpacing` (36) is how much water a boat wants to itself. The binding
constraint is not the hulls — two of them reach only 8 units each — but the
**line of fire**: consorts a third of a boat-length apart on one bearing
shoot through each other. Measured on the battle bench, 36 holds three boats
44 units apart at the closest, with nothing within 9 units of anyone's line.

## Sides

One `int Team` on every hull. Two vessels are enemies when their teams
differ, and that is the whole of the game's diplomacy. The player is team 0,
so a scenario boat defaults to 1 and starts out hostile; `boats: ai0:protect@2`
puts one somewhere else.

Worth knowing: a cannonball excludes only the hull that fired it, so two
boats on the same side **can** shoot each other. Harmless with one AI boat
afloat; something to decide about when there is a squadron.

## Guarding a spot

`protect` is the first order, and the one the numbers were chosen for.

```
holding ──enemy inside the engage ring──► pursuing ──in range──► engaging
   ▲                                          ▲                     │
   │                                     drifts out                 │
 arrived ◄── sailing ◄── enemy leaves the disengage ring ◄───────────┘
```

**The radii are measured from the station, not from the boat.** This is the
one thing in here that would be a bug if it were the obvious way round. A
boat measuring from herself would be chasing the very thing that decides
whether to chase: closing makes the acquire test pass harder, breaking off
makes it fail, so she locks on, charges, drops, drifts back, and locks on
again. The station cannot move, so the decision is stable whatever she does.
`AiEngageMinSec` covers the other direction — a target loitering exactly on
the disengage ring cannot flicker either.

**She stops to shoot.** Holding a beam at constant range while under way is
an orbit: it needs a continuous turn at speed÷range, about 25°/s at a hundred
units, and that is where "can never satisfy both range and bearing" comes
from. But under oars a boat stops from full speed in well under a second and
pivots on the spot at about 50°/s, so a beam is less than two seconds away —
less than one reload. Stop, pivot, hold, fire.

**There is no "firing" state.** Whether a round goes is decided every frame
by the battery — the best *loaded* cannon that *bears* and can *reach* — and
never by the state machine. So the bearing windows below only choose which
word the report prints; state chatter cannot cost a shot, which is why the
hysteresis can be loose and readable instead of exact.

**The blind arc is real.** The guns sit at 0° and ±90° with a 60°-wide arc
each, so a broadside bears between 60° and 120° off the bow and **nothing at
all bears between 120° and 180°**. A boat turning to the far beam would sweep
her target *through* that blind spot rather than out of it, so she always
turns to the beam the target is already on. It also means a boat running away
is safe, which is why a stand-off gunner must never present her stern.

### The knobs

All `[Tweak]`s in `src/ai/AiConfig.cs`, group "AI": press **T** and drag them
while the fight happens, or `--set=AiFightRange=90` on a scripted run. The
gunnery facts they are derived from: max range `MuzzleSpeed²/Gravity` = 163
units, reload 2 s a gun, three guns a broadside and one in the bow.

| knob | start | why |
|---|---|---|
| `AiEngageRadius` | 220 | more than gun range, so she starts moving before she can shoot |
| `AiDisengageRadius` | 380 | must be **larger** than engage, or she drops a target and instantly re-acquires it |
| `AiEngageMinSec` | 4 | committed for this long whatever the target does |
| `AiLeashRadius` | 300 | a fast target cannot tow a guard off the thing it guards |
| `AiFightRange` | 110 | comfortably inside 163, with room for the target to open the range mid-volley |
| `AiFightFar` | 140 | beyond this she closes again |
| `AiMinRange` | 30 | inside this she opens the range rather than drifting into a ram |
| `AiBeamEnterDeg` / `AiBeamHoldDeg` | 18 / 27 | enter the fight well inside the broadside arc, leave a little outside it |
| `AiFireDwellSec` | 2.5 | longer than a reload, so a volley finishes |
| `AiStopSpeed` | 6 | back the oars above this while engaging |
| `AiLeadFraction` | 0.85 | see below |

## Leading the shot

Nothing else in the game leads: the moored fleet fires at where the player
*is*, which is why it feels accurate against a boat lying still and misses
everything under way. At a hundred units a ball is a second and a half in the
air and a rowed boat covers sixty-five units in that time — five hull lengths.

`Gunnery.Solve` iterates the obvious fixed point — aim ahead, which opens the
range, which lengthens the flight, which lengthens the lead — three times.
`AiLeadFraction` scales the whole correction, and **0 reproduces the moored
fleet's naive aim exactly**, which is the control that proves the rest of it
does anything.

Two honest limits. It predicts a *straight line*, so a boat that turns while
the ball is in the air dodges it — as it should. And a target fast enough to
outrun the ball has no exact solution at all: every unit of lead buys it more
time. Three passes then under-lead rather than running away to the horizon,
which is the right way to be wrong, because that shot was never going to land.

## Making a passage

A `voyage` order is three small pieces, none of which knows about the others:

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
3. **The passage.** `VoyageOrder` drops every waypoint already astern — a
   fast boat can pass more than one in a frame, and the first is its own
   starting point — steers for the next, and on the last one strikes the oars
   and goes back to `idle`.

AI boats sail **under oars**, sails struck. Under canvas the wind would shove
them about before they had any say in where they went, and the oars answer
the helm at any speed — including turning on the spot, which is what a river
bend needs. Teaching the AI to sail is a later state, not a flag on this one.

**What it does not do.** A voyage plans once, at spawn, and never re-plans:
nothing on these maps moves the land, but a boat knocked off its route by a
current would follow it back rather than think again. (A boat *chasing*
something does re-plan, every `AiRepathSec`, because the target moves.) It stops a boat length short of
its destination, by design (`Helmsman.ArriveRadius`) — aiming finer just
makes a boat circle a spot it has already reached. And it steers between
waypoints without looking at the shore: the route is clear of land by
construction, but the boat's own turning circle is not part of that promise,
so a hull can still brush a bank on a tight bend. `Boat.CollideWithLand`
catches it, exactly as it does for the player.

**Seeing it.** **N** draws the nav mesh and, on top of it, each boat's
remaining route as a yellow line with a tick at every waypoint and an orange
cross on the leg it is steering for right now (`RouteOverlay`, rebuilt every
frame while it is up). A scripted run's last line carries the whole state
without pixels — order and state as separate words, then the target, the
guns and the damage:

```
protect/holding@(355.8, 198.5) -171° 0.0 tgt none guns 7/7 shots 0 taken 0
protect/pursuing@(332.5, 183.1) 158° 34.7 tgt 0 r 168 brg +16 blind guns 7/7 shots 0 taken 0
protect/engaging@(178.0, 173.3) 103° 0.0 tgt 0 r 111 brg +86 los guns 4/7 shots 10 taken 0
voyage/idle@(396.8, 527.7) 90° 0.0 tgt none taken 0 arrived (22 legs)
```

`guns 4/7` frozen at `0/7` would be the "forgot to tick the battery" bug
showing itself, and `shots` is **this boat's** count: the world's tally in
`Projectiles` stopped meaning "the player" the moment an AI boat opened fire,
so every battery keeps its own.

**Do not tune or judge a fight under `--hold=Shift`.** Main gives the *player*
`TurnRateScale = 1/timeScale` so the helm feels constant per real second; an
AI boat has no such correction, so fast-forward makes it turn several times
harder than you do.

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

Worth knowing when you try it by hand: a boat under way is doing 42 units a
second and a shell is most of a second in the air, so clicking *on* one lands
the ball where she was. You have to lead her, exactly as she leads you.

## The world's fleet

The game stocks itself with `AiConfig.FleetCount` (20) of these, scattered on
coastal water by `AiBoats.Scatter` — never in the deep ocean, never within
`MinSpawnDistance` of where the player starts. Each one is given **protect**
orders over the spot she appears on, so the world is a coast full of guards
rather than a shooting gallery: sail near one and she comes for you.

There used to be a second, dumber kind — moored hulls that never moved and
only fired. That class is gone. **There is one kind of AI boat now**, and the
fleet in the world is the same code as the boats on the battle bench, which
means the bench is a real test of the game rather than a thing beside it.

A scenario turns the fleet off with `enemyFleet: false`, which every bench
does, so the only boats afloat are the ones it lists ([scenarios.md](scenarios.md)).

### Sinking

Three rounds (`AiHitsToSink`) and a boat goes down: she stops being a target,
her hull and shadow go, a splash rings where she was, and the kill pays
`KillHonour` (50).

**The player cannot be sunk at all.** `Boat.TakeHit` counts the hit and
flashes the hull and does nothing else; only `AiBoat` overrides it. That
asymmetry is deliberate for now, and it is also the assertion channel — a
run's `taken N` is the AI's marksmanship, measured on a hull that survives to
report it.

In the **world**, a wreck comes back: the fleet never shrinks below its
count, and a replacement appears outside the ground the camera can see, so
nobody watches a hull pop into existence. In a **scenario** nothing respawns
— a bench with a shifting cast would not be a bench.

A note on the hit that matters: `Boat.TakeHit` is `virtual` precisely because
`Projectiles` calls it through `ICombatTarget`. A `new` method on `AiBoat`
would be silently skipped and the boat would never notice it had been hit.
