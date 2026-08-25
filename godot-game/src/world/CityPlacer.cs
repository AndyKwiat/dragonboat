using System;
using System.Collections.Generic;
using System.Text.Json;

// Builds the world's cities at launch: N cities on the coast, 10% large,
// 30% medium, the rest small (large first — the hardest to fit), each
// stamped into the map as a square of City cells whose seaward edge sits
// on the shoreline. No Godot types (unit-tested in tests/).
//
// Placement: the map's coast cells (grass touching real, in-bounds water,
// never the map border) are collected once and shuffled; each city takes the first one that works. The footprint is
// pushed inland from it, away from the water, so the coast cell is on its
// edge. Accept if at least MinLandFraction of the footprint is land (small
// bays get filled in), water is left touching the footprint's outside — a
// city that swallows the whole creek it sits on is landlocked — it's at
// least MinSpacing from every placed city, and it clears the map border by
// MapEdgeMargin.
// If the map runs out of room the land requirement is relaxed in steps; a
// map with no coast at all is an error.
public static class CityPlacer
{
    public const int SmallCells = 60, MediumCells = 100, LargeCells = 140;
    public const double LargeFraction = 0.1, MediumFraction = 0.3;
    public const int MinSpacing = 300;            // centre to centre, cells
    // No city (or its footprint) comes within this many cells of the map's
    // border: off-map counts as water, so the border reads as a fake coast.
    public const int MapEdgeMargin = 200;
    public const float MinLandFraction = 0.9f;

    private static readonly (int dx, int dy)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    public static int CellsFor(string size) => size == "large" ? LargeCells : size == "medium" ? MediumCells : SmallCells;

    // The size mix for a city count: large = 10%, medium = 30% (rounded,
    // large at least 1), the rest small — the old 100-city world's 10/30/60.
    public static List<string> SizesFor(int count)
    {
        int large = Math.Max(1, (int)Math.Round(count * LargeFraction));
        int medium = (int)Math.Round(count * MediumFraction);
        if (large + medium > count) medium = count - large;
        var sizes = new List<string>();
        for (int i = 0; i < large; i++) sizes.Add("large");
        for (int i = 0; i < medium; i++) sizes.Add("medium");
        while (sizes.Count < count) sizes.Add("small");
        return sizes;
    }

    public static List<string> ParseNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var names = new List<string>();
        foreach (var n in doc.RootElement.GetProperty("names").EnumerateArray()) names.Add(n.GetString() ?? "");
        return names;
    }

    // Names dealt without repeats; if the list runs short, numbered suffixes.
    public static List<string> DealNames(List<string> pool, int count, Random rng)
    {
        var shuffled = new List<string>(pool);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }
        var names = new List<string>();
        for (int i = 0; i < count; i++)
            names.Add(i < shuffled.Count ? shuffled[i] : $"{shuffled[i % Math.Max(1, shuffled.Count)]} {i / shuffled.Count + 1}");
        return names;
    }

    public static List<City> Place(TerrainMap map, int count, List<string> namePool, Random rng)
    {
        var sizes = SizesFor(count);
        var names = DealNames(namePool, count, rng);
        var coast = ShuffledCoast(map, rng);
        var cities = new List<City>();
        for (int i = 0; i < sizes.Count; i++)
        {
            int n = CellsFor(sizes[i]);
            var spot = FindSpot(map, coast, n, cities) ?? throw new Exception(
                $"CityPlacer: no coast left for city {i + 1}/{count} ({sizes[i]}, {n}x{n}) — fewer cities or a bigger coastline needed");
            var city = new City { Name = names[i], Size = sizes[i], Cells = n, X = spot.cx + 0.5f, Y = spot.cy + 0.5f };
            Stamp(map, spot.cx, spot.cy, n);
            cities.Add(city);
        }
        return cities;
    }

    // Every grass cell that touches water, in random order. Cells within
    // MapEdgeMargin of the border are skipped, and so is the border itself
    // as a water neighbour — off-map reads as water but is not a coast.
    public static List<(int x, int y)> ShuffledCoast(TerrainMap map, Random rng)
    {
        var coast = new List<(int x, int y)>();
        for (int y = MapEdgeMargin; y < map.Height - MapEdgeMargin; y++)
            for (int x = MapEdgeMargin; x < map.Width - MapEdgeMargin; x++)
            {
                if (map.TerrainAt(x, y) != TerrainMap.Grass) continue;
                foreach (var s in Steps)
                {
                    int nx = x + s.dx, ny = y + s.dy;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height) continue;
                    if (map.IsWater(nx, ny)) { coast.Add((x, y)); break; }
                }
            }
        for (int i = coast.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (coast[i], coast[j]) = (coast[j], coast[i]);
        }
        return coast;
    }

    // The first coast cell whose footprint fits, trying the land requirement
    // in relaxing steps so a crowded map still places every city.
    private static (int cx, int cy)? FindSpot(TerrainMap map, List<(int x, int y)> coast, int n, List<City> placed)
    {
        foreach (float minLand in new[] { MinLandFraction, 0.7f, 0.5f })
            foreach (var (x, y) in coast)
            {
                // Earlier cities may have paved over this cell.
                if (map.TerrainAt(x, y) != TerrainMap.Grass) continue;
                foreach (var s in Steps)
                {
                    if (!map.IsWater(x + s.dx, y + s.dy)) continue;
                    // Water lies in direction s: push the centre the other way so
                    // the footprint's edge sits on this coast cell.
                    int half = n / 2;
                    int cx = x - s.dx * half, cy = y - s.dy * half;
                    if (Fits(map, cx, cy, n, minLand, placed)) return (cx, cy);
                }
            }
        return null;
    }

    public static bool Fits(TerrainMap map, int cx, int cy, int n, float minLand, List<City> placed)
    {
        int x0 = cx - n / 2, y0 = cy - n / 2;
        if (x0 < MapEdgeMargin || y0 < MapEdgeMargin ||
            x0 + n > map.Width - MapEdgeMargin || y0 + n > map.Height - MapEdgeMargin) return false;
        foreach (var c in placed)
        {
            float dx = c.X - (cx + 0.5f), dy = c.Y - (cy + 0.5f);
            if (dx * dx + dy * dy < (float)MinSpacing * MinSpacing) return false;
        }
        int land = 0;
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
            {
                int t = map.TerrainAt(x, y);
                if (t == TerrainMap.City) return false;   // never overlap another city
                if (!TerrainMap.IsWaterIndex(t)) land++;
            }
        if (land < minLand * n * n) return false;
        // The shore has to survive the stamp: water must remain in the ring
        // just outside the footprint, or the city has no harbour.
        for (int x = x0; x < x0 + n; x++)
            if (map.IsWater(x, y0 - 1) || map.IsWater(x, y0 + n)) return true;
        for (int y = y0; y < y0 + n; y++)
            if (map.IsWater(x0 - 1, y) || map.IsWater(x0 + n, y)) return true;
        return false;
    }

    public static void Stamp(TerrainMap map, int cx, int cy, int n)
    {
        int x0 = cx - n / 2, y0 = cy - n / 2;
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
                map.Data[y * map.Width + x] = TerrainMap.City;
    }

    // Does any cell of the footprint touch water (4-adjacent)?
    public static bool TouchesWater(TerrainMap map, City c)
    {
        int n = c.Cells, x0 = (int)c.X - n / 2, y0 = (int)c.Y - n / 2;
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
                foreach (var s in Steps)
                    if (map.IsWater(x + s.dx, y + s.dy)) return true;
        return false;
    }

    // A water spot off a city's shore for the boat: walk out from the
    // footprint's water-facing edge until `clearance` cells of open water
    // surround the point. Returns the point and the outward heading
    // (radians, 0 = east, +y south). Null if the city has no usable shore.
    public static (float x, float y, float heading)? WaterStartNear(TerrainMap map, City c, int clearance)
    {
        int n = c.Cells, x0 = (int)c.X - n / 2, y0 = (int)c.Y - n / 2;
        (float, float, float)? best = null;
        int bestDist = int.MaxValue;
        for (int y = y0; y < y0 + n; y++)
            for (int x = x0; x < x0 + n; x++)
                foreach (var s in Steps)
                {
                    if (!map.IsWater(x + s.dx, y + s.dy)) continue;
                    for (int d = clearance; d < clearance * 6; d++)
                    {
                        int px = x + s.dx * d, py = y + s.dy * d;
                        if (px < 0 || py < 0 || px >= map.Width || py >= map.Height) break;
                        if (!Clear(map, px, py, clearance)) continue;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = (px + 0.5f, py + 0.5f, MathF.Atan2(s.dy, s.dx));
                        }
                        break;
                    }
                }
        return best;
    }

    private static bool Clear(TerrainMap map, int cx, int cy, int r)
    {
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if (!map.IsWater(x, y)) return false;
        return true;
    }
}
