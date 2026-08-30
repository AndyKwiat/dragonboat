using System;
using System.Collections.Generic;

// A route across the water: a list of world points to steer through, in
// order, ending at the destination. Produced by NavPath.Find, consumed by
// whatever is steering (src/ai/AiBoat.cs).
public sealed class NavRoute
{
    public IReadOnlyList<(float X, float Y)> Waypoints { get; }
    // True when the route ends where it was asked to end, rather than at the
    // last bit of open water before it. A destination inside a city or up a
    // creek too tight for the mesh gets a route that stops short.
    public bool ReachesDestination { get; }

    public NavRoute(IReadOnlyList<(float X, float Y)> waypoints, bool reachesDestination)
    {
        Waypoints = waypoints; ReachesDestination = reachesDestination;
    }

    public float Length()
    {
        float total = 0;
        for (int i = 1; i < Waypoints.Count; i++)
        {
            float dx = Waypoints[i].X - Waypoints[i - 1].X, dy = Waypoints[i].Y - Waypoints[i - 1].Y;
            total += MathF.Sqrt(dx * dx + dy * dy);
        }
        return total;
    }
}

// A* over the nav mesh, then a string-pull to straighten what comes out.
//
// Searching NODES rather than cells is the whole reason the mesh exists: the
// open sea is a handful of huge leaves, so a crossing is a few dozen hops
// instead of a hundred thousand. The raw result is a chain of portal
// midpoints — the middle of each shared edge — which is safe (two adjacent
// nodes are each wholly clear, so their union is clear across the edge) but
// zigzags, because it visits the middle of every edge whether or not the
// turn was needed. The string-pull then drops every waypoint that the boat
// can simply see past, which is what turns a staircase across open water
// into a straight line.
public static class NavPath
{
    // How far from a point we are willing to look for a way onto the mesh:
    // boats sit where the mesh is not (clearance keeps it off every shore).
    public const float DefaultSnapRadius = 48f;

    public static NavRoute? Find(NavMesh nav, float fromX, float fromY, float toX, float toY,
        float snapRadius = DefaultSnapRadius)
    {
        int start = nav.NearestNode(fromX, fromY, snapRadius);
        int goal = nav.NearestNode(toX, toY, snapRadius);
        if (start < 0 || goal < 0) return null;
        if (nav.Nodes[start].Body != nav.Nodes[goal].Body) return null;   // different seas

        var nodes = Search(nav, start, goal);
        if (nodes == null) return null;

        var points = new List<(float X, float Y)> { (fromX, fromY) };
        for (int i = 1; i < nodes.Count; i++)
            points.Add(Portal(nav, nodes[i - 1], nodes[i]));
        // The destination itself, if it can be reached from the water; if not
        // (a quay inside a city, a creek too tight for the mesh) the route
        // ends on the last node instead and says so.
        bool reaches = nav.NodeAtWorld(toX, toY) >= 0
                       || Visible(nav, points[^1].X, points[^1].Y, toX, toY);
        if (reaches) points.Add((toX, toY));
        else { nav.Centre(nodes[^1], out float cx, out float cy); points.Add((cx, cy)); }

        return new NavRoute(StringPull(nav, points), reaches);
    }

    // A* with a binary heap; cost and heuristic are both straight-line
    // distance, so the heuristic never overestimates and the result is a
    // shortest path through the node graph.
    private static List<int>? Search(NavMesh nav, int start, int goal)
    {
        int n = nav.Nodes.Count;
        var cameFrom = new int[n];
        var cost = new float[n];
        var closed = new bool[n];
        Array.Fill(cameFrom, -1);
        Array.Fill(cost, float.PositiveInfinity);
        cost[start] = 0;
        var open = new Heap(n);
        open.Push(start, Distance(nav, start, goal));
        while (open.TryPop(out int current))
        {
            if (current == goal) break;
            if (closed[current]) continue;
            closed[current] = true;
            foreach (int next in nav.NeighboursOf(current))
            {
                if (closed[next]) continue;
                float step = cost[current] + Distance(nav, current, next);
                if (step >= cost[next]) continue;
                cost[next] = step;
                cameFrom[next] = current;
                open.Push(next, step + Distance(nav, next, goal));
            }
        }
        if (float.IsPositiveInfinity(cost[goal])) return null;
        var path = new List<int>();
        for (int at = goal; at >= 0; at = cameFrom[at]) path.Add(at);
        path.Reverse();
        return path;
    }

    private static float Distance(NavMesh nav, int a, int b)
    {
        nav.Centre(a, out float ax, out float ay);
        nav.Centre(b, out float bx, out float by);
        float dx = bx - ax, dy = by - ay;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // The midpoint of the edge two neighbouring nodes share.
    private static (float X, float Y) Portal(NavMesh nav, int a, int b)
    {
        var na = nav.Nodes[a];
        var nb = nav.Nodes[b];
        int c = nav.CellSize;
        // Overlap of the two squares along each axis; on the axis they touch,
        // the overlap is empty and the shared face is the touching edge.
        float x0 = Math.Max(na.X, nb.X), x1 = Math.Min(na.X + na.Size, nb.X + nb.Size);
        float y0 = Math.Max(na.Y, nb.Y), y1 = Math.Min(na.Y + na.Size, nb.Y + nb.Size);
        float px = x0 < x1 ? (x0 + x1) / 2f * c : Math.Max(na.X, nb.X) * (float)c;
        float py = y0 < y1 ? (y0 + y1) / 2f * c : Math.Max(na.Y, nb.Y) * (float)c;
        return (px, py);
    }

    // Drop every waypoint the boat can see straight past. Greedy from the
    // start: keep the furthest point still in line of sight, walk on from
    // there. O(waypoints²) worst case, but a route is dozens of points.
    private static List<(float X, float Y)> StringPull(NavMesh nav, List<(float X, float Y)> points)
    {
        if (points.Count <= 2) return points;
        var pulled = new List<(float X, float Y)> { points[0] };
        int at = 0;
        while (at < points.Count - 1)
        {
            int furthest = at + 1;
            for (int probe = points.Count - 1; probe > at + 1; probe--)
                if (Visible(nav, points[at].X, points[at].Y, points[probe].X, points[probe].Y))
                { furthest = probe; break; }
            pulled.Add(points[furthest]);
            at = furthest;
        }
        return pulled;
    }

    // Is every nav cell under the segment navigable? Stepped at a quarter of
    // a cell, and when a step moves diagonally both cells it could have cut
    // the corner of are checked too — otherwise a route could squeeze
    // through a diagonal gap no boat can use.
    public static bool Visible(NavMesh nav, float ax, float ay, float bx, float by)
    {
        float dx = bx - ax, dy = by - ay;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        int steps = Math.Max(1, (int)MathF.Ceiling(length / (nav.CellSize * 0.25f)));
        int lastCx = int.MinValue, lastCy = int.MinValue;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            int cx = (int)MathF.Floor((ax + dx * t) / nav.CellSize);
            int cy = (int)MathF.Floor((ay + dy * t) / nav.CellSize);
            if (cx == lastCx && cy == lastCy) continue;
            if (nav.NodeAt(cx, cy) < 0) return false;
            if (lastCx != int.MinValue && cx != lastCx && cy != lastCy
                && (nav.NodeAt(lastCx, cy) < 0 || nav.NodeAt(cx, lastCy) < 0)) return false;
            lastCx = cx; lastCy = cy;
        }
        return true;
    }

    // A plain binary min-heap keyed on f-score. Entries are never updated in
    // place: a cheaper route to a node simply pushes it again, and the stale
    // copy is skipped by the closed check when it surfaces.
    private sealed class Heap
    {
        private readonly List<(int Node, float Score)> items;
        public Heap(int capacity) => items = new List<(int, float)>(Math.Min(capacity, 64));

        public void Push(int node, float score)
        {
            items.Add((node, score));
            int i = items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (items[parent].Score <= items[i].Score) break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public bool TryPop(out int node)
        {
            if (items.Count == 0) { node = -1; return false; }
            node = items[0].Node;
            items[0] = items[^1];
            items.RemoveAt(items.Count - 1);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, best = i;
                if (l < items.Count && items[l].Score < items[best].Score) best = l;
                if (r < items.Count && items[r].Score < items[best].Score) best = r;
                if (best == i) break;
                (items[best], items[i]) = (items[i], items[best]);
                i = best;
            }
            return true;
        }
    }
}
