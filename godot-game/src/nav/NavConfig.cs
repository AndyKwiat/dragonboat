// Nav mesh knobs (src/nav/NavMeshBuilder.cs, docs/nav.md).
//
// These live here rather than in core/Config.cs because that file imports
// Godot and so cannot be compiled into tests/; the builder is pure grid code
// and stays testable. They are deliberately NOT read from BoatConfig either:
// CollisionRadiusScale is a live [Tweak] restored from tweaks.dump at every
// launch, and a slider left off its default must not quietly change the
// world's navigation. Nor are they [Tweak]s themselves — the mesh is baked
// once when the map loads, so dragging them at runtime would do nothing.
//
// The two numbers are measured, not guessed (docs/nav.md has the table):
//   CellSize 8   — 16 shatters pathingTest.terrain's river network into 62
//                  disconnected pieces; 8 keeps it a single body.
//   Clearance 2  — the radius of the collision circles Boat.CollideWithLand
//                  uses, so a boat's centre anywhere inside a node is clear
//                  of land. 4 severs pathingTest into 93 pieces.
public static class NavConfig
{
    // World units per nav cell — also the quadtree's minimum leaf size.
    public const int CellSize = 8;
    // Tiles of clear water kept between a navigable cell and the nearest land.
    public const int ClearanceCells = 2;
}
