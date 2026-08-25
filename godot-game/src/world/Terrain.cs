using Godot;
using System;

// Gameplay queries against the map: what is under a world point, how high
// the ground is there, and circle-vs-land collision for things that float.
// Purely data — no nodes — so boats, projectiles and (later) AI can share it.
public sealed class Terrain
{
    private readonly TerrainMap map;
    public readonly float MaxHeight;

    public Terrain(TerrainMap map)
    {
        this.map = map;
        MaxHeight = 0;
        foreach (var h in Config.TerrainHeights) MaxHeight = Math.Max(MaxHeight, h);
    }

    public static bool IsWaterIndex(int t) => TerrainMap.IsWaterIndex(t);

    // One cell is one world unit.
    public int TerrainAt(float x, float y) => map.TerrainAt((int)MathF.Floor(x), (int)MathF.Floor(y));

    public bool IsWater(float x, float y) => IsWaterIndex(TerrainAt(x, y));

    // Height of the column top at a world point (0 on water). Beyond the map
    // is ocean.
    public float HeightAt(float x, float y) => Config.TerrainHeights[TerrainAt(x, y)];

    // Push a circle at (x, y) of radius r out of every non-water cell it
    // overlaps. Returns the total push (zero if it was clear); `normal` is the
    // push direction, for velocity response. Cells are axis-aligned squares,
    // so this is closest-point-on-box vs circle, iterated a few times since
    // one push can create another overlap at an inside corner.
    public Vector2 ResolveCircle(ref float x, ref float y, float r, out Vector2 normal)
    {
        var total = Vector2.Zero;
        for (int iter = 0; iter < 4; iter++)
        {
            bool moved = false;
            int cx0 = (int)MathF.Floor(x - r), cx1 = (int)MathF.Floor(x + r);
            int cy0 = (int)MathF.Floor(y - r), cy1 = (int)MathF.Floor(y + r);
            for (int cy = cy0; cy <= cy1; cy++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    if (IsWaterIndex(map.TerrainAt(cx, cy))) continue;
                    float bx0 = cx, by0 = cy, bx1 = bx0 + 1, by1 = by0 + 1;
                    float px = Math.Clamp(x, bx0, bx1), py = Math.Clamp(y, by0, by1);
                    float dx = x - px, dy = y - py;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= r * r) continue;
                    Vector2 push;
                    if (d2 > 1e-8f)
                    {
                        float d = MathF.Sqrt(d2);
                        push = new Vector2(dx / d, dy / d) * (r - d);
                    }
                    else
                    {
                        // Centre inside the cell: leave through the nearest face.
                        float l = x - bx0, rr = bx1 - x, t = y - by0, b = by1 - y;
                        float m = Math.Min(Math.Min(l, rr), Math.Min(t, b));
                        push = m == l ? new Vector2(-(l + r), 0) : m == rr ? new Vector2(rr + r, 0)
                             : m == t ? new Vector2(0, -(t + r)) : new Vector2(0, b + r);
                    }
                    x += push.X; y += push.Y;
                    total += push;
                    moved = true;
                }
            if (!moved) break;
        }
        normal = total.LengthSquared() > 1e-10f ? total.Normalized() : Vector2.Zero;
        return total;
    }

    // Where a ray (Godot space: X = world x, Z = world y, Y = height) first
    // meets the ground — the water plane or the top of a raised column. Steps
    // through the band of heights where columns live, then falls back to the
    // y = 0 plane. False if the ray never comes down.
    public bool RaycastGround(Vector3 origin, Vector3 dir, float maxDistance, out Vector3 hit)
    {
        hit = Vector3.Zero;
        if (dir.Y >= -1e-6f) return false;
        float tTop = (origin.Y - (MaxHeight + 0.01f)) / -dir.Y;   // where the ray drops below the tallest column
        float tZero = origin.Y / -dir.Y;
        if (tZero > maxDistance) return false;
        float t0 = Math.Max(0, tTop);
        const int Steps = 96;
        float dt = (tZero - t0) / Steps;
        float prevT = t0;
        for (int i = 1; i <= Steps; i++)
        {
            float t = t0 + dt * i;
            var p = origin + dir * t;
            float h = HeightAt(p.X, p.Z);
            if (p.Y <= h)
            {
                // Bisect between prevT and t for a cleaner landing height.
                float lo = prevT, hi = t;
                for (int k = 0; k < 6; k++)
                {
                    float mid = (lo + hi) / 2;
                    var q = origin + dir * mid;
                    if (q.Y <= HeightAt(q.X, q.Z)) hi = mid; else lo = mid;
                }
                hit = origin + dir * hi;
                hit.Y = HeightAt(hit.X, hit.Z);
                return true;
            }
            prevT = t;
        }
        hit = origin + dir * tZero;
        hit.Y = 0;
        return true;
    }
}
