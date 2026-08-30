using System;
using System.Collections.Generic;

// Bakes a NavMesh out of a map's tile grid, once, when the map loads.
// Four O(cells) passes; see docs/nav.md for the reasoning and the numbers.
//
//   1. Navigable nav cells. The world is cut into cells of `cellSize` world
//      units. A cell is navigable iff EVERY tile in its block expanded by
//      `clearanceCells` on all sides is water. Expanding the block that way
//      is exactly equivalent to "every tile in the block is at least K tiles
//      from land", because the Chebyshev dilation of a solid box by K is
//      that box grown by K — so this needs no distance transform and no
//      map-sized intermediate buffer, just one early-out scan per cell.
//   2. Water bodies. 4-connected flood fill (not 8: a boat cannot slip
//      through a diagonal gap). Nothing is discarded — deleting "puddles"
//      would be policy, and would make the debug overlay lie about where the
//      water is. Callers threshold with NavMesh.BodySize instead.
//   3. Quadtree merge. The largest power-of-two-aligned square that is
//      wholly navigable becomes a node. No same-body test is needed: a solid
//      square is internally 4-connected, so it is always one body — which
//      holds exactly because pass 2 discards nothing.
//   4. Adjacency, into CSR arrays.
//
// WHAT THE MESH PROMISES, AND WHAT IT DOES NOT: `clearanceCells` is
// half-BEAM clearance. At the default 2 it is the radius of the collision
// circles Boat.CollideWithLand uses, so a boat's centre anywhere inside a
// node is clear of land. But the hull is three such circles at ±Length/3 and
// reaches 6 units from the centre, so a bow can still find a rock at the
// wrong heading. Guaranteeing otherwise would need clearance 6, and 4
// already severs the pathingTest river network. Steering still owns
// collision; the mesh promises a channel the beam fits through.
//
// Build AFTER CityPlacer.Place: stamping a city writes City tiles into
// map.Data and can close a creek (tests/NavMeshTests.CityTilesBlockTheNavMesh).
public static class NavMeshBuilder
{
    public static NavMesh Build(TerrainMap map, int cellSize, int clearanceCells, bool offMapIsLand = true)
    {
        if (cellSize < 1 || (cellSize & (cellSize - 1)) != 0)
            throw new ArgumentException($"nav: cellSize must be a power of two, got {cellSize}");
        if (clearanceCells < 0) throw new ArgumentException($"nav: clearanceCells cannot be negative, got {clearanceCells}");
        if (map.Width % cellSize != 0 || map.Height % cellSize != 0)
            throw new ArgumentException($"nav: cellSize {cellSize} does not divide the {map.Width}x{map.Height} map");
        int cellsX = map.Width / cellSize, cellsY = map.Height / cellSize;
        if (cellsX < 1 || cellsY < 1) throw new ArgumentException($"nav: cellSize {cellSize} is larger than the map");

        var navigable = Navigable(map, cellSize, clearanceCells, offMapIsLand, cellsX, cellsY, out int navigableCells);
        var body = Bodies(navigable, cellsX, cellsY, out var bodySizes);
        var nodes = Merge(navigable, body, cellsX, cellsY);
        var cellToNode = Index(nodes, cellsX, cellsY);
        Adjacency(nodes, cellToNode, cellsX, cellsY, out var starts, out var neighbours);
        return new NavMesh(cellSize, clearanceCells, cellsX, cellsY, nodes, cellToNode,
            starts, neighbours, bodySizes, navigableCells);
    }

    // Pass 1. Every tile of the expanded block must be water. Off-map counts
    // as land by default: TerrainMap.TerrainAt answers Water outside the map,
    // which would mark the whole border navigable and invite a pathfinder to
    // sail off the edge of the world.
    private static bool[] Navigable(TerrainMap map, int cellSize, int k, bool offMapIsLand,
        int cellsX, int cellsY, out int count)
    {
        var navigable = new bool[cellsX * cellsY];
        var data = map.Data;
        int w = map.Width, h = map.Height;
        count = 0;
        for (int cy = 0; cy < cellsY; cy++)
            for (int cx = 0; cx < cellsX; cx++)
            {
                int x0 = cx * cellSize - k, x1 = cx * cellSize + cellSize + k;
                int y0 = cy * cellSize - k, y1 = cy * cellSize + cellSize + k;
                if (x0 < 0 || y0 < 0 || x1 > w || y1 > h)
                {
                    if (offMapIsLand) continue;
                    x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0);
                    x1 = Math.Min(x1, w); y1 = Math.Min(y1, h);
                }
                bool clear = true;
                for (int y = y0; y < y1 && clear; y++)
                {
                    int row = y * w;
                    for (int x = x0; x < x1; x++)
                        if (data[row + x] != TerrainMap.Water) { clear = false; break; }
                }
                if (clear) { navigable[cy * cellsX + cx] = true; count++; }
            }
        return navigable;
    }

    // Pass 2. Body id per navigable cell (-1 elsewhere), 4-connected, with a
    // preallocated stack — a flood fill over 400k cells must never recurse.
    private static int[] Bodies(bool[] navigable, int cellsX, int cellsY, out int[] bodySizes)
    {
        var body = new int[cellsX * cellsY];
        Array.Fill(body, -1);
        var stack = new int[navigable.Length];
        var sizes = new List<int>();
        for (int start = 0; start < navigable.Length; start++)
        {
            if (!navigable[start] || body[start] >= 0) continue;
            int id = sizes.Count, size = 0, top = 0;
            stack[top++] = start;
            body[start] = id;
            while (top > 0)
            {
                int i = stack[--top];
                size++;
                int x = i % cellsX, y = i / cellsX;
                if (x > 0) Push(i - 1);
                if (x < cellsX - 1) Push(i + 1);
                if (y > 0) Push(i - cellsX);
                if (y < cellsY - 1) Push(i + cellsX);
                void Push(int j)
                {
                    if (navigable[j] && body[j] < 0) { body[j] = id; stack[top++] = j; }
                }
            }
            sizes.Add(size);
        }
        bodySizes = sizes.ToArray();
        return body;
    }

    // Pass 3. The quadtree, rooted on the smallest power-of-two square that
    // covers the grid; quadrants entirely off the grid emit nothing, so a
    // non-square or non-power-of-two cell grid works too.
    private static NavNode[] Merge(bool[] navigable, int[] body, int cellsX, int cellsY)
    {
        int root = 1;
        while (root < cellsX || root < cellsY) root *= 2;
        var nodes = new List<NavNode>();
        Emit(0, 0, root);
        return nodes.ToArray();

        // True if the whole square was emitted as one node.
        bool Emit(int x, int y, int size)
        {
            if (x >= cellsX || y >= cellsY) return true;   // off the grid: nothing to cover
            if (x + size <= cellsX && y + size <= cellsY && Uniform(x, y, size))
            {
                nodes.Add(new NavNode(x, y, size, body[y * cellsX + x]));
                return true;
            }
            if (size == 1) return false;
            int h = size / 2;
            Emit(x, y, h); Emit(x + h, y, h); Emit(x, y + h, h); Emit(x + h, y + h, h);
            return false;
        }

        bool Uniform(int x, int y, int size)
        {
            for (int j = y; j < y + size; j++)
            {
                int row = j * cellsX;
                for (int i = x; i < x + size; i++)
                    if (!navigable[row + i]) return false;
            }
            return true;
        }
    }

    private static int[] Index(NavNode[] nodes, int cellsX, int cellsY)
    {
        var cellToNode = new int[cellsX * cellsY];
        Array.Fill(cellToNode, -1);
        for (int n = 0; n < nodes.Length; n++)
        {
            var node = nodes[n];
            for (int y = node.Y; y < node.Y + node.Size; y++)
                for (int x = node.X; x < node.X + node.Size; x++)
                    cellToNode[y * cellsX + x] = n;
        }
        return cellToNode;
    }

    // Pass 4. Walk each node's four borders and look up the node across the
    // edge. Deduping is a consecutive-run check rather than a set: along one
    // edge equal ids are always consecutive, and one leaf cannot touch
    // another on two different sides — a square holding a cell to the left of
    // A and a cell above A would have to hold A itself. No allocation, and
    // so no set-ordering nondeterminism in the output.
    private static void Adjacency(NavNode[] nodes, int[] cellToNode, int cellsX, int cellsY,
        out int[] starts, out int[] neighbours)
    {
        var counts = new int[nodes.Length];
        for (int n = 0; n < nodes.Length; n++) counts[n] = Walk(n, null, 0);

        starts = new int[nodes.Length + 1];
        for (int n = 0; n < nodes.Length; n++) starts[n + 1] = starts[n] + counts[n];
        neighbours = new int[starts[nodes.Length]];
        for (int n = 0; n < nodes.Length; n++) Walk(n, neighbours, starts[n]);

        // Every neighbour of node n, once: written into `into` at `at` when
        // given one, and counted either way.
        int Walk(int n, int[]? into, int at)
        {
            var node = nodes[n];
            int found = 0;
            foreach (var (dx, dy) in Sides)
            {
                int last = -1;
                for (int s = 0; s < node.Size; s++)
                {
                    // The cell just outside this side, opposite step s.
                    int x = dx == 0 ? node.X + s : dx < 0 ? node.X - 1 : node.X + node.Size;
                    int y = dy == 0 ? node.Y + s : dy < 0 ? node.Y - 1 : node.Y + node.Size;
                    if (x < 0 || y < 0 || x >= cellsX || y >= cellsY) continue;
                    int other = cellToNode[y * cellsX + x];
                    if (other < 0 || other == n || other == last) continue;
                    last = other;
                    if (into != null) into[at + found] = other;
                    found++;
                }
            }
            return found;
        }
    }

    // Left, right, up, down: which side of a node a border walk is on.
    private static readonly (int dx, int dy)[] Sides = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    // The one-line summary printed at load, and asserted in tests.
    public static string Describe(NavMesh nav)
    {
        long cells = (long)nav.CellsX * nav.CellsY;
        double pct = cells == 0 ? 0 : 100.0 * nav.NavigableCells / cells;
        return $"Nav mesh: {nav.CellsX * nav.CellSize}² @ cell {nav.CellSize}, clearance {nav.ClearanceCells} — " +
               $"{nav.NavigableCells} cells ({pct:F1}% of the map), " +
               $"{nav.BodyCount} water bod{(nav.BodyCount == 1 ? "y" : "ies")}, {nav.Nodes.Count} nodes";
    }
}
