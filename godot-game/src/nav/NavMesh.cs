using System;
using System.Collections.Generic;

// One node of the nav mesh: a square block of nav cells that is entirely
// navigable water. Coordinates and Size are in NAV CELLS, not world units —
// multiply by CellSize for the world (WorldRect/Centre on NavMesh do it).
// Size is always a power of two and the node is aligned to its own size
// (X % Size == 0), because nodes come out of a quadtree.
public readonly struct NavNode
{
    public int X { get; }
    public int Y { get; }
    public int Size { get; }
    // Which connected body of water this node belongs to. Nodes in different
    // bodies cannot be sailed between, however close they look.
    public int Body { get; }

    public NavNode(int x, int y, int size, int body)
    {
        X = x; Y = y; Size = size; Body = body;
    }

    public bool Contains(int cx, int cy) => cx >= X && cy >= Y && cx < X + Size && cy < Y + Size;
}

// A navigable partition of the water, baked from the tile grid when a map
// loads (NavMeshBuilder). Nothing steers by it yet — the N key draws it and
// that is all (docs/nav.md).
public sealed class NavMesh
{
    public int CellSize { get; }
    public int ClearanceCells { get; }
    public int CellsX { get; }
    public int CellsY { get; }
    public IReadOnlyList<NavNode> Nodes => nodes;
    public int BodyCount { get; }
    public int NavigableCells { get; }

    private readonly NavNode[] nodes;
    // Nav cell -> owning node index, or -1. This is what makes a point lookup
    // O(1) instead of a quadtree descent, and it is needed to build the
    // adjacency anyway, so it is kept rather than thrown away.
    private readonly int[] cellToNode;
    // Neighbours in CSR form: node i's are neighbours[start[i] .. start[i+1]].
    private readonly int[] neighbourStart;
    private readonly int[] neighbours;
    private readonly int[] bodySizes;

    internal NavMesh(int cellSize, int clearanceCells, int cellsX, int cellsY, NavNode[] nodes,
        int[] cellToNode, int[] neighbourStart, int[] neighbours, int[] bodySizes, int navigableCells)
    {
        CellSize = cellSize; ClearanceCells = clearanceCells;
        CellsX = cellsX; CellsY = cellsY;
        this.nodes = nodes; this.cellToNode = cellToNode;
        this.neighbourStart = neighbourStart; this.neighbours = neighbours;
        this.bodySizes = bodySizes;
        BodyCount = bodySizes.Length;
        NavigableCells = navigableCells;
    }

    // The node covering a nav cell, or -1 where the water is too tight (or
    // there is no water at all).
    public int NodeAt(int cx, int cy)
    {
        if (cx < 0 || cy < 0 || cx >= CellsX || cy >= CellsY) return -1;
        return cellToNode[cy * CellsX + cx];
    }

    public int NodeAtWorld(float wx, float wy) =>
        NodeAt((int)MathF.Floor(wx / CellSize), (int)MathF.Floor(wy / CellSize));

    public int BodyAtWorld(float wx, float wy)
    {
        int n = NodeAtWorld(wx, wy);
        return n < 0 ? -1 : nodes[n].Body;
    }

    // Could a boat sail from one point to the other at all? Two nodes in
    // different bodies are unreachable, which is worth knowing before a
    // search rather than after one fails.
    public bool SameBody(float ax, float ay, float bx, float by)
    {
        int a = BodyAtWorld(ax, ay);
        return a >= 0 && a == BodyAtWorld(bx, by);
    }

    public int BodySize(int body) => body >= 0 && body < bodySizes.Length ? bodySizes[body] : 0;

    public ReadOnlySpan<int> NeighboursOf(int node) =>
        node < 0 || node >= nodes.Length
            ? ReadOnlySpan<int>.Empty
            : new ReadOnlySpan<int>(neighbours, neighbourStart[node], neighbourStart[node + 1] - neighbourStart[node]);

    public void WorldRect(int node, out float x0, out float y0, out float x1, out float y1)
    {
        var n = nodes[node];
        x0 = n.X * CellSize; y0 = n.Y * CellSize;
        x1 = x0 + n.Size * CellSize; y1 = y0 + n.Size * CellSize;
    }

    public void Centre(int node, out float x, out float y)
    {
        var n = nodes[node];
        x = (n.X + n.Size / 2f) * CellSize;
        y = (n.Y + n.Size / 2f) * CellSize;
    }

    // A nearby node for a point that is on none, searched outwards in square
    // rings up to maxWorldRadius; -1 if there is none that close. Boats start
    // where the mesh is not — clearance keeps nodes a couple of units off
    // every shore, and a city footprint is a hole — so getting ONTO the mesh
    // needs this.
    //
    // It returns the closest node in the first ring that has one, which is
    // near-but-not-strictly-nearest: a ring's corners lie further out than
    // the next ring's sides. That is the right trade for an entry point, and
    // callers that need the true nearest should say so and pay for it.
    public int NearestNode(float wx, float wy, float maxWorldRadius)
    {
        int cx = (int)MathF.Floor(wx / CellSize), cy = (int)MathF.Floor(wy / CellSize);
        int here = NodeAt(cx, cy);
        if (here >= 0) return here;
        int maxRing = (int)MathF.Ceiling(maxWorldRadius / CellSize);
        int best = -1;
        float bestDistSq = float.PositiveInfinity;
        for (int r = 1; r <= maxRing; r++)
        {
            // A ring is only worth walking while its inner edge is in range;
            // the first ring with a hit still has to finish, since a corner
            // cell of a small ring can be further than a side cell of it.
            if ((r - 1) * CellSize > maxWorldRadius) break;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // ring, not disc
                    int node = NodeAt(cx + dx, cy + dy);
                    if (node < 0) continue;
                    float ncx = (cx + dx + 0.5f) * CellSize, ncy = (cy + dy + 0.5f) * CellSize;
                    float ddx = ncx - wx, ddy = ncy - wy;
                    float distSq = ddx * ddx + ddy * ddy;
                    if (distSq < bestDistSq) { bestDistSq = distSq; best = node; }
                }
            if (best >= 0) break;
        }
        return best >= 0 && bestDistSq <= maxWorldRadius * maxWorldRadius ? best : -1;
    }
}
