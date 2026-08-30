# Nav mesh: where a boat can go

Files: `src/nav/NavConfig.cs`, `src/nav/NavMesh.cs`, `src/nav/NavMeshBuilder.cs`
(all Godot-free and in `dotnet test tests`), `src/nav/NavMeshOverlay.cs`.
Look at it with **N**, or `--nav` at launch.

The mesh is baked when a map loads. `NavPath.Find` searches it with A* and
straightens the result, which is how an AI boat gets from one side of the map
to the other ([ai.md](ai.md)); **N** draws both the mesh and the routes.
Nothing else reads it, and no player-facing behaviour depends on it.

The problem it solves: the game knows only "is this one tile water?", and
`map1.terrain` has **26 million** water tiles. Nothing can search that. The
mesh turns it into ~19,000 squares.

## The four passes

All run once, in `NavMeshBuilder.Build`, in about 100 ms on the 8192² map.

**1. Navigable nav cells.** The world is cut into cells of `CellSize` = 8
world units. A cell is navigable iff **every** tile in its block expanded by
`Clearance` = 2 tiles on all sides is water.

Expanding the block is exactly equivalent to "every tile in the block is at
least 2 tiles from land" — the Chebyshev dilation of a solid box by K *is*
that box grown by K — so this needs no distance transform and no map-sized
intermediate buffer, just one scan per cell that bails on the first non-water
tile.

**Out of range counts as land**, not water. `TerrainMap.TerrainAt` answers
water off the map, which would mark the whole border navigable and invite a
pathfinder to sail off the edge of the world. `CityPlacer` had to work around
the same thing.

**2. Water bodies.** A 4-connected flood fill labels each navigable cell with
the body of water it belongs to. Four-connected, not eight: a boat cannot
slip through a diagonal gap. Two nodes in different bodies are unreachable
from each other however close they look, which a search gets as an O(1) test
instead of a failure after the fact. Nothing is discarded — deleting
"puddles" would be policy, and would make the overlay lie about where the
water is; `NavMesh.BodySize` is there for a caller that wants to threshold.

**3. The quadtree.** The largest power-of-two-aligned square of nav cells
that is wholly navigable becomes a node. No same-body test is needed: a solid
square is internally 4-connected, so it is always one body — which is true
precisely because pass 2 discards nothing.

**4. Adjacency.** Each node's border is walked and the node across each edge
looked up, into CSR arrays (`NavMesh.NeighboursOf`). Deduping is a
consecutive-run check rather than a set: along one edge equal ids are always
consecutive, and one leaf cannot touch another on two different sides — a
square holding a cell to the left of A *and* a cell above A would have to hold
A itself. Allocation-free, and so deterministic.

## What it looks like

| | `map1.terrain` 8192² | `pathingTest.terrain` 1024² |
|---|---|---|
| water | 38.9% (26.1M tiles) | 20.9% (219k tiles) |
| navigable nav cells | 394,972 | 2,193 |
| **water bodies** | **1** | **1** |
| nodes | 18,745 | 1,155 |
| bake | ~97 ms | ~2 ms |

The open sea in the south of `map1` collapses into a handful of enormous
leaves; every river and coast stays fine-grained down to single cells. That
is the whole point of the quadtree, and it is visible at a glance in the
overlay.

## What it promises, and what it does not

- **The clearance is half-BEAM, not hull.** 2 units is the radius of the
  collision circles `Boat.CollideWithLand` uses, so a boat's *centre*
  anywhere inside a node is clear of land. But the hull is three such circles
  at ±`Length/3` and reaches **6 units** from the centre, so a bow can still
  find a rock at the wrong heading. Guaranteeing otherwise needs clearance 6,
  and clearance 4 already severs the `pathingTest` river network into 93
  bodies — it is not on offer. **Steering still owns collision.**
- **Crossing between neighbours is safe.** Two adjacent nodes are each wholly
  clear, so their union is clear across the shared edge: hopping node to node
  through edge midpoints never clips a corner. Any straight segment inside a
  leaf is collision-free by construction, which is what will let a path across
  open water be a straight line rather than a staircase.
- **The cliff is alignment, not width.** Because a node must be *wholly*
  navigable, a channel needs up to `2(CellSize + Clearance)` tiles of width to
  be guaranteed a lane, but as few as `2·Clearance + 1` if it happens to land
  on the grid. That is why `CellSize` is 8: **at 16 the `pathingTest` rivers
  shatter into 62 disconnected pieces.** Eight is the largest value that keeps
  both maps a single body, which is one step of margin — so
  `NavMeshTests.ThePathingMapIsOneConnectedBody` asserts it, the load line
  prints the body count, and dropping to 4 is a one-constant change.
- **Cities and quays are holes.** `CityPlacer` stamps City tiles into the map,
  so a city footprint plus its clearance is not navigable. Docking happens
  inside that hole (`Config.CityDockContactMargin`): the mesh gets a boat to
  the roads, the last few units to the quay belong to whatever is steering.
- Because cities are stamped into the tiles, **the mesh must be built after
  `CityPlacer.Place`** — a city can close a creek.
  (`NavMeshTests.CityTilesBlockTheNavMesh` is that rule, written down.)

## The knobs

`src/nav/NavConfig.cs`: `CellSize` = 8, `ClearanceCells` = 2. They live there
rather than in `core/Config.cs` because that file imports Godot and so cannot
be compiled into `tests/`. They are deliberately not read from `BoatConfig`
either — `CollisionRadiusScale` is a live `[Tweak]` restored from
`tweaks.dump` at every launch, and a slider left off its default must not
quietly change the world's navigation. They are not `[Tweak]`s themselves
because the mesh is baked once at load, so dragging one would do nothing.

## Finding a way across it

`NavPath.Find(nav, fromX, fromY, toX, toY)` returns a `NavRoute` — the world
points to steer through — or null when there is no way at all.

A* over the **nodes**, with straight-line distance as both cost and
heuristic, so the result is a shortest path through the graph. Searching
nodes rather than cells is the whole reason the mesh exists: crossing `map1`
is dozens of hops instead of hundreds of thousands. The raw result is a chain
of **portal midpoints**, the middle of each shared edge, which is safe but
zigzags — it visits the middle of every edge whether the turn was needed or
not. A **string-pull** then drops every waypoint the boat can see straight
past, which is what turns a staircase across open water into a line. Line of
sight is sampled at a quarter of a cell and refuses to cut a diagonal corner,
so a route never squeezes through a gap no boat can use.

Both ends are snapped onto the mesh (`NearestNode`), because boats sit where
the mesh is not. A destination that is *on* land or up a creek too tight for
the mesh gets a route to the water beside it, with `ReachesDestination`
false, rather than a route that pretends.

## What else a caller gets

`NodeAtWorld` / `NodeAt` (−1 off the mesh), `NeighboursOf` (a CSR span),
`WorldRect` / `Centre`, `BodyAtWorld` / `SameBody` / `BodySize`, and
`NearestNode(x, y, maxRadius)` — a bounded ring search for a way *onto* the
mesh, which matters because boats habitually sit where the mesh is not: the
clearance keeps nodes a couple of units off every shore, and a scenario
marker or a city quay may be on no node at all.

## The overlay (N)

One translucent quad per node, inset half a unit so the gaps draw the node
boundaries, plus an outline pass — at `--zoom=0.14` a node is about a pixel
and the fills alias into mush, while 1 px lines stay legible.

**Colour is node size**, ramping blue (1×1) to red (64×64 and up), because
that is what makes the quadtree readable. Body id only shifts the hue when a
map actually has more than one body — both real maps are a single body, so
colouring by body alone would paint the world one flat colour and show
nothing.

The mesh is built lazily on the first press, so a run that never presses N
pays nothing; expect a one-frame hitch when it does. It draws at y = 0.12/0.13
— above the water plane, boat shadow and current art, below the lowest land
column top (0.3), so land still occludes it correctly.

```bash
./pathingTest.sh --nav --mode=free --center=300,300 --zoom=4      # the quadtree in a river
./run.sh -- --nav --seed=7 --mode=free --center=4096,7000 --zoom=0.25   # the open sea as huge leaves
```
