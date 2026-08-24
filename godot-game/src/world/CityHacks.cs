using System;
using System.Collections.Generic;

// ========================== TEMPORARY HACK ==============================
// Cosmetic surgery on the loaded map, applied in Main right after the map
// loads and before anything (terrain, currents, collision) reads it. This
// is NOT an important system — it papers over the current world-generator
// output until a new terrain generation system replaces it, and the whole
// file (plus its one call in Main.cs) should be deleted then.
//
// What it does, per city, using the run's shuffled size:
//   1. City cells (red squares) outside every city's target footprint are
//      turned to grassland.
//   2. An exact footprint of city cells is stamped centred on the city:
//      3x3 small, 4x4 medium, 5x5 large.
//   3. Every city must touch water: if no footprint cell is 4-adjacent to
//      a water cell, a strip of river cells is carved from the footprint
//      to the nearest water.
// ========================================================================
public static class CityHacks
{
    public static void Apply(DMap map, List<City> cities)
    {
        int SizeN(City c) => c.Size == "large" ? 5 : c.Size == "medium" ? 4 : 3;
        (int x0, int y0, int x1, int y1) Rect(City c)
        {
            int n = SizeN(c);
            int cx = (int)MathF.Floor(c.X / Config.TileScale), cy = (int)MathF.Floor(c.Y / Config.TileScale);
            return (cx - n / 2, cy - n / 2, cx - n / 2 + n - 1, cy - n / 2 + n - 1);
        }
        var rects = cities.ConvertAll(Rect);

        // 1. Existing city cells outside every footprint become grassland.
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (map.Data[y * map.Width + x] != DMap.City) continue;
                bool keep = false;
                foreach (var r in rects)
                    if (x >= r.x0 && x <= r.x1 && y >= r.y0 && y <= r.y1) { keep = true; break; }
                if (!keep) map.Data[y * map.Width + x] = DMap.Grassland;
            }

        // 2. Stamp each footprint (over anything, water included — step 3
        // restores a water connection if that mattered).
        foreach (var r in rects)
            for (int y = Math.Max(0, r.y0); y <= Math.Min(map.Height - 1, r.y1); y++)
                for (int x = Math.Max(0, r.x0); x <= Math.Min(map.Width - 1, r.x1); x++)
                    map.Data[y * map.Width + x] = DMap.City;

        // 3. Carve a river strip from any footprint that no longer touches water.
        foreach (var r in rects) EnsureWaterTouch(map, r);
    }

    private static readonly (int dx, int dy)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    private static bool IsWater(DMap map, int x, int y) => Terrain.IsWaterIndex(map.TerrainAt(x, y));

    private static void EnsureWaterTouch(DMap map, (int x0, int y0, int x1, int y1) r)
    {
        // Already adjacent to water anywhere on the footprint's border?
        for (int y = r.y0; y <= r.y1; y++)
            for (int x = r.x0; x <= r.x1; x++)
                foreach (var s in Steps)
                    if (IsWater(map, x + s.dx, y + s.dy)) return;

        // BFS out over land from the footprint to the nearest water cell,
        // then walk the path back turning it into river cells.
        var queue = new Queue<(int x, int y)>();
        var parent = new Dictionary<(int, int), (int, int)>();
        for (int y = r.y0; y <= r.y1; y++)
            for (int x = r.x0; x <= r.x1; x++)
            {
                queue.Enqueue((x, y));
                parent[(x, y)] = (x, y);   // roots point at themselves
            }
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var s in Steps)
            {
                int nx = x + s.dx, ny = y + s.dy;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                if (parent.ContainsKey((nx, ny))) continue;
                if (IsWater(map, nx, ny))
                {
                    // Found the sea/river: pave the way back to the city.
                    var (px, py) = (x, y);
                    while (map.TerrainAt(px, py) != DMap.City)
                    {
                        map.Data[py * map.Width + px] = DMap.River;
                        (px, py) = parent[(px, py)];
                    }
                    return;
                }
                parent[(nx, ny)] = (x, y);
                queue.Enqueue((nx, ny));
            }
        }
    }
}
